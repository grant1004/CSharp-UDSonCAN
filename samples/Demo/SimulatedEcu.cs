using UdsOnCan.Can;
using UdsOnCan.IsoTp;

namespace Demo;

/// <summary>
/// A throwaway in-process ECU so the demo runs end-to-end with no hardware. It
/// speaks just enough UDS to accept a programming flow, and deliberately answers
/// the erase routine with two responsePending (NRC 0x78) frames before the positive
/// response — exercising the client's P2* wait loop.
///
/// Seed→key here is a TRIVIAL placeholder (key = seed XOR 0xA5); a real ECU's
/// algorithm is proprietary. The demo's <c>FlashOptions.SeedKey</c> matches it.
/// </summary>
public sealed class SimulatedEcu
{
    private readonly IsoTpChannel _tp;
    private readonly byte[] _seed = { 0x11, 0x22, 0x33, 0x44 };

    public SimulatedEcu(ICanBus bus, IsoTpConfig cfg) => _tp = new IsoTpChannel(bus, cfg);

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            byte[] req;
            try { req = await _tp.ReceiveAsync(TimeSpan.FromSeconds(30), ct); }
            catch (TimeoutException) { continue; }
            catch (OperationCanceledException) { break; }
            catch { continue; }

            try { await HandleAsync(req, ct); }
            catch (OperationCanceledException) { break; }
            catch { /* ignore malformed in the sim */ }
        }
    }

    private async Task HandleAsync(byte[] req, CancellationToken ct)
    {
        byte sid = req[0];
        switch (sid)
        {
            case 0x3E: // TesterPresent
                if (req.Length > 1 && (req[1] & 0x80) != 0) return; // suppressPosRsp → silent
                await Pos(new byte[] { 0x7E, (byte)(req.Length > 1 ? req[1] & 0x7F : 0) }, ct);
                break;

            case 0x10: // DiagnosticSessionControl
                await Pos(new byte[] { 0x50, req[1], 0x00, 0x32, 0x01, 0xF4 }, ct);
                break;

            case 0x27: // SecurityAccess
                if ((req[1] & 1) == 1) // requestSeed (odd)
                {
                    var r = new byte[2 + _seed.Length];
                    r[0] = 0x67; r[1] = req[1];
                    Array.Copy(_seed, 0, r, 2, _seed.Length);
                    await Pos(r, ct);
                }
                else // sendKey (even)
                {
                    var key = req[2..];
                    var expect = _seed.Select(b => (byte)(b ^ 0xA5)).ToArray();
                    if (key.SequenceEqual(expect)) await Pos(new byte[] { 0x67, req[1] }, ct);
                    else await Neg(0x27, 0x35, ct); // invalidKey
                }
                break;

            case 0x28: await Pos(new byte[] { 0x68, req[1] }, ct); break; // CommunicationControl
            case 0x85: await Pos(new byte[] { 0xC5, req[1] }, ct); break; // ControlDTCSetting

            case 0x31: // RoutineControl
            {
                ushort rid = (ushort)((req[2] << 8) | req[3]);
                if (rid == 0xFF00) // erase → simulate a long operation with responsePending
                {
                    await Neg(0x31, 0x78, ct);
                    await Task.Delay(150, ct);
                    await Neg(0x31, 0x78, ct);
                    await Task.Delay(150, ct);
                }
                await Pos(new byte[] { 0x71, req[1], req[2], req[3] }, ct);
                break;
            }

            case 0x34: await Pos(new byte[] { 0x74, 0x20, 0x01, 0x02 }, ct); break; // RequestDownload → maxBlock 0x0102
            case 0x36: await Pos(new byte[] { 0x76, req[1] }, ct); break;            // TransferData → echo block seq
            case 0x37: await Pos(new byte[] { 0x77 }, ct); break;                    // RequestTransferExit
            case 0x11: await Pos(new byte[] { 0x51, req[1] }, ct); break;            // ECUReset

            default: await Neg(sid, 0x11, ct); break; // serviceNotSupported
        }
    }

    private Task Pos(byte[] data, CancellationToken ct) => _tp.SendAsync(data, ct);
    private Task Neg(byte sid, byte nrc, CancellationToken ct) => _tp.SendAsync(new byte[] { 0x7F, sid, nrc }, ct);
}
