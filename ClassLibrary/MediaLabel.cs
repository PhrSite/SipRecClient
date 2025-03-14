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
    /// Media label number for audio received from the caller or the called party. Corresponds to "a=label:1".
    /// </summary>
    ReceivedAudio = 1,
    /// <summary>
    /// Media label number for audio sent to the caller or the called party. Corresponds to "a=label:2".
    /// </summary>
    SentAudio,
    /// <summary>
    /// Media label number for video received from the caller or the called party. Corresponds to "a=label:3".
    /// </summary>
    ReceivedVideo,
    /// <summary>
    /// Media label number for video sent to the caller or the called party. Corresponds to "a=label:4".
    /// </summary>
    SentVideo,
    /// <summary>
    /// Media label number for RTT received from the caller or the called party. Corresponds to "a=label:5".
    /// </summary>
    ReceivedRTT,
    /// <summary>
    /// Media label number for RTT sent to the caller or the called party. Corresponds to "a=label:6".
    /// </summary>
    SentRTT,
    /// <summary>
    /// Media label number for MSRP media received from the caller or the called party. Corresponds to "a=label:7".
    /// </summary>
    ReceivedMsrp,
    /// <summary>
    /// Media label number for MSRP media sent to the caller or the called party. Corresponds to "a=label:8",
    /// </summary>
    SentMsrp,
}
