namespace UdsOnCan.Uds;

/// <summary>UDS Negative Response Codes (ISO 14229-1) — the common ones.</summary>
public static class Nrc
{
    public const byte GeneralReject = 0x10;
    public const byte ServiceNotSupported = 0x11;
    public const byte SubFunctionNotSupported = 0x12;
    public const byte IncorrectMessageLengthOrInvalidFormat = 0x13;
    public const byte ConditionsNotCorrect = 0x22;
    public const byte RequestSequenceError = 0x24;
    public const byte RequestOutOfRange = 0x31;
    public const byte SecurityAccessDenied = 0x33;
    public const byte InvalidKey = 0x35;
    public const byte ExceedNumberOfAttempts = 0x36;
    public const byte RequiredTimeDelayNotExpired = 0x37;
    public const byte UploadDownloadNotAccepted = 0x70;
    public const byte TransferDataSuspended = 0x71;
    public const byte GeneralProgrammingFailure = 0x72;
    public const byte WrongBlockSequenceCounter = 0x73;

    /// <summary>requestCorrectlyReceived-ResponsePending: keep waiting (P2*), do NOT treat as failure.</summary>
    public const byte RequestCorrectlyReceivedResponsePending = 0x78;

    public const byte ServiceNotSupportedInActiveSession = 0x7F;

    public static string Name(byte nrc) => nrc switch
    {
        GeneralReject => "generalReject",
        ServiceNotSupported => "serviceNotSupported",
        SubFunctionNotSupported => "subFunctionNotSupported",
        IncorrectMessageLengthOrInvalidFormat => "incorrectMessageLengthOrInvalidFormat",
        ConditionsNotCorrect => "conditionsNotCorrect",
        RequestSequenceError => "requestSequenceError",
        RequestOutOfRange => "requestOutOfRange",
        SecurityAccessDenied => "securityAccessDenied",
        InvalidKey => "invalidKey",
        ExceedNumberOfAttempts => "exceedNumberOfAttempts",
        RequiredTimeDelayNotExpired => "requiredTimeDelayNotExpired",
        UploadDownloadNotAccepted => "uploadDownloadNotAccepted",
        TransferDataSuspended => "transferDataSuspended",
        GeneralProgrammingFailure => "generalProgrammingFailure",
        WrongBlockSequenceCounter => "wrongBlockSequenceCounter",
        RequestCorrectlyReceivedResponsePending => "requestCorrectlyReceived-ResponsePending",
        ServiceNotSupportedInActiveSession => "serviceNotSupportedInActiveSession",
        _ => $"unknown(0x{nrc:X2})",
    };
}
