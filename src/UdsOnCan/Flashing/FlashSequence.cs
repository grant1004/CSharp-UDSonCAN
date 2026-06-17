using UdsOnCan.Uds;

namespace UdsOnCan.Flashing;

/// <summary>
/// OEM-tunable knobs for the flash flow. Routine IDs, the addr/length format and
/// the seed→key transform are ECU-specific — override them. Defaults are
/// placeholders so the pipeline is runnable against the simulated ECU.
/// </summary>
public sealed class FlashOptions
{
    /// <summary>The real seed→key algorithm. MUST be supplied for a real ECU.</summary>
    public SeedKeyAlgorithm SeedKey { get; init; } = (_, seed) => seed;

    /// <summary>requestSeed sub-function (the sendKey level is this + 1).</summary>
    public byte SecuritySeedLevel { get; init; } = 0x11;

    /// <summary>Add CommunicationControl (0x28) + ControlDTCSetting (0x85) for in-vehicle flashing.</summary>
    public bool OnVehicleBus { get; init; }

    public ushort EraseRoutineId { get; init; } = 0xFF00;
    public ushort CheckRoutineId { get; init; } = 0xFF01;

    /// <summary>dataFormatIdentifier for RequestDownload (0x00 = no compression / no encryption).</summary>
    public byte DataFormatIdentifier { get; init; } = 0x00;

    /// <summary>addressAndLengthFormatIdentifier: high nibble = #size bytes, low nibble = #address bytes (0x44 = 4+4).</summary>
    public byte AddressAndLengthFormatIdentifier { get; init; } = 0x44;

    /// <summary>Programming session sub-function (usually 0x02).</summary>
    public byte ProgrammingSession { get; init; } = 0x02;
}

/// <summary>What each flash step receives.</summary>
public sealed class FlashContext
{
    public UdsClient Uds { get; }
    public HexImage Image { get; }
    public FlashOptions Options { get; }

    public FlashContext(UdsClient uds, HexImage image, FlashOptions options)
    {
        Uds = uds;
        Image = image;
        Options = options;
    }
}

public delegate Task FlashStepAction(FlashContext ctx, IProgress<FlashProgress> progress, CancellationToken ct);

/// <summary>
/// An ordered list of named steps. Use <see cref="Default"/> for the standard UDS
/// programming flow, or compose your own with <see cref="Step"/>.
/// </summary>
public sealed class FlashSequence
{
    private readonly List<(string Name, FlashStepAction Action)> _steps = new();

    public IReadOnlyList<(string Name, FlashStepAction Action)> Steps => _steps;

    public FlashSequence Step(string name, FlashStepAction action)
    {
        _steps.Add((name, action));
        return this;
    }

    /// <summary>
    /// Standard programming flow:
    /// extended→programming session → security access → (in-vehicle: stop comms + DTC)
    /// → erase → per-segment RequestDownload/TransferData/TransferExit → CRC check → ECU reset.
    /// </summary>
    public static FlashSequence Default(FlashOptions o)
    {
        var seq = new FlashSequence();

        seq.Step("Enter extended session", async (c, p, ct) =>
            await c.Uds.DiagnosticSessionControlAsync(0x03, ct));

        seq.Step("Enter programming session", async (c, p, ct) =>
            await c.Uds.DiagnosticSessionControlAsync(o.ProgrammingSession, ct));

        if (o.OnVehicleBus)
        {
            seq.Step("Disable normal communication", async (c, p, ct) =>
                await c.Uds.CommunicationControlAsync(0x03 /*disableRxAndTx*/, 0x01 /*normalCommMsgs*/, ct));
            seq.Step("Disable DTC setting", async (c, p, ct) =>
                await c.Uds.ControlDtcSettingAsync(0x02 /*off*/, ct));
        }

        seq.Step("Security access", async (c, p, ct) =>
        {
            var seedResp = await c.Uds.SecurityAccessRequestSeedAsync(o.SecuritySeedLevel, ct);
            var seed = seedResp[2..]; // [0x67, level, seed...]
            if (seed.All(b => b == 0)) return; // already unlocked
            var key = o.SeedKey(o.SecuritySeedLevel, seed);
            await c.Uds.SecurityAccessSendKeyAsync((byte)(o.SecuritySeedLevel + 1), key, ct);
        });

        seq.Step("Erase memory", async (c, p, ct) =>
            await c.Uds.RoutineControlAsync(0x01 /*startRoutine*/, o.EraseRoutineId, null, ct));

        seq.Step("Transfer data", async (c, p, ct) =>
        {
            long total = c.Image.TotalBytes;
            long done = 0;
            foreach (var seg in c.Image.Segments)
            {
                int maxBlock = await c.Uds.RequestDownloadAsync(
                    o.DataFormatIdentifier, o.AddressAndLengthFormatIdentifier,
                    seg.Address, seg.Data.Length, ct);

                int chunk = maxBlock > 2 ? maxBlock - 2 : 256; // minus 0x36 SID + sequence byte
                byte seqCounter = 1;
                int offset = 0;
                while (offset < seg.Data.Length)
                {
                    ct.ThrowIfCancellationRequested();
                    int n = Math.Min(chunk, seg.Data.Length - offset);
                    await c.Uds.TransferDataAsync(seqCounter, seg.Data.AsSpan(offset, n).ToArray(), ct);
                    offset += n;
                    done += n;
                    seqCounter = (byte)(seqCounter == 0xFF ? 0x00 : seqCounter + 1);
                    p.Report(new FlashProgress("Transfer data",
                        total == 0 ? 100 : done * 100.0 / total,
                        $"0x{seg.Address:X}: {offset}/{seg.Data.Length} B"));
                }
                await c.Uds.RequestTransferExitAsync(ct);
            }
        });

        seq.Step("Verify (CRC)", async (c, p, ct) =>
            await c.Uds.RoutineControlAsync(0x01, o.CheckRoutineId, null, ct));

        seq.Step("ECU reset", async (c, p, ct) =>
            await c.Uds.EcuResetAsync(0x01 /*hardReset*/, ct));

        return seq;
    }
}
