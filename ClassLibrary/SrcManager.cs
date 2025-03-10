/////////////////////////////////////////////////////////////////////////////////////
//  File:   SrcManager.cs                                           20 Oct 24 PHR
/////////////////////////////////////////////////////////////////////////////////////

using I3V3.LoggingHelpers;
using SipLib.Media;
using System.Security.Cryptography.X509Certificates;
using SipLib.Logging;

namespace SipRecClient;

/// <summary>
/// Class for managing SipRec Recording Client interfaces to multiple SIPREC Recorder Servers (SRS)
/// </summary>
public class SrcManager
{
    private SipRecSettings m_SipRecSettings;
    private List<SrcUserAgent> m_SrcUserAgents = new List<SrcUserAgent>();

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="sipRecSettings">Settings for all SIPREC recording clients</param>
    /// <param name="portManager">Used for allocating UDP and TCP ports for media streams.</param>
    /// <param name="certificate">X.509 certificate to use for SIP over TLS (SIPS) and MSRP over
    /// TLS (MSRPS). Required even if TLS is not currently in use.</param>
    /// <param name="agencyID">Identity of the agency that is recording and logging calls</param>
    /// <param name="agentID">Identity of the agent or call taker that is recording and logging calls.</param>
    /// <param name="elementID">NG9-1-1 Element Identifier of the entity recording calls.</param>
    /// <param name="i3EventLoggingManager">Used for logging NG9-1-1 events.</param>
    /// <param name="enableLogging">If true then I3 event logging is enabled</param>
    public SrcManager(SipRecSettings sipRecSettings, MediaPortManager portManager, X509Certificate2 certificate,
        string agencyID, string agentID, string elementID, I3LogEventClientMgr i3EventClientManager, bool enableLogging)
    {
        m_SipRecSettings = sipRecSettings;

        foreach (SipRecRecorderSettings settings in m_SipRecSettings.SipRecRecorders)
        {
            if (settings.Enabled == false)
                continue;   // Don't create a SrcUserAgent object if it is not enabled

            SrcUserAgent Ua;
            try
            {
                Ua = new SrcUserAgent(settings, portManager, certificate, agencyID, agentID, elementID,
                    i3EventClientManager, enableLogging);
                m_SrcUserAgents.Add(Ua);
            }
            catch (Exception ex)
            {
                SipLogger.LogError(ex, $"Failed to create the SrcUserAgent for SIPREC recorder: {settings.Name} " +
                    $"using local endpoint: {settings.LocalIpEndpoint}");
            }
        }
    }

    /// <summary>
    /// Start the interface to each SIPREC recording server
    /// </summary>
    public void Start()
    {
        foreach (SrcUserAgent Ua in m_SrcUserAgents)
        {
            Ua.Start();
        }
    }

    /// <summary>
    /// Shuts down the interface to each SIPREC recording server and releases all connections and resources.
    /// </summary>
    /// <returns></returns>
    public async Task Shutdown()
    {
        foreach (SrcUserAgent Ua in m_SrcUserAgents)
        {
            await Ua.Shutdown();
        }

        m_SrcUserAgents.Clear();
    }

    /// <summary>
    /// Starts a recording session for each recording server for a new call.
    /// </summary>
    /// <param name="srcCallParameters">Parameters for the call</param>
    public void StartRecording(SrcCallParameters srcCallParameters)
    {
        if (m_SipRecSettings.EnableSipRec == false)
            return;

        foreach (SrcUserAgent Ua in m_SrcUserAgents)
        {
            if (Ua.Enabled == true)
                Ua.StartRecording(srcCallParameters);
        }
    }

    /// <summary>
    /// Call this method when a call that is being recorded handles a re-INVITE.
    /// </summary>
    /// <param name="srcCallParameters">Parameters for the call</param>
    public void HandleReInvite(SrcCallParameters srcCallParameters)
    {
        if (m_SipRecSettings.EnableSipRec == false)
            return;

        foreach (SrcUserAgent Ua in m_SrcUserAgents)
        {
            if (Ua.Enabled == true)
                Ua.HandleReInvite(srcCallParameters);
        }
    }

    /// <summary>
    /// Stops the recording session for each recorder for a call. 
    /// </summary>
    /// <param name="strCallId">Call-ID for the call being recorded.</param>
    public void StopRecording(string strCallId)
    {
        foreach (SrcUserAgent Ua in m_SrcUserAgents)
        {
            if (Ua.Enabled == true)
                Ua.StopRecording(strCallId);
        }
    }
}
