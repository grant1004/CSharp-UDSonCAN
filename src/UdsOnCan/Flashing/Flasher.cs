using UdsOnCan.Uds;

namespace UdsOnCan.Flashing;

/// <summary>
/// Drives a <see cref="FlashSequence"/> against an ECU. Async, reports
/// <see cref="FlashProgress"/>, honours a <see cref="CancellationToken"/>, and runs
/// Tester Present in the background for the duration so the programming session
/// doesn't time out. This is the single entry point a GUI calls.
/// </summary>
public sealed class Flasher
{
    private readonly UdsClient _uds;

    public Flasher(UdsClient uds) => _uds = uds;

    public async Task FlashAsync(
        HexImage image,
        FlashSequence sequence,
        FlashOptions options,
        IProgress<FlashProgress>? progress = null,
        CancellationToken ct = default)
    {
        var ctx = new FlashContext(_uds, image, options);
        var report = progress ?? new Progress<FlashProgress>();
        int n = sequence.Steps.Count;

        _uds.StartTesterPresent();
        try
        {
            for (int i = 0; i < n; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (name, action) = sequence.Steps[i];
                report.Report(new FlashProgress(name, i * 100.0 / n, "started"));
                await action(ctx, report, ct);
                report.Report(new FlashProgress(name, (i + 1) * 100.0 / n, "done"));
            }
        }
        finally
        {
            _uds.StopTesterPresent();
        }
    }
}
