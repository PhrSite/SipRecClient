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
    /// <value>This SIPURI identifies the originator of the call. It is used to associate the incoming media streams
    /// with the caller in the SIPREC metadata that is sent to the SRS.</value>
    public SIPURI FromUri { get; set; } = new SIPURI(SIPSchemesEnum.sip, IPAddress.Loopback, 5060);

    /// <summary>
    /// To header value from the call to be recorded.
    /// </summary>
    /// <value>This SIPURI identifies the called party. It is used to associate the outgoing media streams with the
    /// called party in the SIPREC metadata that is sent to the SRS.</value>
    public SIPURI ToUri {  get; set; } = new SIPURI(SIPSchemesEnum.sip, IPAddress.Loopback, 5060);

    /// <summary>
    /// SDP that was used to answer the call that is to be recorded.
    /// </summary>
    /// <value>The SDP identifies the media types of each RtpChannel and the media encoding (codecs). This information is used
    /// to build the SDP that is offered to the SRS in the SIP INVITE request to it.</value>
    public Sdp AnsweredSdp = new Sdp(IPAddress.Loopback, "");

    /// <summary>
    /// List of RtpChannel objects that are handling the audio, RTT and video media for the original call. The list may be
    /// empty if the call does not contain any audio, video or RTT media.
    /// </summary>
    /// <value>Each RtpChannel handles the received and sent media streams. Each SrcUserAgent hooks the events of each RtpChannel
    /// so that it can duplicate the media to send to the SRS.</value>
    public List<RtpChannel> CallRtpChannels = new List<RtpChannel>();

    /// <summary>
    /// MsrpConnection object that is handling the MSRP media for the original call.
    /// This may be null if the call has no MSRP media.
    /// </summary>
    /// <value>The MsrpConnection class handles the received and sent MSRP media. Each SrcUserAgent hooks the events of
    /// the MsrpConnection so that it can duplicate the media to send to the SRS.</value>
    public MsrpConnection? CallMsrpConnection = null;

    /// <summary>
    /// Call-ID header value for the original call.
    /// </summary>
    /// <value>Required to uniquely identify the call that is being recorded.</value>
    public string CallId = string.Empty;

    /// <summary>
    /// Call-Info header value of the Call-Info header that has a purpose parameter of "emergency-CallId" from the call that is
    /// being recorded. This field is required for NG9-1-1 calls that are being recorded.
    /// </summary>
    /// <value>See Section 2.1.6 of NENA-STA-010.3b.</value>
    public string EmergencyCallIdentifier = string.Empty;

    /// <summary>
    /// Call-Info header value of the Call-Info header that has a purpose parameter of "emergency-IncidentId" from the call that
    /// is being recorded. This field is required for NG9-1-1 calls that are being recorded.
    /// </summary>
    /// <value>See Section 2.1.7 of NENA-STA-010.3b.</value>
    public string EmergencyIncidentIdentifier = string.Empty;
}
