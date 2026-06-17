namespace UdsOnCan.Flashing;

/// <summary>
/// Progress report surfaced through <see cref="System.IProgress{T}"/> so a GUI can
/// bind a status label (<see cref="Step"/>/<see cref="Detail"/>) and a bar
/// (<see cref="Percent"/>, 0–100) without the flashing core knowing anything about the UI.
/// </summary>
public readonly record struct FlashProgress(string Step, double Percent, string Detail);
