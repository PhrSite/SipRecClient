/////////////////////////////////////////////////////////////////////////////////////
//  File:   SipRecUtils.cs                                          17 Oct 24 PHR
/////////////////////////////////////////////////////////////////////////////////////

using SipLib.Media;

namespace SipRecClient;

/// <summary>
/// Utility functions for SIPREC
/// </summary>
internal static class SipRecUtils
{
    /// <summary>
    /// Converts a MediaLabel enum value to a string to use for the SIPREC metadata or the
    /// SDP media description label attribute value
    /// </summary>
    /// <param name="mediaLabel">Input enum value</param>
    /// <returns>The string representation of the numeric value of the enum</returns>
    public static string MediaLabelToString(MediaLabel mediaLabel)
    {
        int LabelInt = (int)mediaLabel;
        return LabelInt.ToString();
    }

    /// <summary>
    /// Gets the media labels for a specified type of media.
    /// </summary>
    /// <param name="mediaType">Media type to get the label value for. For example: audio, videi, etc.</param>
    /// <param name="received">Media label for the media received from the remote party.</param>
    /// <param name="sent">Media label for media sent to the remote party.</param>
    public static void GetMediaLabelForMediaType(string mediaType, out MediaLabel received, out MediaLabel sent)
    {
        switch (mediaType)
        {
            case MediaTypes.Audio:
                received = MediaLabel.ReceivedAudio;
                sent = MediaLabel.SentAudio;
                break;
            case MediaTypes.Video:
                received = MediaLabel.ReceivedVideo;
                sent = MediaLabel.SentVideo;
                break;
            case MediaTypes.RTT:
                received = MediaLabel.ReceivedRTT;
                sent = MediaLabel.SentRTT;
                break;
            case MediaTypes.MSRP:
                received = MediaLabel.ReceivedMsrp;
                sent = MediaLabel.SentMsrp;
                break;
            default:
                received = MediaLabel.ReceivedAudio;
                sent = MediaLabel.SentAudio;
                break;
        }    
    }
}
