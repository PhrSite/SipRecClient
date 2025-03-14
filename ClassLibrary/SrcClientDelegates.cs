/////////////////////////////////////////////////////////////////////////////////////
//  File:   SrcClientDelegates.cs                                   13 Mar 25 PHR
/////////////////////////////////////////////////////////////////////////////////////

using SipLib.Core;

namespace SipRecClient;

/// <summary>
/// Delegate type for the SrsStatusChange event of the SrcManager and the SrcUserAgent classes.
/// </summary>
/// <param name="RecorderName">Name of the SIP Recording Server. Corresponds to the Name property of the SipRecRecordingSettings
/// for the recorder.</param>
/// <param name="SrsResponding">If true then the SRS is responding to SIP OPTIONS requests, else it is not responding.</param>
/// <param name="ResponseCode">Response code returned in the response to the OPTIONS request. Will be set to
/// SIPResonseStatusCodesEnum.None if SrsResponding is false.</param>
public delegate void SrsStatusDelegate(string RecorderName, bool SrsResponding, SIPResponseStatusCodesEnum ResponseCode);