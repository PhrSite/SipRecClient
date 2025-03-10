/////////////////////////////////////////////////////////////////////////////////////
//  File:   SrcCallParameters.cs                                    14 Oct 24 PHR
/////////////////////////////////////////////////////////////////////////////////////

using System.Net;

namespace SipRecClient;
using SipLib.Core;
using SipLib.Rtp;
using SipLib.Msrp;
using SipLib.Sdp;

/// <summary>
/// Parameters that the SrcUserAgent will need to set up a SIPREC call to a SIP Recording Server (SRS).
/// </summary>
public class SrcCallParameters
{
    /// <summary>
    /// SIP URI of the caller from the From header value of the call to be recorded.
    /// </summary>
    public SIPURI FromUri { get; set; } = new SIPURI(SIPSchemesEnum.sip, IPAddress.Loopback, 5060);

    /// <summary>
    /// To header value from the call to be recorded.
    /// </summary>
    public SIPURI ToUri {  get; set; } = new SIPURI(SIPSchemesEnum.sip, IPAddress.Loopback, 5060);

    /// <summary>
    /// SDP that was used to answer the original call.
    /// </summary>
    public Sdp AnsweredSdp = new Sdp(IPAddress.Loopback, "");
    /// <summary>
    /// List of RtpChannel objects that are handling the audio, RTT and video media for the original call.
    /// </summary>
    public List<RtpChannel> CallRtpChannels = new List<RtpChannel>();
    /// <summary>
    /// MsrpConnection object that is handling the MSRP media for the original call.
    /// This may be null if the call has no MSRP media.
    /// </summary>
    public MsrpConnection? CallMsrpConnection = null;
    /// <summary>
    /// Call-ID header value for the original call.
    /// </summary>
    public string CallId = string.Empty;
    /// <summary>
    /// emergency-CallId Call-Info header purpose value for the original call.
    /// </summary>
    public string EmergencyCallIdentifier = string.Empty;
    /// <summary>
    /// emergency-IncidentId Call-Info header purpose value for the original call.
    /// </summary>
    public string EmergencyIncidentIdentifier = string.Empty;
}
