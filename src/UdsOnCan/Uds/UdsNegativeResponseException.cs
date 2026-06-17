namespace UdsOnCan.Uds;

/// <summary>Thrown when the ECU returns a negative response (0x7F) other than responsePending (0x78).</summary>
public sealed class UdsNegativeResponseException : Exception
{
    public byte RequestSid { get; }
    public byte ResponseCode { get; }

    public UdsNegativeResponseException(byte requestSid, byte nrc)
        : base($"UDS service 0x{requestSid:X2} rejected: NRC 0x{nrc:X2} ({Nrc.Name(nrc)})")
    {
        RequestSid = requestSid;
        ResponseCode = nrc;
    }
}
