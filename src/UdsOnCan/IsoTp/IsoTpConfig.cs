namespace UdsOnCan.IsoTp;

/// <summary>
/// ISO-TP (ISO 15765-2) channel configuration: the request/response CAN IDs and
/// the flow-control / timing parameters this node advertises and enforces.
/// </summary>
public sealed class IsoTpConfig
{
    /// <summary>Arbitration ID this node transmits on (tester → ECU for the tester side).</summary>
    public uint TxId { get; init; }

    /// <summary>Arbitration ID this node listens on (ECU → tester for the tester side).</summary>
    public uint RxId { get; init; }

    /// <summary>true = 29-bit IDs, false = 11-bit.</summary>
    public bool Extended { get; init; }

    public AddressingMode Mode { get; init; } = AddressingMode.Normal;

    /// <summary>Block size we advertise in our Flow Control (0 = send all consecutive frames without further FC).</summary>
    public byte BlockSize { get; init; } = 0;

    /// <summary>Separation time (STmin) we advertise. 0x00–0x7F = milliseconds; 0xF1–0xF9 = 100–900 µs.</summary>
    public byte STmin { get; init; } = 0;

    /// <summary>Byte used to pad frames shorter than 8 (classic CAN). Common values: 0x00, 0xAA, 0xCC.</summary>
    public byte Padding { get; init; } = 0xAA;

    /// <summary>N_Bs: timeout waiting for a Flow Control frame after a First Frame.</summary>
    public TimeSpan N_Bs { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>N_Cr: timeout waiting for each Consecutive Frame.</summary>
    public TimeSpan N_Cr { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Convenience: build the tester-side config given the two IDs.</summary>
    public static IsoTpConfig Tester(uint txId, uint rxId, bool extended = false)
        => new() { TxId = txId, RxId = rxId, Extended = extended };
}
