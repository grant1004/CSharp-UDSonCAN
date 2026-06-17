namespace UdsOnCan.Uds;

/// <summary>
/// Tester-side UDS timing. These are how long the CLIENT waits, deliberately a bit
/// generous vs. the spec server values.
/// </summary>
public sealed class UdsTiming
{
    /// <summary>P2_client: max wait for the first response after a request.</summary>
    public TimeSpan P2 { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>P2*_client: extended wait after each NRC 0x78 (responsePending).</summary>
    public TimeSpan P2Star { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>S3_client: tester-present is sent every S3/2 to keep the session alive.</summary>
    public TimeSpan S3 { get; init; } = TimeSpan.FromSeconds(2);
}
