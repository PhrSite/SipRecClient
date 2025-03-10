/////////////////////////////////////////////////////////////////////////////////////
//  File:   MediaLabel.cs                                          17 Oct 24 PHR
/////////////////////////////////////////////////////////////////////////////////////

namespace SipRecClient;

/// <summary>
/// Enumeration for the label attribute of an SDP media description for SIPREC media channels.
/// </summary>
public enum MediaLabel : int
{
    /// <summary>
    /// Media label number for audio received from the caller or the called party.
    /// </summary>
    ReceivedAudio = 1,
    /// <summary>
    /// Media label number for audio sent to the caller or the called party.
    /// </summary>
    SentAudio,
    /// <summary>
    /// Media label number for video received from the caller or the called party.
    /// </summary>
    ReceivedVideo,
    /// <summary>
    /// Media label number for video sent to the caller or the called party.
    /// </summary>
    SentVideo,
    /// <summary>
    /// Media label number for RTT received from the caller or the called party.
    /// </summary>
    ReceivedRTT,
    /// <summary>
    /// Media label number for RTT sent to the caller or the called party.
    /// </summary>
    SentRTT,
    /// <summary>
    /// Media label number for MSRP media received from the caller or the called party.
    /// </summary>
    ReceivedMsrp,
    /// <summary>
    /// Media label number for MSRP media sent to the caller or the called party.
    /// </summary>
    SentMsrp,
}
