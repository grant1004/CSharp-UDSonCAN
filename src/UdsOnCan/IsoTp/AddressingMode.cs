namespace UdsOnCan.IsoTp;

/// <summary>ISO 15765-2 addressing mode. Only <see cref="Normal"/> is implemented in this scaffold.</summary>
public enum AddressingMode
{
    /// <summary>Normal addressing: full 8 data bytes carry PCI + payload; no address-extension byte.</summary>
    Normal,
    /// <summary>Extended addressing: first data byte is the target address (NOT yet implemented).</summary>
    Extended,
    /// <summary>Normal-fixed addressing (29-bit, ISO 15765-2 §10.3) (NOT yet implemented).</summary>
    NormalFixed,
}
