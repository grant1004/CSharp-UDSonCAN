using Demo;
using UdsOnCan.Can;
using UdsOnCan.Flashing;
using UdsOnCan.IsoTp;
using UdsOnCan.Uds;

// End-to-end flashing demo against an in-process simulated ECU — NO hardware needed.
// Swap LoopbackCanBus for VectorCanBus (once its vxlapi binding is filled in) to
// talk to a real ECU; everything above the CAN layer is identical.

const uint testerTx = 0x7E0; // tester → ECU
const uint testerRx = 0x7E8; // ECU → tester

var medium = new VirtualCanMedium();

// ----- simulated ECU side (its IDs are the tester's, swapped) -----
using var ecuBus = new LoopbackCanBus(medium);
var ecu = new SimulatedEcu(ecuBus, new IsoTpConfig { TxId = testerRx, RxId = testerTx });
using var ecuCts = new CancellationTokenSource();
var ecuTask = ecu.RunAsync(ecuCts.Token);

// ----- tester side -----
using var testerBus = new LoopbackCanBus(medium);
using var tp = new IsoTpChannel(testerBus, IsoTpConfig.Tester(testerTx, testerRx));
using var uds = new UdsClient(tp);

// A fake firmware image: two segments, sized to force multi-frame ISO-TP transfers.
var image = HexImage.FromSegments(new[]
{
    new FlashSegment(0x08000000, MakeBytes(600, i => (byte)i)),
    new FlashSegment(0x08001000, MakeBytes(300, i => (byte)(i * 3))),
});

var options = new FlashOptions
{
    OnVehicleBus = true,                                   // inserts 0x28 + 0x85 steps
    SeedKey = (level, seed) => seed.Select(b => (byte)(b ^ 0xA5)).ToArray(), // matches the sim ECU
};

var flasher = new Flasher(uds);
var progress = new Progress<FlashProgress>(p =>
    Console.WriteLine($"  [{p.Percent,5:0.0}%] {p.Step,-28} {p.Detail}"));

Console.WriteLine($"Flashing {image.TotalBytes} bytes across {image.Segments.Count} segment(s) " +
                  $"to a simulated ECU (tester {testerTx:X}/{testerRx:X})...\n");

int exitCode;
try
{
    await flasher.FlashAsync(image, FlashSequence.Default(options), options, progress);
    Console.WriteLine("\n[OK] Flash completed successfully (simulated).");
    exitCode = 0;
}
catch (Exception ex)
{
    Console.WriteLine($"\n[FAIL] {ex.GetType().Name}: {ex.Message}");
    exitCode = 1;
}
finally
{
    ecuCts.Cancel();
}

return exitCode;

static byte[] MakeBytes(int n, Func<int, byte> f)
{
    var b = new byte[n];
    for (int i = 0; i < n; i++) b[i] = f(i);
    return b;
}
