namespace UdsOnCan.Uds;

/// <summary>
/// Security-access seed → key transform. This is ECU/OEM-specific and proprietary,
/// so it is a plug-in point: you (the OEM) supply the real algorithm. Receives the
/// security level (the requestSeed sub-function) and the seed bytes from the ECU;
/// returns the key bytes to send back.
/// </summary>
public delegate byte[] SeedKeyAlgorithm(byte level, byte[] seed);
