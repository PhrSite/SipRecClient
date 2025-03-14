/////////////////////////////////////////////////////////////////////////////////////
//  File:   SrcUserAgent.cs                                         14 Oct 24 PHR
/////////////////////////////////////////////////////////////////////////////////////


namespace SipRecClient;

using Ng911Lib.Utilities;
using SipLib.Body;
using SipLib.Channels;
using SipLib.Core;
using I3V3.LoggingHelpers;
using I3V3.LogEvents;
using SipLib.Logging;
using SipLib.Media;
using SipLib.Rtp;
using SipLib.Sdp;
using SipLib.Transactions;
using SipRecMetaData;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using SipLib.Threading;

/// <summary>
/// SIP Recording Client (SRC) User Agent class. This class handles all calls being recorded by a SIP Recording
/// Server (SRS) using a single, permanent connection to the SRS. It also logs NG9-1-1 I3V3 events.
/// <para>
/// To use this class, construct an instance of it, hook the events and then call the Start() method.
/// </para>
/// <para>
/// Call the Shutdown() method to close all network connections and release resources when the application
/// is closing or when the interface to the SIPREC recorder is no longer needed.
/// </para>
/// </summary>
public class SrcUserAgent : QueuedActionWorkerTask
{
    private SipRecRecorderSettings m_Settings;
    private MediaPortManager m_PortManager;    
    private X509Certificate2 m_Certificate;
    private string m_ElementID;
    private string m_AgencyID;
    private string m_AgentID;
    private bool m_EnableLogging;

    private const string UaName = "SrcUserAgent";

    private IPEndPoint m_LocalEndPoint;
    private SIPChannel m_SipChannel;
    private SipTransport m_SipTransport;

    private bool m_IsStarted = false;
    private bool m_IsShutdown = false;

    /// <summary>
    /// The key is the Call-ID header value of the original call, which is the same as the Call-ID of the call to the SRS.
    /// </summary>
    private Dictionary<string, SrcCall> m_Calls = new Dictionary<string, SrcCall>();

    private bool m_SrsResponding = true;
    private DateTime m_LastOptionsTime = DateTime.Now;
    private SIPResponseStatusCodesEnum m_LastResponseCode = SIPResponseStatusCodesEnum.None;

    private SIPRequest m_OptionsReq;
    private IPEndPoint m_SrsRemoteEndPoint;
    private const int OPTIONS_TIMEOUT_MS = 1000;

    private SIPURI m_SrsUri;
    private I3LogEventClientMgr m_I3LogEventClientMgr;

    /// <summary>
    /// This event is fired when the status of the SRS changes. This event will not be fired if the EnableOptions property
    /// of the SipRecRecorderSettings object for this SRC is false.
    /// </summary>
    public event SrsStatusDelegate? SrsStatusChanged = null;

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="settings">Configuration settings</param>
    /// <param name="portManager">Used for allocating UDP and TCP ports for media streams.</param>
    /// <param name="certificate">X.509 certificate to use for SIP over TLS (SIPS) and MSRP over
    /// TLS (MSRPS). Required even if TLS is not currently in use.</param>
    /// <param name="agencyID">Identity of the agency that is recording and logging calls</param>
    /// <param name="agentID">Identity of the agent or call taker that is recording and logging calls.</param>
    /// <param name="elementID">NG9-1-1 Element Identifier of the entity recording calls.</param>
    /// <param name="i3LogEventClientMgr">Used for logging NG9-1-1 events. Required if NG9-1-1 event logging is required.
    /// If not using NG9-1-1 event logging, pass in a default I3LogEventClientMgr object that contains an empty list
    /// of I3LogEventClient objects.</param>
    /// <param name="enableLogging">If true then I3 event logging is enabled.</param>
    public SrcUserAgent(SipRecRecorderSettings settings, MediaPortManager portManager, X509Certificate2 certificate,
        string agencyID, string agentID, string elementID, I3LogEventClientMgr i3LogEventClientMgr,
        bool enableLogging) : base(10)
    {
        m_Settings = settings;
        m_PortManager = portManager;
        m_Certificate = certificate;
        m_ElementID = elementID;
        m_AgencyID = agencyID;
        m_AgentID = agentID;
        m_I3LogEventClientMgr = i3LogEventClientMgr;
        m_EnableLogging = enableLogging;

        m_LocalEndPoint = IPEndPoint.Parse(m_Settings.LocalIpEndpoint);

        if (m_Settings.SipTransportProtocol == SIPProtocolsEnum.udp)
            m_SipChannel = new SIPUDPChannel(m_LocalEndPoint, UaName, null);
        else if (m_Settings.SipTransportProtocol == SIPProtocolsEnum.tcp)
            m_SipChannel = new SIPTCPChannel(m_LocalEndPoint, UaName, null);
        else
            m_SipChannel = new SIPTLSChannel(m_Certificate, m_LocalEndPoint, UaName);

        m_SipTransport = new SipTransport(m_SipChannel);

        m_SrsRemoteEndPoint = IPEndPoint.Parse(m_Settings.SrsIpEndpoint);
        SIPEndPoint SrsEP = new SIPEndPoint(m_Settings.SipTransportProtocol, m_SrsRemoteEndPoint);
        SIPSchemesEnum SipScheme = m_SipChannel.SIPChannelContactURI.Scheme;
        m_SrsUri = new SIPURI(SipScheme, SrsEP);
        m_SrsUri.User = m_Settings.Name;

        m_OptionsReq = SIPRequest.CreateBasicRequest(SIPMethodsEnum.OPTIONS, m_SrsUri, m_SrsUri, m_Settings.Name, 
            m_SipChannel.SIPChannelContactURI, UaName);
    }

    /// <summary>
    /// Initializes the SIP transport interface to the SRS and starts communication with the SRS.
    /// </summary>
    public override void Start()
    {
        if (m_IsStarted == true || m_IsShutdown == true)
            return;

        m_SipTransport.SipRequestReceived += OnSipRequestReceived;
        m_SipTransport.SipResponseReceived += OnSipResponseReceived;
        m_SipTransport.LogSipRequest += OnLogSipRequest;
        m_SipTransport.LogSipResponse += OnLogSipResponse;

        // Force an OPTIONS request to be sent to the SRS as soon as this SRC is started.
        m_LastOptionsTime = DateTime.Now - TimeSpan.FromSeconds(m_Settings.OptionsIntervalSeconds);

        m_IsStarted = true;
        base.Start();
        m_SipTransport.Start();
    }

    /// <summary>
    /// Shuts down all SIP transport connections and releases resources.
    /// </summary>
    public override async Task Shutdown()
    {
        if (m_IsStarted == false)
            return;

        if (m_IsShutdown == true)
            return;

        m_IsShutdown = true;

        // Terminate any calls that are currently active
        await TerminatAllCallsAsync();

        // Unhook the event handlers
        m_SipTransport.SipRequestReceived -= OnSipRequestReceived;
        m_SipTransport.SipResponseReceived -= OnSipResponseReceived;
        m_SipTransport.LogSipRequest -= OnLogSipRequest;
        m_SipTransport.LogSipResponse -= OnLogSipResponse;
        m_SipTransport.Shutdown();

        await base.Shutdown();
    }

    private async Task TerminatAllCallsAsync()
    {
        SemaphoreSlim semaphore = new SemaphoreSlim(0, 1);
        EnqueueWork(() => 
        {
            foreach (SrcCall srcCall in m_Calls.Values)
            {
                if (srcCall.ClientInviteTransaction != null)
                {   // An OK response or other final response has not been received yet
                    srcCall.ClientInviteTransaction.CancelInvite();
                    // Wait synchronously
                    SipTransactionBase trans = srcCall.ClientInviteTransaction.WaitForCompletionAsync().Result;
                }

                else
                {
                    SIPRequest ByeRequest = SipUtils.BuildByeRequest(srcCall.Invite, m_SipChannel, m_SrsRemoteEndPoint,
                        false, srcCall.LastCSeq, srcCall.OkResponse!);
                        
                    ClientNonInviteTransaction Cnit = m_SipTransport.StartClientNonInviteTransaction(ByeRequest, 
                        m_SrsRemoteEndPoint, null, 1000);
                    // Wait synchronously
                    SipTransactionBase Stb = Cnit.WaitForCompletionAsync().Result;
                    srcCall.ShutdownMediaConnections();
                    SendRecCallEndLogEvent(srcCall);
                }
            }

            semaphore.Release();
        });

        await semaphore.WaitAsync();
    }

    protected override void DoTimedEvents()
    {
        if (m_IsShutdown == true)
            return;

        DateTime Now = DateTime.Now;
        if (m_Settings.EnableOptions == true && m_Settings.Enabled == true)
        {
            if (Now >= (m_LastOptionsTime + TimeSpan.FromSeconds(m_Settings.OptionsIntervalSeconds)))
            {
                m_OptionsReq.Header.CSeq += 1;
                m_OptionsReq.Header.Vias.TopViaHeader!.Branch = CallProperties.CreateBranchId();
                m_SipTransport.StartClientNonInviteTransaction(m_OptionsReq, m_SrsRemoteEndPoint, OnOptionsRequestComplete,
                    OPTIONS_TIMEOUT_MS);
                m_LastOptionsTime = Now;
            }
        }
    }

    /// <summary>
    /// Gets the status of the SIP Recording Server (SRS)
    /// </summary>
    public bool SrsResponding
    {
        get { return m_SrsResponding; }
    }

    private void OnOptionsRequestComplete(SIPRequest sipRequest, SIPResponse? sipResponse,
        IPEndPoint remoteEndPoint, SipTransport sipTransport, SipTransactionBase Transaction)
    {
        if (sipResponse == null)
        {   // No response was received.
            if (m_SrsResponding == true)
                SrsStatusChanged?.Invoke(m_Settings.Name, false, SIPResponseStatusCodesEnum.None);

            m_SrsResponding = false;
            m_LastResponseCode = SIPResponseStatusCodesEnum.None;
        }
        else
        {
            if (m_SrsResponding == false || m_LastResponseCode != sipResponse.Status)
                SrsStatusChanged?.Invoke(m_Settings.Name, true, sipResponse.Status);

            m_SrsResponding = true;
            m_LastResponseCode = sipResponse.Status;
        }
    }

    /// <summary>
    /// Gets the Call object for a specified call ID
    /// </summary>
    /// <param name="callID">Call-ID header value for the call.</param>
    /// <returns>Returns the SrcCall object if it exists or null if it does not</returns>
    private SrcCall? GetCall(string callID)
    {
        try
        {
            return m_Calls.GetValueOrDefault(callID);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the Enabled setting
    /// </summary>
    public bool Enabled
    {
        get { return m_Settings.Enabled; }
    }

    /// <summary>
    /// Starts the SIPREC recording process for a new call
    /// </summary>
    /// <param name="srcCallParameters">Contains the parameters for the new call</param>
    public void StartRecording(SrcCallParameters srcCallParameters)
    {
        EnqueueWork(() => { DoStartRecording(srcCallParameters); });
    }

    private void DoStartRecording(SrcCallParameters srcCallParameters)
    {
        SIPRequest Invite = BuildInitialInviteRequest(srcCallParameters, out Sdp offerSdp,
            out recording Recording);
        SrcCall srcCall = new SrcCall(srcCallParameters, Invite, offerSdp, Recording, UaName, m_Certificate,
            m_I3LogEventClientMgr, m_EnableLogging, m_ElementID, m_AgencyID, m_AgentID, m_SrsRemoteEndPoint);
        m_Calls.Add(srcCallParameters.CallId, srcCall);

        srcCall.ClientInviteTransaction = m_SipTransport.StartClientInvite(Invite, m_SrsRemoteEndPoint,
            OnInviteTransactionComplete, null);
    }

    private void OnInviteTransactionComplete(SIPRequest sipRequest, SIPResponse? sipResponse,
        IPEndPoint remoteEndPoint, SipTransport sipTransport, SipTransactionBase Transaction)
    {
        EnqueueWork(() => {
            DoInviteTransactionComplete(sipRequest, sipResponse, remoteEndPoint, sipTransport, Transaction);
        });
    }   

    private void DoInviteTransactionComplete(SIPRequest sipRequest, SIPResponse? sipResponse,
        IPEndPoint remoteEndPoint, SipTransport sipTransport, SipTransactionBase Transaction)
    {
        SrcCall? srcCall = GetCall(sipRequest.Header.CallId);
        if (srcCall == null)
        {   // Error: Call not found
            SipLogger.LogError($"SIPREC call to SRS at {remoteEndPoint} not found");
            return;
        }

        srcCall.ClientInviteTransaction = null;     // The client INVITE transaction is complete

        if (sipResponse == null)
        {   // The SRS did not respond to the INVITE request
            SipLogger.LogError($"INVITE to SRS at {remoteEndPoint} for Call_ID = {sipRequest.Header.CallId} " +
                "timed out");
            m_Calls.Remove(sipRequest.Header.CallId);
            return;
        }

        if (sipResponse.Status != SIPResponseStatusCodesEnum.Ok)
        {   // The SRS rejected the SIPREC call
            SipLogger.LogError($"The SRS at {remoteEndPoint} rejected Call-ID = {sipRequest.Header.CallId} " +
                $"with a status of {sipResponse.StatusCode}");
            m_Calls.Remove(sipRequest.Header.CallId);
            return;
        }

        string? strAnsweredSdp = sipResponse.GetContentsOfType(SipLib.Body.ContentTypes.Sdp);
        if (strAnsweredSdp == null)
        {   // Error: No answered SDP in the 200 OK response
            SipLogger.LogError($"The SRS at {remoteEndPoint} did not answer Call-ID = {sipRequest.Header.CallId} " +
                "with SDP");
            // Terminate the call
            DoStopRecording(sipRequest.Header.CallId);
            return;
        }

        Sdp AnsweredSdp = Sdp.ParseSDP(strAnsweredSdp);
        if (AnsweredSdp.Media.Count != srcCall.OfferedSdp.Media.Count)
        {
            SipLogger.LogError($"The SRS at {remoteEndPoint} send invalid SDP for Call-ID = {sipRequest.Header.CallId}");
            DoStopRecording(sipRequest.Header.CallId);
            return;
        }

        srcCall.OkResponse = sipResponse;
        srcCall.AnsweredSdp = AnsweredSdp;
        srcCall.Invite.Header.To!.ToTag = sipResponse.Header.To!.ToTag;

        SendRecCallStartLogEvent(srcCall);
        srcCall.SetupMediaChannels();
    }

    /// <summary>
    /// Call this method after the call being recorded has been re-invited. A re-INVITE can occur in order to
    /// re-target media and/or to add new media to an existing call.
    /// <para>
    /// If the call being recorded is re-invited just to retarget existing media, then it is necessary to
    /// hook the events of any RtpChannel and MsrpConnection objects of the call and it is not necessary to
    /// send a re-INVITE to the SRS or to rebuild the SIPREC metadata.
    /// </para>
    /// <para>
    /// If media is added to the call being recorded, then it is necessary to send a re-INVITE request to the
    /// SRS with updated SIPREC metadata.
    /// </para>
    /// </summary>
    /// <param name="newSrcCallParameters">Updated parameters for the SRC call</param>
    public void HandleReInvite(SrcCallParameters newSrcCallParameters)
    {
        // Execute it on the task context for this object.
        EnqueueWork(() => DoHandleReInvite(newSrcCallParameters));
    }

    private void DoHandleReInvite(SrcCallParameters newSrcCallParameters)
    {
        SrcCall? srcCall = GetCall(newSrcCallParameters.CallId);
        if (srcCall == null)
            // The call to the SRS may have already ended because of a BYE request from the SRS or due to
            // forced termination by this SrcUserAgent due to an error.
            return;

        // Check for re-targeted media first.
        SrcCallParameters origParams = srcCall.CallParameters;
        if (newSrcCallParameters.CallRtpChannels.Count < origParams.CallRtpChannels.Count)
        {   // The number or RtpChannels for the re-INVITE should never be less than the number from the
            // original INVITE or the last re-INVITE request.
            SipLogger.LogError($"Invalid number of RtpChannels for re-INVITE for Call-ID = {newSrcCallParameters.CallId}");
            return;
        }

        if (newSrcCallParameters.AnsweredSdp.Media.Count < origParams.AnsweredSdp.Media.Count)
        {   // The number of MediaDescription blocks for the re-INVITE should never be less than the number
            // from the original INVITE or the last re-INVITE request.
            SipLogger.LogError($"Invalid number of MediaDescription blocks for re-INVITE for Call-ID = " +
                $"{newSrcCallParameters.CallId}");
            return;
        }

        int i;
        for (i=0; i < origParams.CallRtpChannels.Count; i++)
        {
            if (origParams.CallRtpChannels[i] != newSrcCallParameters.CallRtpChannels[i])
            {   // A new RtpChannel object was created as a result of the re-INVITE of the call that is
                // being recorded, so it is necessary to un-hook the events of the original RtpChannel object
                // and hook the events of the new RtpChannel object.
                srcCall.ReHookRtpChannelEvents(origParams.CallRtpChannels[i], newSrcCallParameters.CallRtpChannels[i]);
            }
        }

        if (origParams.CallMsrpConnection != null && newSrcCallParameters.CallMsrpConnection != null)
        {   // The original call has MSRP and so does the re-invited call
            if (origParams.CallMsrpConnection != newSrcCallParameters.CallMsrpConnection)
            {   // And the MSRP media was re-targeted
                srcCall.ReHookMsrpChannelEvents(origParams.CallMsrpConnection, newSrcCallParameters.CallMsrpConnection);
            }
        }

        if (origParams.AnsweredSdp.Media.Count == newSrcCallParameters.AnsweredSdp.Media.Count)
        {   // No media was added to the call as a result of the re-INVITE, so its not necessary to send a
            // re-INVITE request to the SRS and the call's metadata does not need to be updated.
            srcCall.CallParameters = newSrcCallParameters;
            return;
        }

        // It is necessary to send a re-INVITE to the SRS to add new media to the call.
        srcCall.ReInviteInProgress = true;
        srcCall.NewMedia.Clear();
        int delta = newSrcCallParameters.AnsweredSdp.Media.Count - origParams.AnsweredSdp.Media.Count;
        int StartIndex = newSrcCallParameters.AnsweredSdp.Media.Count - delta;
        for (i = StartIndex; i < newSrcCallParameters.AnsweredSdp.Media.Count; i++)
        {
            MediaDescription mediaDescription = newSrcCallParameters.AnsweredSdp.Media[i];
            srcCall.NewMedia.Add(mediaDescription.MediaType);
            if (mediaDescription.MediaType == MediaTypes.MSRP)
            {
                srcCall.OfferedSdp.Media.Add(BuildOfferMsrpMediaDescription(mediaDescription, true));
                srcCall.OfferedSdp.Media.Add(BuildOfferMsrpMediaDescription(mediaDescription, false));
            }
            else
            {   // The media type is audio, video or RTT
                srcCall.OfferedSdp.Media.Add(BuildOfferRtpMediaDescription(mediaDescription, true));
                srcCall.OfferedSdp.Media.Add(BuildOfferRtpMediaDescription(mediaDescription, false));
            }
        }

        // Update the SIPREC metadata for new media streams that are being added to the call
        // recording Recording = BuildMetaData(newSrcCallParameters, newSrcCallParameters.AnsweredSdp);
        foreach (string newMediaType in srcCall.NewMedia)
        {
            AddNewMediaStreamsToMetaData(srcCall.Recording, srcCall.CallParameters, newMediaType);
        }

        srcCall.CallParameters = newSrcCallParameters;

        // Attach the SDP and the SIPREC metadata to the body of the INVITE request
        SipBodyBuilder sipBodyBuilder = new SipBodyBuilder();
        sipBodyBuilder.AddContent(SipLib.Body.ContentTypes.Sdp, srcCall.OfferedSdp.ToString(), null, null);
        sipBodyBuilder.AddContent(recording.ContentType, XmlHelper.SerializeToString(srcCall.Recording), null, null);
        sipBodyBuilder.AttachMessageBody(srcCall.Invite);

        srcCall.LastCSeq += 1;
        srcCall.Invite.Header.CSeq = srcCall.LastCSeq;
        srcCall.Invite.Header!.To!.ToTag = srcCall.OkResponse!.Header!.To!.ToTag;
        srcCall.Invite.Header!.Vias!.TopViaHeader!.Branch = CallProperties.CreateBranchId();

        srcCall.ClientInviteTransaction = m_SipTransport.StartClientInvite(srcCall.Invite, m_SrsRemoteEndPoint,
            OnReInviteTransactionComplete, null);
    }

    private void OnReInviteTransactionComplete(SIPRequest sipRequest, SIPResponse? sipResponse,
        IPEndPoint remoteEndPoint, SipTransport sipTransport, SipTransactionBase Transaction)
    {
        EnqueueWork(() => DoReInviteTransactionComplete(sipRequest, sipResponse, remoteEndPoint, sipTransport, Transaction));
    }

    private void DoReInviteTransactionComplete(SIPRequest sipRequest, SIPResponse? sipResponse,
        IPEndPoint remoteEndPoint, SipTransport sipTransport, SipTransactionBase Transaction)
    {
        SrcCall? srcCall = GetCall(sipRequest.Header.CallId);
        if (srcCall == null)
        {   // Error: Call not found -- It may have already ended.
            return;
        }

        if (srcCall.ReInviteInProgress == false)
        {   // Unexpected
            SipLogger.LogError($"Re-INVITE not in progress for Call-ID = {srcCall.CallParameters.CallId}");
            return;
        }

        srcCall.ClientInviteTransaction = null;     // The client re-INVITE transaction is complete
        srcCall.ReInviteInProgress = false;

        if (sipResponse == null)
        {   // The re-INVITE transaction failed
            SipLogger.LogError($"Re-INVITE failed for SRS = {m_Settings.Name}, Call-ID = {srcCall.CallParameters.CallId}");
            return;
        }

        if (sipResponse.Status != SIPResponseStatusCodesEnum.Ok)
        {   // The SRS rejected the re-INVITE request
            SipLogger.LogError($"The SRS '{m_Settings.Name}' rejected a re-INVITE for Call-ID = {srcCall.CallParameters.CallId} " +
                $"with a response code of {sipResponse.StatusCode}");
            return;
        }

        string? strAnsweredSdp = sipResponse.GetContentsOfType(SipLib.Body.ContentTypes.Sdp);
        if (strAnsweredSdp == null)
        {   // Error: The SRS did not provide SDP data with the OK response
            SipLogger.LogError($"The SRS '{m_Settings.Name}' did not provide SDP data for a re-INVITE for " +
                $"Call-ID = {srcCall.CallParameters.CallId}");
            return;
        }

        Sdp AnsweredSdp = Sdp.ParseSDP(strAnsweredSdp);
        if (AnsweredSdp.Media.Count != srcCall.OfferedSdp.Media.Count)
        {
            SipLogger.LogError($"Media mismatch for re-INVITE to SRS '{m_Settings.Name}' for Call-ID = " +
                $"{srcCall.CallParameters.CallId}");
            return;
        }

        srcCall.AnsweredSdp = AnsweredSdp;

        foreach (MediaDescription AnsweredMd in AnsweredSdp.Media)
        {
            if (AnsweredMd.Port == 0)
                continue;       // This media port was rejected by the SRS

            if (srcCall.NewMedia.Contains(AnsweredMd.MediaType) == true)
            {   // This media type is being added to the call
                if (string.IsNullOrEmpty(AnsweredMd.Label) == true)
                {
                    SipLogger.LogError($"No Label attribute in re-INVITE response from SRS '{m_Settings.Name}' " +
                        $"for media type = {AnsweredMd.MediaType} for Call-ID = {srcCall.CallParameters.CallId}");
                    continue;
                }

                // Get the offered MediaDescription matching this media type and this label
                MediaDescription? OfferedMd = srcCall.OfferedSdp.GetMediaByTypeAndLabel(AnsweredMd.MediaType,
                    AnsweredMd.Label);
                if (OfferedMd == null)
                {
                    SipLogger.LogError($"Offered MediaDescription not found in re-INVITE response from SRS = " +
                        $"'{m_Settings.Name}', MediaType = {AnsweredMd.MediaType}, Label = {AnsweredMd.Label}, " +
                        $"Call-ID = {srcCall.CallParameters.CallId}");
                    continue;
                }

                srcCall.SetupChannelForAddedMedia(OfferedMd, AnsweredMd);
            }
        }

        foreach (string newMediaType in srcCall.NewMedia)
        {
            srcCall.HookEventsForNewMedia(newMediaType);
        }
    }

    private void SendRecCallStartLogEvent(SrcCall srcCall)
    {
        if (m_EnableLogging == false)
            return;

        RecCallStartLogEvent Rcsle = new RecCallStartLogEvent();
        SetCallLogEventParams(srcCall, Rcsle);
        Rcsle.direction = "outgoing";
        m_I3LogEventClientMgr.SendLogEvent(Rcsle);
    }

    private void SetCallLogEventParams(SrcCall call, LogEvent logEvent)
    {
        logEvent.elementId = m_ElementID;
        logEvent.agencyId = m_AgentID;
        logEvent.agencyAgentId = m_AgentID;
        logEvent.callId = call.CallParameters.EmergencyCallIdentifier;
        logEvent.incidentId = call.CallParameters.EmergencyIncidentIdentifier;
        logEvent.callIdSip = call.Invite.Header.CallId;
        logEvent.ipAddressPort = m_SrsRemoteEndPoint.ToString();
    }

    private SIPRequest BuildInitialInviteRequest(SrcCallParameters srcCallParameters, out Sdp offerSdp,
        out recording Recording)
    {
        SIPRequest Invite = SIPRequest.CreateBasicRequest(SIPMethodsEnum.INVITE, m_SrsUri, m_SrsUri,
            m_Settings.Name, m_SipChannel.SIPChannelContactURI, UaName);
        // Add the +sip.src feature tag to the Contact header. See Section 6.1.1 of RFC 7866.
        if (Invite.Header.Contact != null && Invite.Header.Contact.Count > 0)
            Invite.Header.Contact[0].ContactParameters.Set("+sip-src", null);

        // Use the same Call-ID as the original call
        Invite.Header.CallId = srcCallParameters.CallId;

        // Add the headers required for SIPREC per RFC 7866
        Invite.Header.Require = "siprec";
        Invite.Header.Accept = "application/sdp, application/rs-metadata, application/rs-metadata-request";

        if (string.IsNullOrEmpty(srcCallParameters.EmergencyCallIdentifier) == false)
            SipUtils.AddEmergencyIdUrnCallInfoHeader(Invite, srcCallParameters.EmergencyCallIdentifier,
                "emergency-CallId");

        if (string.IsNullOrEmpty(srcCallParameters.EmergencyIncidentIdentifier) == false)
            SipUtils.AddEmergencyIdUrnCallInfoHeader(Invite, srcCallParameters.EmergencyIncidentIdentifier,
                "emergency-IncidentId");

        // Build the SDP to offer to the SRS
        offerSdp = new Sdp(m_LocalEndPoint.Address, UaName);
        foreach (MediaDescription mediaDescription in srcCallParameters.AnsweredSdp.Media)
        {
            if (mediaDescription.Port == 0)
                // This media type was rejected in the original call so it cannot be recorded
                continue;

            if (mediaDescription.MediaType == MediaTypes.MSRP)
            {
                offerSdp.Media.Add(BuildOfferMsrpMediaDescription(mediaDescription, true));
                offerSdp.Media.Add(BuildOfferMsrpMediaDescription(mediaDescription, false));
            }
            else
            {   // The media type is audio, video or RTT
                offerSdp.Media.Add(BuildOfferRtpMediaDescription(mediaDescription, true));
                offerSdp.Media.Add(BuildOfferRtpMediaDescription(mediaDescription, false));
            }
        }

        // Build the initial SIPREC metadata
        Recording = BuildMetaData(srcCallParameters, srcCallParameters.AnsweredSdp);

        // Attach the SDP and the SIPREC metadata to the body of the INVITE request
        SipBodyBuilder sipBodyBuilder = new SipBodyBuilder();
        sipBodyBuilder.AddContent(SipLib.Body.ContentTypes.Sdp, offerSdp.ToString(), null, null);
        sipBodyBuilder.AddContent(recording.ContentType, XmlHelper.SerializeToString(Recording), null, null);
        sipBodyBuilder.AttachMessageBody(Invite);

        return Invite;
    }

    private recording BuildMetaData(SrcCallParameters srcCallParameters, Sdp answeredSdp)
    {
        DateTime Now = DateTime.Now;

        recording Recording = new recording();
        Recording.datamode = dataMode.complete;
        group Group = new group();
        Recording.groups.Add(Group);
        Group.associatetime = Now;
        session Session = new session();
        Session.starttime = Now;
        Session.sipSessionID.Add(srcCallParameters.CallId);

        // Assign the session object to the Communication Session Group.
        Session.groupref = Group.group_id;
        Recording.sessions.Add(Session);
        sessionrecordingassoc Sra = new sessionrecordingassoc();
        Sra.associatetime = Now;
        Sra.session_id = Session.session_id;
        Recording.sessionrecordingassocs.Add(Sra);

        // Create the participants
        participant FromParticipant = new participant();
        nameID FromNameId = new nameID();
        FromNameId.aor = srcCallParameters.FromUri.ToString();
        FromNameId.name = new name();
        FromNameId.name.Value = srcCallParameters.FromUri.User != null ? srcCallParameters.FromUri.User : srcCallParameters.FromUri.Host;
        FromParticipant.nameIDs.Add(FromNameId);
        Recording.participants.Add(FromParticipant);

        participant ToParticipant = new participant();
        nameID ToNameId = new nameID();
        ToNameId.aor = srcCallParameters.ToUri.ToString();
        ToNameId.name = new name();
        ToNameId.name.Value = srcCallParameters.ToUri.User != null ? srcCallParameters.ToUri.User : srcCallParameters.ToUri.Host;
        ToParticipant.nameIDs.Add(ToNameId);
        Recording.participants.Add(ToParticipant);

        // Associate the participants with the session.
        participantsessionassoc FromPsa = new participantsessionassoc();
        FromPsa.associatetime = Now;
        FromPsa.participant_id = FromParticipant.participant_id;
        FromPsa.session_id = Session.session_id;
        Recording.participantsessionassocs.Add(FromPsa);

        participantsessionassoc ToPsa = new participantsessionassoc();
        ToPsa.associatetime = Now;
        ToPsa.participant_id = ToParticipant.participant_id;
        ToPsa.session_id = Session.session_id;
        Recording.participantsessionassocs.Add(ToPsa);

        // Create the participant stream associations.
        participantstreamassoc FromParticipantSteamAssoc = new participantstreamassoc();
        FromParticipantSteamAssoc.associatetime = Now;
        FromParticipantSteamAssoc.participant_id = FromParticipant.participant_id;
        Recording.participantstreamassocs.Add(FromParticipantSteamAssoc);

        participantstreamassoc ToParticipantStreamAssoc = new participantstreamassoc();
        ToParticipantStreamAssoc.associatetime = Now;
        ToParticipantStreamAssoc.participant_id = ToParticipant.participant_id;
        Recording.participantstreamassocs.Add(ToParticipantStreamAssoc);

        // Create the streams and build the associations
        foreach (MediaDescription mediaDescription in answeredSdp.Media)
        {
            if (mediaDescription.Port == 0)
                continue;

            switch (mediaDescription.MediaType)
            {
                case MediaTypes.Audio:
                    BuildAndAssociateStreams(Recording, MediaLabel.ReceivedAudio, MediaLabel.SentAudio,
                        Session, FromParticipantSteamAssoc, ToParticipantStreamAssoc);
                    break;
                case MediaTypes.Video:
                    BuildAndAssociateStreams(Recording, MediaLabel.ReceivedVideo, MediaLabel.SentVideo,
                        Session, FromParticipantSteamAssoc, ToParticipantStreamAssoc);
                    break;
                case MediaTypes.RTT:
                    BuildAndAssociateStreams(Recording, MediaLabel.ReceivedRTT, MediaLabel.SentRTT,
                        Session, FromParticipantSteamAssoc, ToParticipantStreamAssoc);
                    break;
                case MediaTypes.MSRP:
                    BuildAndAssociateStreams(Recording, MediaLabel.ReceivedMsrp, MediaLabel.SentMsrp,
                        Session, FromParticipantSteamAssoc, ToParticipantStreamAssoc);
                    break;
            }
        }

        return Recording;
    }

    private void AddNewMediaStreamsToMetaData(recording Recording, SrcCallParameters srcCallParameters,
        string mediaType)
    {
        string FromAor = srcCallParameters.FromUri.ToString();
        string ToAor = srcCallParameters.ToUri.ToString();
        participantstreamassoc? FromPsa = GetParticipantStreamAssociation(Recording, GetParticipantFromAor(Recording, FromAor));
        participantstreamassoc? ToPsa = GetParticipantStreamAssociation(Recording, GetParticipantFromAor(Recording, ToAor));
        if (FromPsa == null)
        {
            SipLogger.LogError($"Could not find the participantstreamassociation for participant AOR = {FromPsa}");
            return;
        }

        if (ToPsa == null)
        {
            SipLogger.LogError($"Could not find the participantstreamassociation for participant AOR = {ToPsa}");
            return;
        }

        MediaLabel ReceivedLabel = MediaLabel.ReceivedAudio;
        MediaLabel SentLabel = MediaLabel.SentAudio;
        switch (mediaType)
        {
            case MediaTypes.Audio:
                ReceivedLabel = MediaLabel.ReceivedAudio;
                SentLabel = MediaLabel.SentAudio;
                break;
            case MediaTypes.Video:
                ReceivedLabel = MediaLabel.ReceivedVideo;
                SentLabel = MediaLabel.SentVideo;
                break;
            case MediaTypes.RTT:
                ReceivedLabel= MediaLabel.ReceivedRTT;
                SentLabel= MediaLabel.SentRTT;
                break;
            case MediaTypes.MSRP:
                ReceivedLabel = MediaLabel.ReceivedMsrp;
                SentLabel = MediaLabel.SentMsrp;
                break;
        }

        BuildAndAssociateStreams(Recording, ReceivedLabel, SentLabel, Recording.sessions[0], FromPsa, ToPsa);
    }


    private participant? GetParticipantFromAor(recording Recording, string strAor)
    {
        participant? Participant = null;
        foreach (participant part in Recording.participants)
        {
            foreach (nameID nid in part.nameIDs)
            {
                if (nid.aor == strAor)
                {
                    Participant = part;
                    break;
                }
            }
        }

        return Participant;
    }

    private participantstreamassoc? GetParticipantStreamAssociation(recording Recording, participant? Participant)
    {
        if (Participant == null)
            return null;

        participantstreamassoc? Psa = null;
        foreach (participantstreamassoc p in Recording.participantstreamassocs)
        {
            if (p.participant_id == Participant.participant_id)
            {
                Psa = p;
                break;
            }
        }
        return Psa;
    }


    private void BuildAndAssociateStreams(recording Recording, MediaLabel ReceiveLabel,  MediaLabel SendLabel,
        session Session, participantstreamassoc FromPsa, participantstreamassoc ToPsa)
    {
        stream ReceiveStream = new stream();
        ReceiveStream.session_id = Session.session_id;
        ReceiveStream.label = ((int) ReceiveLabel).ToString();
        Recording.streams.Add(ReceiveStream);

        stream SendStream = new stream();
        SendStream.session_id = Session.session_id;
        SendStream.label = ((int) SendLabel).ToString();
        Recording.streams.Add(SendStream);

        FromPsa.recv.Add(SendStream.stream_id);
        FromPsa.send.Add(ReceiveStream.stream_id);
        ToPsa.recv.Add(ReceiveStream.stream_id);
        ToPsa.send.Add(SendStream.stream_id);
    }

    private MediaDescription BuildOfferRtpMediaDescription(MediaDescription Original, bool ForReceived)
    {
        MediaDescription offerMd = Original.CreateCopy();
        MediaLabel label = MediaLabel.ReceivedAudio;

        switch (Original.MediaType)
        {
            case MediaTypes.Audio:
                offerMd.Port = m_PortManager.NextAudioPort;
                label = ForReceived == true ? MediaLabel.ReceivedAudio : MediaLabel.SentAudio;
                break;
            case MediaTypes.Video:
                offerMd.Port = m_PortManager.NextVideoPort;
                label = ForReceived == true ? MediaLabel.ReceivedVideo : MediaLabel.SentVideo;
                break;
            case MediaTypes.RTT:
                offerMd.Port = m_PortManager.NextRttPort;
                label = ForReceived == true ? MediaLabel.ReceivedRTT : MediaLabel.SentRTT;
                break;
        }

        offerMd.Label = SipRecUtils.MediaLabelToString(label);
        offerMd.MediaDirection = MediaDirectionEnum.sendonly;

        // Handle media encryption
        if (m_Settings.RtpEncryption == RtpEncryptionEnum.SdesSrtp)
            SdpUtils.AddSdesSrtpEncryption(offerMd);
        else if (m_Settings.RtpEncryption == RtpEncryptionEnum.DtlsSrtp && RtpChannel.CertificateFingerprint != null)
            SdpUtils.AddDtlsSrtp(offerMd, RtpChannel.CertificateFingerprint);

        return offerMd;
    }

    private MediaDescription BuildOfferMsrpMediaDescription(MediaDescription Original, bool ForReceived)
    {
        MediaDescription offerMd = SdpUtils.CreateMsrpMediaDescription(m_LocalEndPoint.Address,
            m_PortManager.NextMsrpPort, m_Settings.MsrpEncryption == MsrpEncryptionEnum.Msrps ?
            true : false, SetupType.active, m_Certificate, UaName);
        offerMd.MediaDirection = MediaDirectionEnum.sendonly;
        MediaLabel label = ForReceived == true ? MediaLabel.ReceivedMsrp : MediaLabel.SentMsrp;
        offerMd.Label = SipRecUtils.MediaLabelToString(label);
        SdpAttribute? AcceptTypes = Original.GetNamedAttribute("accept-types");
        if (AcceptTypes != null)
            offerMd.Attributes.Add(AcceptTypes);

        return offerMd;
    }

    /// <summary>
    /// Call this method to stop recording when a call has ended.
    /// </summary>
    /// <param name="strCallId">Call-ID for the call.</param>
    public void StopRecording(string strCallId)
    {
        EnqueueWork(() => { DoStopRecording(strCallId); });
    }

    private void DoStopRecording(string strCallId)
    {
        SrcCall? srcCall = GetCall(strCallId);
        if (srcCall == null)
            return;     // SRS call may have already ended.

        if (srcCall.ClientInviteTransaction != null)
        {   // An OK response has not been received yet
            srcCall.ClientInviteTransaction.CancelInvite();
            return;
        }

        SIPRequest ByeRequest = SipUtils.BuildByeRequest(srcCall.Invite, m_SipChannel, m_SrsRemoteEndPoint,
            false, srcCall.LastCSeq, srcCall.OkResponse!);
        // Fire and forget the BYE request transaction
        m_SipTransport.StartClientNonInviteTransaction(ByeRequest, m_SrsRemoteEndPoint, null, 1000);
        srcCall.ShutdownMediaConnections();
        EnqueueWork(() => SendRecCallEndLogEvent(srcCall));
        m_Calls.Remove(strCallId);
    }

    private void SendRecCallEndLogEvent(SrcCall call)
    {
        if (m_EnableLogging == false)
            return;

        RecCallEndLogEvent Rcele = new RecCallEndLogEvent();
        SetCallLogEventParams(call, Rcele);
        Rcele.direction = "outgoing";
        m_I3LogEventClientMgr.SendLogEvent(Rcele);
    }

    private void OnLogSipRequest(SIPRequest sipRequest, IPEndPoint remoteEndPoint, bool Sent, SipTransport sipTransport)
    {
        if (m_EnableLogging == false)
            return;

        if (sipRequest.Method == SIPMethodsEnum.OPTIONS)
            return;

        EnqueueWork(() => SendCallSignalingEvent(sipRequest.ToString(), sipRequest.Header, remoteEndPoint, Sent, sipTransport));
    }

    private void OnLogSipResponse(SIPResponse sipResponse, IPEndPoint remoteEndPoint, bool Sent, SipTransport sipTransport)
    {
        if (m_EnableLogging == false)
            return;

        if (sipResponse.Header.CSeqMethod == SIPMethodsEnum.OPTIONS)
            return;

        EnqueueWork(() => SendCallSignalingEvent(sipResponse.ToString(), sipResponse.Header, remoteEndPoint, Sent, sipTransport));
    }

    private void SendCallSignalingEvent(string sipString, SIPHeader header, IPEndPoint remoteEndpoint, bool Sent, SipTransport sipTransport)
    {
        CallSignalingMessageLogEvent Csm = new CallSignalingMessageLogEvent();
        Csm.elementId = m_ElementID;
        Csm.agencyId = m_AgencyID;
        Csm.agencyAgentId = m_AgentID;

        SrcCall? call = GetCall(header.CallId);
        // Handle callId and incidentId
        string? EmergencyCallIdentifier = SipUtils.GetCallInfoValueForPurpose(header, "emergency-CallId");
        if (string.IsNullOrEmpty(EmergencyCallIdentifier) == true && call != null)
            Csm.callId = call.CallParameters.EmergencyCallIdentifier;
        else
            Csm.callId = EmergencyCallIdentifier;

        string? EmergencyIncidentIdentifier = SipUtils.GetCallInfoValueForPurpose(header, "emergency-IncidentId");
        if (string.IsNullOrEmpty(EmergencyIncidentIdentifier) == true && call != null)
            Csm.incidentId = call.CallParameters.EmergencyIncidentIdentifier;
        else
            Csm.incidentId = EmergencyIncidentIdentifier;

        Csm.text = sipString;
        Csm.direction = Sent == true ? "outgoing" : "incoming";

        m_I3LogEventClientMgr.SendLogEvent(Csm);
    }

    // A SIP request message was received from the SRS
    private void OnSipRequestReceived(SIPRequest sipRequest, SIPEndPoint remoteEndPoint, SipTransport sipTransportManager)
    {
        EnqueueWork(() => HandleSipRequest(sipRequest, remoteEndPoint, sipTransportManager));
    }

    private void HandleSipRequest(SIPRequest sipRequest, SIPEndPoint remoteEndPoint, SipTransport sipTransportManager)
    {
        if (sipRequest.Method == SIPMethodsEnum.BYE)
            HandleByeRequest(sipRequest, remoteEndPoint, sipTransportManager);
        else if (sipRequest.Method == SIPMethodsEnum.ACK)
        {   // Not necessary to do anything for this case
        }
        else
        {
            SIPResponse response = SipUtils.BuildResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed,
                "Method Not Allowed", sipTransportManager.SipChannel, UaName);
            if (sipRequest.Method == SIPMethodsEnum.INVITE)
                sipTransportManager.StartServerInviteTransaction(sipRequest, remoteEndPoint.GetIPEndPoint(),
                    null, response);
            else
                sipTransportManager.StartServerNonInviteTransaction(sipRequest, remoteEndPoint.GetIPEndPoint(),
                    null, response);
        }
    }
    
    // Handles a BYE request from the SRS. A SRS may terminate a recording call if it is being shut down
    // in an orderly manner.
    private void HandleByeRequest(SIPRequest sipRequest, SIPEndPoint remoteEndPoint, SipTransport sipTransportManager)
    {
        SrcCall? srcCall = GetCall(sipRequest.Header.CallId);
        if (srcCall == null)
        {   // The call may have ended already
            SIPResponse notFound = SipUtils.BuildResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist,
                "Dialog Not Found", sipTransportManager.SipChannel, UaName);
            sipTransportManager.StartServerNonInviteTransaction(sipRequest, remoteEndPoint.GetIPEndPoint(), null,
                notFound);
            return;
        }

        SIPResponse okResponse = SipUtils.BuildResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, "OK",
            sipTransportManager.SipChannel, UaName);
        sipTransportManager.StartServerNonInviteTransaction(sipRequest, remoteEndPoint.GetIPEndPoint(),
            null, okResponse);

        srcCall.ShutdownMediaConnections();
        m_Calls.Remove(sipRequest.Header.CallId);
        EnqueueWork(() => SendRecCallEndLogEvent(srcCall));
    }

    // No action required because all responses from an SRS are handled in transactions
    private void OnSipResponseReceived(SIPResponse sipResponse, SIPEndPoint remoteEndPoint, SipTransport sipTransportManager)
    {
    }

}
