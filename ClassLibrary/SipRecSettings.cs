/////////////////////////////////////////////////////////////////////////////////////
//  File:   SipRecSettings.cs                                       9 Feb 24 PHR
/////////////////////////////////////////////////////////////////////////////////////

namespace SipRecClient;

/// <summary>
/// SIPREC configuration settings
/// </summary>
public class SipRecSettings
{
    /// <summary>
    /// Enables recording on all SIPREC recorders
    /// </summary>
    public bool EnableSipRec { get; set; } = false;

    /// <summary>
    /// Settings for one or more SIPREC media recorders
    /// </summary>
    public List<SipRecRecorderSettings> SipRecRecorders { get; set; } = new List<SipRecRecorderSettings>();

    /// <summary>
    /// Constructor
    /// </summary>
    public SipRecSettings()
    {
    }
}
