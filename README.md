# C# UDS-on-CAN

A layered, async **C# library for UDS-based ECU flashing/programming over CAN**.
Built for an OEM workflow: you plug in your seed→key algorithm and arrange the
flash sequence; the library handles ISO-TP segmentation, the UDS request/response
machinery (including the NRC `0x78` responsePending wait loop), Tester Present
keep-alive, progress reporting and cancellation.

> Classic CAN (8-byte frames), 11- and 29-bit IDs. CAN FD is intentionally not
> wired yet, but the CAN abstraction doesn't preclude it.

## Architecture

```
        ┌──────────────────────────── your GUI (later) ───────────────────────────┐
        │  pick .hex  →  Flasher.FlashAsync(image, seq, opts, IProgress, Cancel)    │
        └───────────────────────────────────┬──────────────────────────────────────┘
                                             │
   Flashing/   Flasher · FlashSequence · FlashOptions · HexImage (Intel HEX)
                                             │
   Uds/        UdsClient  (0x10/11/27/28/31/34/36/37/3E/85, NRC 0x78 loop, Tester Present)
                                             │
   IsoTp/      IsoTpChannel  (ISO 15765-2: SF/FF/CF/FC, block size + STmin, 11/29-bit)
                                             │
   Can/        ICanBus  ──  VectorCanBus (vxlapi) │ LoopbackCanBus (in-process, for tests)
```

Each layer depends only on the one below. Swap `ICanBus` to change hardware vendor
without touching ISO-TP / UDS / flashing.

## Quick start (no hardware)

```bash
dotnet run --project samples/Demo
```

This flashes a fake image to an **in-process simulated ECU** over a virtual CAN bus
and prints progress — end to end, including a simulated `0x78` responsePending
during erase and multi-frame ISO-TP transfers.

## Using a real ECU

Three things are ECU/OEM-specific and are deliberately left to you:

1. **CAN hardware** — finish `Can/VectorCanBus.cs` (the vxlapi port/transmit/receive
   bodies; the call sequence is documented in the file). Needs the Vector XL Driver
   Library + a licence. Any other vendor: implement `ICanBus` the same way.
2. **Seed → key** — supply the real algorithm via `FlashOptions.SeedKey`
   (`(level, seed) => key`).
3. **Routine IDs / formats** — set `FlashOptions.EraseRoutineId`,
   `CheckRoutineId`, `AddressAndLengthFormatIdentifier`, `DataFormatIdentifier`,
   session/security levels to match your ECU's flash spec.

```csharp
using var bus = new VectorCanBus(appChannel: 0);
bus.Open(500_000);

using var tp  = new IsoTpChannel(bus, IsoTpConfig.Tester(txId: 0x7E0, rxId: 0x7E8));
using var uds = new UdsClient(tp);

var image = HexImage.LoadIntelHex("firmware.hex");
var opts  = new FlashOptions
{
    OnVehicleBus = true,                    // adds 0x28 (comm off) + 0x85 (DTC off)
    SecuritySeedLevel = 0x11,
    EraseRoutineId = 0xFF00,
    CheckRoutineId = 0xFF01,
    SeedKey = (level, seed) => MyOemKey(level, seed),
};

var flasher  = new Flasher(uds);
var progress = new Progress<FlashProgress>(p => Console.WriteLine($"[{p.Percent:0}%] {p.Step} {p.Detail}"));
using var cts = new CancellationTokenSource();

await flasher.FlashAsync(image, FlashSequence.Default(opts), opts, progress, cts.Token);
```

Need a non-standard order (e.g. download a flash driver to RAM first)? Build your
own `FlashSequence` with `.Step("name", async (ctx, progress, ct) => { ... })`
instead of `FlashSequence.Default`.

## Standard flash flow (`FlashSequence.Default`)

extended session (0x10 03) → programming session (0x10 02) → *(in-vehicle: 0x28 + 0x85)*
→ security access (0x27 seed→key) → erase (0x31) → per segment: RequestDownload (0x34)
→ TransferData (0x36, chunked to the ECU's maxBlockLength) → RequestTransferExit (0x37)
→ CRC check (0x31) → ECU reset (0x11). Tester Present (0x3E, suppressPosRsp) runs in
the background throughout.

## What's implemented vs. left to you

| Implemented | Stub / your part |
|---|---|
| ISO-TP normal addressing (SF/FF/CF/FC, BS, STmin, 11/29-bit) | ISO-TP extended / normal-fixed addressing |
| UDS client + 0x78 loop + Tester Present + the flashing services | `VectorCanBus` vxlapi bodies |
| Intel HEX parser, segment merge | seed→key algorithm (proprietary) |
| Default + custom flash sequences, progress, cancellation | erase/check routine IDs & data formats |
| In-process simulated ECU + virtual bus (demo) | — |

## Notes

- **`0x78` is handled centrally** in `UdsClient.RequestAsync`: a responsePending
  switches the wait to P2* and keeps waiting — required for erase/write.
- **Tester Present** uses suppressPosRsp (no reply expected) and is serialised with
  real requests so it can't interleave on the bus.
- Timing (`UdsTiming`) and flow-control (`IsoTpConfig`) are tunable.

## License

MIT — see [LICENSE](LICENSE).
