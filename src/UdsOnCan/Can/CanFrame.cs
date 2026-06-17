namespace UdsOnCan.Can;

/// <summary>
/// One classic-CAN frame (≤ 8 data bytes). <paramref name="IsExtended"/> selects
/// 29-bit (true) vs 11-bit (false) arbitration ID.
/// </summary>
public readonly record struct CanFrame(uint Id, bool IsExtended, byte[] Data)
{
    public override string ToString()
        => $"{(IsExtended ? "x" : "s")}{Id:X}#{Convert.ToHexString(Data)}";
}
