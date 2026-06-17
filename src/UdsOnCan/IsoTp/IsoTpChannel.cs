using System.Threading.Channels;
using UdsOnCan.Can;

namespace UdsOnCan.IsoTp;

/// <summary>
/// ISO 15765-2 (ISO-TP) over classic 8-byte CAN, normal addressing. Segments a UDS
/// payload into Single / First / Consecutive frames and reassembles the reverse,
/// honouring Flow Control (block size + STmin). One channel = one TX/RX ID pair.
/// </summary>
public sealed class IsoTpChannel : IDisposable
{
    private readonly ICanBus _bus;
    private readonly IsoTpConfig _cfg;
    private readonly Channel<CanFrame> _inbox = Channel.CreateUnbounded<CanFrame>();

    public IsoTpChannel(ICanBus bus, IsoTpConfig cfg)
    {
        if (cfg.Mode != AddressingMode.Normal)
            throw new NotSupportedException($"addressing mode {cfg.Mode} not implemented in this scaffold");
        _bus = bus;
        _cfg = cfg;
        _bus.FrameReceived += OnFrame;
    }

    private void OnFrame(CanFrame f)
    {
        if (f.Id == _cfg.RxId) _inbox.Writer.TryWrite(f);
    }

    // ---- Sending --------------------------------------------------------------
    public async Task SendAsync(byte[] payload, CancellationToken ct)
    {
        if (payload.Length <= 7)
        {
            var sf = new byte[1 + payload.Length];
            sf[0] = (byte)payload.Length;            // SF: high nibble 0, low nibble = length
            Array.Copy(payload, 0, sf, 1, payload.Length);
            SendFrame(sf);
            return;
        }

        int len = payload.Length;
        if (len > 0x0FFF)
            throw new NotSupportedException("payloads > 4095 bytes need the ISO-TP escape length form (not implemented)");

        // First Frame: 0x1L LL + 6 data bytes (always a full 8-byte frame).
        var ff = new byte[8];
        ff[0] = (byte)(0x10 | ((len >> 8) & 0x0F));
        ff[1] = (byte)(len & 0xFF);
        Array.Copy(payload, 0, ff, 2, 6);
        _bus.Send(new CanFrame(_cfg.TxId, _cfg.Extended, ff));

        int offset = 6;
        byte sn = 1;
        var (bs, stmin, _) = await WaitFlowControlAsync(ct);
        int inBlock = 0;

        while (offset < len)
        {
            var cf = MakeFrame();
            cf[0] = (byte)(0x20 | (sn & 0x0F));      // Consecutive Frame
            int n = Math.Min(7, len - offset);
            Array.Copy(payload, offset, cf, 1, n);
            _bus.Send(new CanFrame(_cfg.TxId, _cfg.Extended, cf));

            offset += n;
            sn = (byte)((sn + 1) & 0x0F);
            inBlock++;

            if (offset >= len) break;

            if (bs != 0 && inBlock >= bs)
            {
                (bs, stmin, _) = await WaitFlowControlAsync(ct);
                inBlock = 0;
            }
            else
            {
                await DelayStminAsync(stmin, ct);
            }
        }
    }

    private async Task<(byte bs, byte stmin, byte flag)> WaitFlowControlAsync(CancellationToken ct)
    {
        while (true)
        {
            var f = await ReadFrameAsync(_cfg.N_Bs, ct);
            if ((f.Data[0] >> 4) != 0x3) continue;   // not a Flow Control frame — ignore
            byte flag = (byte)(f.Data[0] & 0x0F);
            if (flag == 0x0) return (f.Data[1], f.Data[2], flag);   // ContinueToSend
            if (flag == 0x1) continue;                              // Wait → keep waiting for another FC
            throw new IsoTpException($"flow control overflow/abort (flag 0x{flag:X1})"); // 0x2 overflow
        }
    }

    // ---- Receiving ------------------------------------------------------------
    /// <param name="firstFrameTimeout">How long to wait for the FIRST frame of the response (UDS passes P2 / P2*).</param>
    public async Task<byte[]> ReceiveAsync(TimeSpan firstFrameTimeout, CancellationToken ct)
    {
        var first = await ReadFrameAsync(firstFrameTimeout, ct);
        int type = first.Data[0] >> 4;

        if (type == 0x0)                                            // Single Frame
        {
            int l = first.Data[0] & 0x0F;
            return first.Data.AsSpan(1, l).ToArray();
        }

        if (type != 0x1)                                           // expected SF or FF
            throw new IsoTpException($"unexpected first PCI 0x{first.Data[0]:X2}");

        int len = ((first.Data[0] & 0x0F) << 8) | first.Data[1];   // First Frame
        var buf = new byte[len];
        int got = Math.Min(6, len);
        Array.Copy(first.Data, 2, buf, 0, got);

        SendFlowControl(0x0);                                       // ContinueToSend
        byte expectSn = 1;
        int sinceFc = 0;

        while (got < len)
        {
            var cf = await ReadFrameAsync(_cfg.N_Cr, ct);
            if ((cf.Data[0] >> 4) != 0x2)
                throw new IsoTpException($"expected Consecutive Frame, got PCI 0x{cf.Data[0]:X2}");

            int sn = cf.Data[0] & 0x0F;
            if (sn != expectSn)
                throw new IsoTpException($"sequence error: expected SN {expectSn}, got {sn}");

            int n = Math.Min(7, len - got);
            Array.Copy(cf.Data, 1, buf, got, n);
            got += n;
            expectSn = (byte)((expectSn + 1) & 0x0F);
            sinceFc++;

            if (_cfg.BlockSize != 0 && sinceFc >= _cfg.BlockSize && got < len)
            {
                SendFlowControl(0x0);
                sinceFc = 0;
            }
        }
        return buf;
    }

    private void SendFlowControl(byte flag)
    {
        var fc = MakeFrame();
        fc[0] = (byte)(0x30 | (flag & 0x0F));
        fc[1] = _cfg.BlockSize;
        fc[2] = _cfg.STmin;
        _bus.Send(new CanFrame(_cfg.TxId, _cfg.Extended, fc));
    }

    // ---- Helpers --------------------------------------------------------------
    private byte[] MakeFrame()
    {
        var d = new byte[8];
        if (_cfg.Padding != 0) Array.Fill(d, _cfg.Padding);
        return d;
    }

    private void SendFrame(byte[] head)
    {
        var d = MakeFrame();
        Array.Copy(head, d, head.Length);
        _bus.Send(new CanFrame(_cfg.TxId, _cfg.Extended, d));
    }

    private async Task<CanFrame> ReadFrameAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            return await _inbox.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"ISO-TP: no frame within {timeout.TotalMilliseconds:0} ms");
        }
    }

    private static async Task DelayStminAsync(byte stmin, CancellationToken ct)
    {
        if (stmin == 0) return;
        if (stmin <= 0x7F) { await Task.Delay(stmin, ct); return; }   // milliseconds
        if (stmin >= 0xF1 && stmin <= 0xF9) { await Task.Delay(1, ct); return; } // 100–900 µs → best-effort 1 ms
        // reserved values → treat as a conservative 1 ms
        await Task.Delay(1, ct);
    }

    public void Dispose() => _bus.FrameReceived -= OnFrame;
}

public sealed class IsoTpException : Exception
{
    public IsoTpException(string message) : base(message) { }
}
