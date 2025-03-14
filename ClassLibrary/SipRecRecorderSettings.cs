/////////////////////////////////////////////////////////////////////////////////////
//  File:   SipRecRecorderSettings.cs                               8 Feb 24 PHR
/////////////////////////////////////////////////////////////////////////////////////

namespace SipRecClient;
using SipLib.Core;
using SipLib.Media;

/// <summary>
/// Configuration settings for a single SIPREC media recorder
/// </summary>
public class SipRecRecorderSettings
{
    /// <summary>
    /// Name of the SIPREC recorder. Required.
    /// </summary>
    /// <value>Each SIPREC recorder must have a unique name.</value>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// If true then this SIPREC recorder is enabled.
    /// </summary>
    /// <value>The default is true.</value>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Specifies the transport protocol for SIP for the recorder
    /// </summary>
    /// <value>UDP, TCP and TLS are supported.</value>
    public SIPProtocolsEnum SipTransportProtocol { get; set; } = SIPProtocolsEnum.tcp;

    /// <summary>
    /// Specifies the SIP interface IP endpoint of the SIPREC Recording Server (SRS).
    /// </summary>
    /// <value>This is a string representation of an IPEndPoint object. For example 192.168.1.76:5060. The address
    /// may be an IPv4 or an IPv6 address. The address family must match the address family of the LocalIpEndpoint
    /// property.</value>
    public string SrsIpEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Specifies the SIP interface local IP endpoint for the SIPREC Recording Client (SRC). This specifies the IPEndPoint
    /// that the SrcUserAgent will bind to. This must be unique within the application domain.
    /// </summary>
    /// <value>This is a string representation of an IPEndPoint object. For example 192.168.1.76:5080. The address
    /// may be an IPv4 or an IPv6 address. The address family must match the address family of the SrsIpEndpoint
    /// property.</value>
    public string LocalIpEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Specifies the encryption to offer the recorder for RTP type media (audio, video, RTT).
    /// </summary>
    /// <value>The default is RtpEncryptionEnum.None</value>
    public RtpEncryptionEnum RtpEncryption { get; set; } = RtpEncryptionEnum.None;

    /// <summary>
    /// Specifies the encryption to offer the recorder for MSRP media
    /// </summary>
    /// <value>The default is MsrpEncryptionEnum.None</value>
    public MsrpEncryptionEnum MsrpEncryption { get; set; } = MsrpEncryptionEnum.None;

    /// <summary>
    /// If true, then the SrcUserAgent will send SIP OPTIONS requests periodically to the SRS. 
    /// </summary>
    /// <value>The default is true.</value>
    public bool EnableOptions { get; set; } = true;

    /// <summary>
    /// Specifies the interval in seconds at which SIP OPTIONS requests are sent to the SRS.
    /// </summary>
    /// <value>The default is 5 seconds</value>
    public int OptionsIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Constructor
    /// </summary>
    public SipRecRecorderSettings()
    {
    }
}
