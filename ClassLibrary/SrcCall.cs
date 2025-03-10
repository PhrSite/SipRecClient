/////////////////////////////////////////////////////////////////////////////////////
//  File:   SrcCall.cs                                              18 Oct 24 PHR
/////////////////////////////////////////////////////////////////////////////////////

namespace SipRecClient;

using SipLib.Core;
using SipLib.Sdp;
using SipRecMetaData;
using SipLib.Rtp;
using SipLib.Msrp;
using SipLib.Transactions;
using SipLib.Media;
using System.Security.Cryptography.X509Certificates;
using I3V3.LoggingHelpers;
using I3V3.LogEvents;
using System.Net;
using SipLib.Logging;

/// <summary>
/// Class for a SIP Recording Client (SRC) call.
/// </summary>
internal class SrcCall
{
    /// <summary>
    /// Contains information about the call being recorded.
    /// </summary>
    public SrcCallParameters CallParameters;

    /// <summary>
    /// Last INVITE request that was sent to the SRS
    /// </summary>
    public SIPRequest Invite;

    /// <summary>
    /// Last used CSeq number. Increment before using in a re-INVITE request.
    /// </summary>
    public int LastCSeq;

    /// <summary>
    /// Last SDP that was sent to the SRS
    /// </summary>
    public Sdp OfferedSdp;

    /// <summary>
    /// OK response to the INVITE request that was received from the SRS.
    /// </summary>
    public SIPResponse? OkResponse = null;

    /// <summary>
    /// SDP received from the SRS in the OK response
    /// </summary>
    public Sdp? AnsweredSdp = null;

    /// <summary>
    /// Contains the last SIPREC metadata object that was sent to the SRS
    /// </summary>
    public recording Recording;

    /// <summary>
    /// Contains a list of RtpChannels for sending RTP media to the SRS
    /// </summary>
    private List<RtpChannel> m_Channels = new List<RtpChannel>();

    // These are the RTP channel objects and the MSRP connection objects of the call to the SRS.
    // The RtpChannel objects are contained in m_Channels.
    private RtpChannel? m_ReceivedAudioChannel = null;
    private RtpChannel? m_SentAudioChannel = null;
    private RtpChannel? m_ReceiveVideoChannel = null;
    private RtpChannel? m_SentVideoChannel = null;
    private RtpChannel? m_ReceiveRttChannel = null;
    private RtpChannel? m_SentRttChannel = null;
    private MsrpConnection? m_ReceivedMsrpConnection = null;
    private MsrpConnection? m_SentMsrpConnection = null;

    private string m_UaName;
    private X509Certificate2 m_Certificate;

    private I3LogEventClientMgr m_I3LogEventClientMgr;
    private bool m_EnableLogging;

    internal bool ReInviteInProgress = false;
    internal List<string> NewMedia = new List<string>();

    /// <summary>
    /// This field is set when the SrcUserAgent starts a client INVITE transaction by sending an INVITE request
    /// to the SRS. It is set to null when the transaction is completed. This is used by the SrcUserAgent to
    /// cancel the INVITE request if the original call ends before the SRS responds to the INVITE request.
    /// </summary>
    public ClientInviteTransaction? ClientInviteTransaction = null;

    private string m_ElementID;
    private string m_AgencyID;
    private string m_AgentID;
    private IPEndPoint m_SrsEndPoint;

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="callParameters">Parameters from the original call that is being recorded</param>
    /// <param name="invite">Original INVITE request sent to the SRS</param>
    /// <param name="offeredSdp">SDP that was offered to the SRS in the INVITE request</param>
    /// <param name="recording">Initial SIPREC metadata sent with the INVITE to the SRS</param>
    /// <param name="UaName">Name of the SrcUserAgent that is managing this call.</param>
    /// <param name="certificate">X.509 certificate to use</param>
    /// <param name="i3LogEventClentMgr">Used for logging NG9-1-1 events.</param>
    /// <param name="enableLogging">If true then I3 event logging is enabled.</param>
    /// <param name="elementID">Element ID of the functional element that is handling the call</param>
    /// <param name="agencyID">Agency ID of the agency that is handling the call.</param>
    /// <param name="agentID">Agent ID of the agent (call taker) that is handling the call.</param>
    /// <param name="srsEndPoint">IPEndPoint of the SRS.</param>
    public SrcCall(SrcCallParameters callParameters, SIPRequest invite, Sdp offeredSdp, recording recording,
        string UaName, X509Certificate2 certificate, I3LogEventClientMgr i3LogEventClentMgr,
        bool enableLogging, string elementID, string agencyID, string agentID, IPEndPoint srsEndPoint)
    {
        CallParameters = callParameters;
        Invite = invite;
        LastCSeq = Invite.Header.CSeq;
        OfferedSdp = offeredSdp;
        Recording = recording;
        m_UaName = UaName;
        m_Certificate = certificate;
        m_I3LogEventClientMgr = i3LogEventClentMgr;
        m_EnableLogging = enableLogging;
        m_ElementID = elementID;
        m_AgencyID = agencyID;
        m_AgentID = agentID;
        m_SrsEndPoint = srsEndPoint;
    }

    /// <summary>
    /// Sets up the RTP and MSRP connections for sending media to the SRS.
    /// <para>
    /// The OfferedSdp and the AnsweredSdp properties must be set before calling this method
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if OfferedSdp or the AnsweredSdp properties
    /// are not set yet.</exception>
    internal void SetupMediaChannels()
    {
        if (OfferedSdp == null)
            throw new InvalidOperationException("The OfferedSdp must be set before calling this method");

        if (AnsweredSdp == null)
            throw new InvalidOperationException("The AnsweredSdp must be set before calling this method");

        // Hook the events of the original call's RTP channels.
        foreach (RtpChannel rtpChannel in CallParameters.CallRtpChannels)
        {
            switch (rtpChannel.MediaType)
            {
                case MediaTypes.Audio:
                    rtpChannel.RtpPacketReceived += OnReceivedAudio;
                    rtpChannel.RtpPacketSent += OnSentAudio;
                    break;
                case MediaTypes.Video:
                    rtpChannel.RtpPacketReceived += OnReceivedVideo;
                    rtpChannel.RtpPacketSent += OnSentVideo;
                    break;
                case MediaTypes.RTT:
                    rtpChannel.RtpPacketReceived += OnReceivedRtt;
                    rtpChannel.RtpPacketSent += OnSentRtt;
                    break;
            }
        }

        // If the original call has MSRP media then hook the events of the MsrpConnection object
        if (CallParameters.CallMsrpConnection != null)
        {
            CallParameters.CallMsrpConnection.MsrpMessageReceived += OnMsrpMessageReceived;
            CallParameters.CallMsrpConnection.MsrpMessageSent += OnMsrpMessageSent;
        }

        bool IsForReceive;
        int iLabel;
        MediaDescription offeredMediaDescription, answeredMediaDescription;
        RtpChannel? callChannel;

        for (int i = 0; i < OfferedSdp.Media.Count; i++)
        {
            offeredMediaDescription = OfferedSdp.Media[i];
            answeredMediaDescription = AnsweredSdp.Media[i];

            iLabel = int.Parse(offeredMediaDescription.Label!);
            // Odd media labels are for media that is received. Even media labels are for media that
            // was sent. See the MediaLabel class.
            IsForReceive = (iLabel % 2) != 0 ? true : false;
            if (offeredMediaDescription.MediaType == MediaTypes.MSRP && CallParameters.CallMsrpConnection != null)
            {
                (MsrpConnection? connection, string? MsrpError) = MsrpConnection.CreateFromSdp(
                    offeredMediaDescription, answeredMediaDescription, false, m_Certificate);
                if (connection != null)
                {
                    if (IsForReceive == true)
                        m_ReceivedMsrpConnection = connection;
                    else
                        m_SentMsrpConnection = connection;

                    connection.Start();
                }
                else
                    SipLogger.LogError($"SRC failed to create an MsrpConnection for label = {iLabel} for " +
                        $"Call-ID = {CallParameters.CallId}, reason = {MsrpError}");
            }
            else
            {   // Media that is handled by RTP
                callChannel = GetCallRtpChannel(CallParameters, offeredMediaDescription.MediaType);
                if (callChannel != null)
                {
                    (RtpChannel? srcRtpChannel, string? Error) = RtpChannel.CreateFromSdp(false, OfferedSdp, offeredMediaDescription,
                        AnsweredSdp, answeredMediaDescription, false, m_UaName);
                    if (srcRtpChannel != null)
                    {
                        if (IsForReceive == true)
                        {
                            switch (offeredMediaDescription.MediaType)
                            {
                                case MediaTypes.Audio:
                                    m_ReceivedAudioChannel = srcRtpChannel;
                                    break;
                                case MediaTypes.Video:
                                    m_ReceiveVideoChannel = srcRtpChannel;
                                    break;
                                case MediaTypes.RTT:
                                    m_ReceiveRttChannel = srcRtpChannel;
                                    break;
                            }
                        }
                        else
                        {
                            switch (offeredMediaDescription.MediaType)
                            {
                                case MediaTypes.Audio:
                                    m_SentAudioChannel = srcRtpChannel;
                                    break;
                                case MediaTypes.Video:
                                    m_SentVideoChannel = srcRtpChannel;
                                    break;
                                case MediaTypes.RTT:
                                    m_SentRttChannel = srcRtpChannel;
                                    break;
                            }
                        }

                        m_Channels.Add(srcRtpChannel);
                        srcRtpChannel.StartListening();
                    }
                    else
                        SipLogger.LogError($"SRC failed to create a RtpChannel for media type = " +
                            $"{offeredMediaDescription.MediaType} for Call-ID = {CallParameters.CallId}, " +
                            $"reason = {Error}");
                }
                else
                    SipLogger.LogError($"Could not find the original RtpChannel for media type = " +
                        $"{offeredMediaDescription.MediaType} and Call-ID = {CallParameters.CallId}");
            }
        } // end for i
    }

    /// <summary>
    /// Sets up a RtpChannel or a MsrpConnection for new media that is being added to the call being recorded.
    /// The SrcUserAgent calls this method the original call has received a re-INVITE request to add new media.
    /// </summary>
    /// <param name="offeredMediaDescription"></param>
    /// <param name="answeredMediaDescription"></param>
    internal void SetupChannelForAddedMedia(MediaDescription offeredMediaDescription, MediaDescription answeredMediaDescription)
    {
        bool IsForReceive;
        int iLabel;

        iLabel = int.Parse(offeredMediaDescription.Label!);
        // Odd media labels are for media that is received. Even media labels are for media that
        // was sent. See the MediaLabel enum.
        IsForReceive = (iLabel % 2) != 0 ? true : false;
        if (offeredMediaDescription.MediaType == MediaTypes.MSRP && CallParameters.CallMsrpConnection != null)
        {
            (MsrpConnection? connection, string? MsrpError) = MsrpConnection.CreateFromSdp(
                offeredMediaDescription, answeredMediaDescription, false, m_Certificate);
            if (connection != null)
            {
                if (IsForReceive == true)
                    m_ReceivedMsrpConnection = connection;
                else
                    m_SentMsrpConnection = connection;

                connection.Start();
            }
            else
                SipLogger.LogError($"Failed to create a MsrpConnection when MSRP is being added to the SIPREC " +
                    $"call. Call-ID = {CallParameters.CallId}, reason = {MsrpError}");
        }
        else
        {   // Media that is handled by RTP
            (RtpChannel? srcRtpChannel, string? Error) = RtpChannel.CreateFromSdp(false, OfferedSdp, offeredMediaDescription,
                AnsweredSdp!, answeredMediaDescription, false, m_UaName);
            if (srcRtpChannel != null)
            {
                if (IsForReceive == true)
                {
                    switch (offeredMediaDescription.MediaType)
                    {
                        case MediaTypes.Audio:
                            m_ReceivedAudioChannel = srcRtpChannel;
                            break;
                        case MediaTypes.Video:
                            m_ReceiveVideoChannel = srcRtpChannel;
                            break;
                        case MediaTypes.RTT:
                            m_ReceiveRttChannel = srcRtpChannel;
                            break;
                    }
                }
                else
                {
                    switch (offeredMediaDescription.MediaType)
                    {
                        case MediaTypes.Audio:
                            m_SentAudioChannel = srcRtpChannel;
                            break;
                        case MediaTypes.Video:
                            m_SentVideoChannel = srcRtpChannel;
                            break;
                        case MediaTypes.RTT:
                            m_SentRttChannel = srcRtpChannel;
                            break;
                    }
                }

                m_Channels.Add(srcRtpChannel);
                srcRtpChannel.StartListening();
            }
            else
                SipLogger.LogError($"Failed to create a RtpChannel when MediaType = {offeredMediaDescription.MediaType} " +
                    $"is added to Call-ID = {CallParameters.CallId}, reason = {Error}");
        }
    }

    /// <summary>
    /// Called by the SrcUserAgent to hook the events of the original call's RtpChannel or MsrpConnection when
    /// new media is added to the original call.
    /// </summary>
    /// <param name="mediaType"></param>
    internal void HookEventsForNewMedia(string mediaType)
    { 
        if (mediaType == MediaTypes.MSRP)
        {
            if (CallParameters.CallMsrpConnection != null)
            {
                CallParameters.CallMsrpConnection.MsrpMessageReceived += OnMsrpMessageReceived;
                CallParameters.CallMsrpConnection.MsrpMessageSent += OnMsrpMessageSent;
            }
        }
        else
        {
            RtpChannel? rtpChannel = GetNewMediaRtpChannel(mediaType);
            if (rtpChannel != null)
            {
                switch (rtpChannel.MediaType)
                {
                    case MediaTypes.Audio:
                        rtpChannel.RtpPacketReceived += OnReceivedAudio;
                        rtpChannel.RtpPacketSent += OnSentAudio;
                        break;
                    case MediaTypes.Video:
                        rtpChannel.RtpPacketReceived += OnReceivedVideo;
                        rtpChannel.RtpPacketSent += OnSentVideo;
                        break;
                    case MediaTypes.RTT:
                        rtpChannel.RtpPacketReceived += OnReceivedRtt;
                        rtpChannel.RtpPacketSent += OnSentRtt;
                        break;
                }
            }
        }
    }

    private RtpChannel? GetNewMediaRtpChannel(string mediaType)
    {
        RtpChannel? rtpChannel = null;
        foreach (RtpChannel channel in CallParameters.CallRtpChannels)
        {
            if (channel.MediaType == mediaType)
            {
                rtpChannel = channel;
                break;
            }
        }
        return rtpChannel;
    }

    /// <summary>
    /// The SrcUserAgent calls this method when it detects that a new RtpChannel object has been created for
    /// the origial call that is being recorded due to a re-INVITE on the original call that changes the
    /// characteristics (such as encryption) of the original call's RtpChannel.
    /// </summary>
    /// <param name="origChannel"></param>
    /// <param name="newChannel"></param>
    internal void ReHookRtpChannelEvents(RtpChannel origChannel, RtpChannel newChannel)
    {
        switch (origChannel.MediaType)
        {
            case MediaTypes.Audio:
                origChannel.RtpPacketReceived -= OnReceivedAudio;
                origChannel.RtpPacketSent -= OnSentAudio;
                newChannel.RtpPacketReceived += OnReceivedVideo;
                newChannel.RtpPacketSent += OnSentAudio;
                break;
            case MediaTypes.Video:
                origChannel.RtpPacketReceived -= OnReceivedVideo;
                origChannel.RtpPacketSent -= OnSentVideo;
                newChannel.RtpPacketReceived += OnReceivedVideo;
                newChannel.RtpPacketSent += OnSentVideo;
                break;
            case MediaTypes.RTT:
                origChannel.RtpPacketReceived -= OnReceivedRtt;
                origChannel.RtpPacketSent -= OnSentRtt;
                newChannel.RtpPacketReceived += OnReceivedRtt;
                newChannel.RtpPacketSent += OnSentRtt;
                break;
        }
    }

    /// <summary>
    /// The SrcUserAgent calls this method when it detects that a new MsrpConnection object has been created for
    /// the origial call that is being recorded due to a re-INVITE on the original call that changes the
    /// characteristics (such as encryption or destination endpoint) of the original call's MsrpConnection.
    /// </summary>
    /// <param name="origConnection"></param>
    /// <param name="newConnection"></param>
    internal void ReHookMsrpChannelEvents(MsrpConnection origConnection, MsrpConnection newConnection)
    {
        origConnection.MsrpMessageReceived -= OnMsrpMessageReceived;
        origConnection.MsrpMessageSent -= OnMsrpMessageSent;
        newConnection.MsrpMessageReceived += OnMsrpMessageReceived;
        newConnection.MsrpMessageSent += OnMsrpMessageSent;
    }

    /// <summary>
    /// The SrcUserAgent calls this method to shutdown all of the media channels to the SRS when the call has ended.
    /// </summary>
    internal void ShutdownMediaConnections()
    {
        // Unhook the events of the original call's RTP channels.
        foreach (RtpChannel rtpChannel in CallParameters.CallRtpChannels)
        {
            switch (rtpChannel.MediaType)
            {
                case MediaTypes.Audio:
                    rtpChannel.RtpPacketReceived -= OnReceivedAudio;
                    rtpChannel.RtpPacketSent -= OnSentAudio;
                    SendRecMediaEndEvent(MediaLabel.ReceivedAudio);
                    SendRecMediaEndEvent(MediaLabel.SentAudio);
                    break;
                case MediaTypes.Video:
                    rtpChannel.RtpPacketReceived -= OnReceivedVideo;
                    rtpChannel.RtpPacketSent -= OnSentVideo;
                    SendRecMediaEndEvent(MediaLabel.ReceivedVideo);
                    SendRecMediaEndEvent(MediaLabel.SentVideo);
                    break;
                case MediaTypes.RTT:
                    rtpChannel.RtpPacketReceived -= OnReceivedRtt;
                    rtpChannel.RtpPacketSent -= OnSentRtt;
                    SendRecMediaEndEvent(MediaLabel.ReceivedRTT);
                    SendRecMediaEndEvent (MediaLabel.SentRTT);
                    break;
            }
        }

        if (CallParameters.CallMsrpConnection != null)
        {
            CallParameters.CallMsrpConnection.MsrpMessageReceived -= OnMsrpMessageReceived;
            CallParameters.CallMsrpConnection.MsrpMessageSent -= OnMsrpMessageSent;
            SendRecMediaEndEvent(MediaLabel.ReceivedMsrp);
            SendRecMediaEndEvent(MediaLabel.SentMsrp);
        }

        foreach (RtpChannel rtpChannel in m_Channels)
        {
            rtpChannel.Shutdown();
        }

        m_Channels.Clear();

        if (m_ReceivedMsrpConnection != null)
        {
            m_ReceivedMsrpConnection.Shutdown();
            m_ReceivedMsrpConnection = null;
        }

        if (m_SentMsrpConnection != null)
        {
            m_SentMsrpConnection.Shutdown();
            m_SentMsrpConnection = null;
        }
    }

    private void SendRecMediaEndEvent(MediaLabel mediaLabel)
    {
        if (m_EnableLogging == false)
            return;

        RecMediaEndLogEvent Rmele = new RecMediaEndLogEvent();
        SetLogEventParams(Rmele);
        Rmele.mediaLabel = new string[1];
        Rmele.mediaLabel[0] = ((int)mediaLabel).ToString();
        m_I3LogEventClientMgr.SendLogEvent(Rmele);
    }

    private void SendRecMediaStartLogEvent(MediaLabel mediaLabel, ref bool FirstPacketSent)
    {
        if (FirstPacketSent == true)
            return;

        FirstPacketSent = true;
        if (m_EnableLogging == false)
            return;

        RecMediaStartLogEvent Rmsle = new RecMediaStartLogEvent();
        SetLogEventParams(Rmsle);
        string strMediaLabel = ((int)mediaLabel).ToString();
        Rmsle.sdp = GetMediaDescriptionForLabel(strMediaLabel)?.ToString();
        Rmsle.mediaLabel = new string[1];
        Rmsle.mediaLabel[0] = ((int)mediaLabel).ToString(); 
        Rmsle.direction = "outgoing";
        m_I3LogEventClientMgr.SendLogEvent(Rmsle);
    }

    private void SetLogEventParams(LogEvent logEvent)
    {
        logEvent.elementId = m_ElementID;
        logEvent.agencyId = m_AgencyID;
        logEvent.agencyAgentId = m_AgentID;
        logEvent.callId = CallParameters.EmergencyCallIdentifier;
        logEvent.incidentId = CallParameters.EmergencyIncidentIdentifier;
        logEvent.callIdSip = CallParameters.CallId;
        logEvent.ipAddressPort = m_SrsEndPoint.ToString();
    }

    private MediaDescription? GetMediaDescriptionForLabel(string mediaLabel)
    {
        if (AnsweredSdp == null)
            return null;

        foreach (MediaDescription Md in AnsweredSdp.Media)
        {
            if (Md.Label == mediaLabel)
                return Md;
        }

        return null;
    }

    private bool m_FirstReceiveAudioPacketSent = false;
    private void OnReceivedAudio(RtpPacket packet)
    {
        if (m_ReceivedAudioChannel != null)
        {
            m_ReceivedAudioChannel.Send(packet);
            if (m_EnableLogging == true)
                SendRecMediaStartLogEvent(MediaLabel.ReceivedAudio, ref m_FirstReceiveAudioPacketSent);
        }
    }

    private bool m_FirstSentAudioPacketSent = false;
    private void OnSentAudio(RtpPacket packet)
    {
        if (m_SentAudioChannel != null)
        {
            m_SentAudioChannel.Send(packet);
            if (m_EnableLogging == true)
                SendRecMediaStartLogEvent(MediaLabel.SentAudio, ref m_FirstSentAudioPacketSent);
        }
    }

    private bool m_FirstReceiveVideoPacketSent = false;
    private void OnReceivedVideo(RtpPacket packet)
    {
        if (m_ReceiveVideoChannel != null)
        {
            m_ReceiveVideoChannel.Send(packet);
            if (m_EnableLogging == true)
                SendRecMediaStartLogEvent(MediaLabel.ReceivedVideo, ref m_FirstReceiveVideoPacketSent);
        }
    }

    private bool m_FirstSentVideoPacketSent = false;
    private void OnSentVideo(RtpPacket packet)
    {
        if (m_SentVideoChannel != null)
        {
            m_SentVideoChannel.Send(packet);
            if (m_EnableLogging == true)
                SendRecMediaStartLogEvent(MediaLabel.SentVideo, ref m_FirstSentVideoPacketSent);
        }
    }

    private bool m_FirstReceiveRttPacketSent = false;
    private void OnReceivedRtt(RtpPacket packet)
    {
        if (m_ReceiveRttChannel != null)
        {
            m_ReceiveRttChannel.Send(packet);
            if (m_EnableLogging == true)
                SendRecMediaStartLogEvent(MediaLabel.ReceivedRTT, ref m_FirstReceiveRttPacketSent);
        }
    }

    private bool m_FirstRttSentPacketSent = false;
    private void OnSentRtt(RtpPacket packet)
    {
        if (m_SentRttChannel != null)
        {
            m_SentRttChannel.Send(packet);
            if (m_EnableLogging == true)
                SendRecMediaStartLogEvent(MediaLabel.SentRTT, ref m_FirstRttSentPacketSent);
        }
    }

    private bool m_FirstReceiveMsrpPacketSent = false;
    private void OnMsrpMessageReceived(string ContentType, byte[] Contents, string from)
    {
        if (m_ReceivedMsrpConnection != null)
        {
            m_ReceivedMsrpConnection.SendMsrpMessage(ContentType, Contents);
            if (m_EnableLogging == true)
                SendRecMediaStartLogEvent(MediaLabel.ReceivedMsrp, ref m_FirstReceiveMsrpPacketSent);
        }
    }

    private bool m_FirstSentMsrpPacketSent = false;
    private void OnMsrpMessageSent(string ContentType, byte[] Contents)
    {
        if (m_SentMsrpConnection != null)
        {
            m_SentMsrpConnection.SendMsrpMessage(ContentType, Contents);
            if (m_EnableLogging == true)
                SendRecMediaStartLogEvent(MediaLabel.SentMsrp, ref m_FirstSentMsrpPacketSent);
        }
    }

    private RtpChannel? GetCallRtpChannel(SrcCallParameters srcCallParameters, string strMediaType)
    {
        foreach (RtpChannel rtpChannel in srcCallParameters.CallRtpChannels)
        {
            if (rtpChannel.MediaType == strMediaType)
                return rtpChannel;
        }

        return null;
    }

}
