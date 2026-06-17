using UdsOnCan.IsoTp;

namespace UdsOnCan.Uds;

/// <summary>
/// UDS (ISO 14229-1) client over one ISO-TP channel. Handles the NRC 0x78
/// (responsePending) wait loop centrally, serialises requests (so a background
/// Tester Present can't interleave with a real exchange), and exposes the services
/// a flashing workflow needs.
/// </summary>
public sealed class UdsClient : IDisposable
{
    private readonly IsoTpChannel _tp;
    private readonly UdsTiming _t;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Timer? _testerPresent;

    public UdsClient(IsoTpChannel tp, UdsTiming? timing = null)
    {
        _tp = tp;
        _t = timing ?? new UdsTiming();
    }

    /// <summary>
    /// Send a request and return the positive response bytes (including the
    /// response SID). NRC 0x78 is absorbed here: we keep waiting on P2* until a real
    /// answer arrives. Any other negative response throws <see cref="UdsNegativeResponseException"/>.
    /// </summary>
    public async Task<byte[]> RequestAsync(byte sid, byte[]? data, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var req = new byte[1 + (data?.Length ?? 0)];
            req[0] = sid;
            if (data is not null) Array.Copy(data, 0, req, 1, data.Length);
            await _tp.SendAsync(req, ct);

            var wait = _t.P2;
            while (true)
            {
                var resp = await _tp.ReceiveAsync(wait, ct);
                if (resp.Length == 0) continue;

                if (resp[0] == 0x7F)
                {
                    byte reqSid = resp.Length > 1 ? resp[1] : sid;
                    byte nrc = resp.Length > 2 ? resp[2] : (byte)0;
                    if (nrc == Nrc.RequestCorrectlyReceivedResponsePending)
                    {
                        wait = _t.P2Star;   // keep waiting — flashing erase/write does this a lot
                        continue;
                    }
                    throw new UdsNegativeResponseException(reqSid, nrc);
                }

                if (resp[0] == (byte)(sid + 0x40))
                    return resp;

                // A stray frame we didn't ask for — keep waiting for our response.
                wait = _t.P2;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Fire-and-forget request with suppressPosRsp (used by Tester Present).</summary>
    private async Task SendNoResponseAsync(byte[] frame, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { await _tp.SendAsync(frame, ct); }
        finally { _gate.Release(); }
    }

    // ---- Services -------------------------------------------------------------
    public Task DiagnosticSessionControlAsync(byte session, CancellationToken ct)         // 0x10
        => RequestAsync(0x10, new[] { session }, ct);

    public Task EcuResetAsync(byte resetType, CancellationToken ct)                        // 0x11
        => RequestAsync(0x11, new[] { resetType }, ct);

    public Task<byte[]> SecurityAccessRequestSeedAsync(byte level, CancellationToken ct)   // 0x27 (odd)
        => RequestAsync(0x27, new[] { level }, ct);

    public Task SecurityAccessSendKeyAsync(byte level, byte[] key, CancellationToken ct)   // 0x27 (even)
    {
        var d = new byte[1 + key.Length];
        d[0] = level;
        Array.Copy(key, 0, d, 1, key.Length);
        return RequestAsync(0x27, d, ct);
    }

    public Task CommunicationControlAsync(byte controlType, byte commType, CancellationToken ct) // 0x28
        => RequestAsync(0x28, new[] { controlType, commType }, ct);

    public Task<byte[]> RoutineControlAsync(byte subFunction, ushort routineId, byte[]? option, CancellationToken ct) // 0x31
    {
        var d = new byte[3 + (option?.Length ?? 0)];
        d[0] = subFunction;
        d[1] = (byte)(routineId >> 8);
        d[2] = (byte)(routineId & 0xFF);
        if (option is not null) Array.Copy(option, 0, d, 3, option.Length);
        return RequestAsync(0x31, d, ct);
    }

    /// <summary>RequestDownload (0x34) → returns maxNumberOfBlockLength (incl. the 0x36 SID + sequence overhead).</summary>
    public async Task<int> RequestDownloadAsync(byte dataFormatId, byte addrLenFormatId, long address, long size, CancellationToken ct) // 0x34
    {
        int addrBytes = addrLenFormatId & 0x0F;
        int sizeBytes = (addrLenFormatId >> 4) & 0x0F;
        var d = new List<byte> { dataFormatId, addrLenFormatId };
        d.AddRange(BigEndian(address, addrBytes));
        d.AddRange(BigEndian(size, sizeBytes));

        var resp = await RequestAsync(0x34, d.ToArray(), ct);
        int lengthFormat = (resp[1] >> 4) & 0x0F;
        int max = 0;
        for (int i = 0; i < lengthFormat; i++) max = (max << 8) | resp[2 + i];
        return max;
    }

    public Task<byte[]> TransferDataAsync(byte blockSequenceCounter, byte[] data, CancellationToken ct) // 0x36
    {
        var d = new byte[1 + data.Length];
        d[0] = blockSequenceCounter;
        Array.Copy(data, 0, d, 1, data.Length);
        return RequestAsync(0x36, d, ct);
    }

    public Task RequestTransferExitAsync(CancellationToken ct)                             // 0x37
        => RequestAsync(0x37, null, ct);

    public Task ControlDtcSettingAsync(byte setting, CancellationToken ct)                 // 0x85
        => RequestAsync(0x85, new[] { setting }, ct);

    // ---- Tester Present keep-alive --------------------------------------------
    public void StartTesterPresent()
    {
        StopTesterPresent();
        var period = _t.S3 / 2;
        _testerPresent = new Timer(_ =>
        {
            // 0x3E 0x80 → suppressPosRspMsgIndicationBit set: the ECU sends no reply,
            // so we just push the frame and don't wait for a response.
            try { SendNoResponseAsync(new byte[] { 0x3E, 0x80 }, CancellationToken.None).GetAwaiter().GetResult(); }
            catch { /* keep-alive is best-effort */ }
        }, null, period, period);
    }

    public void StopTesterPresent()
    {
        _testerPresent?.Dispose();
        _testerPresent = null;
    }

    private static IEnumerable<byte> BigEndian(long value, int byteCount)
    {
        for (int i = byteCount - 1; i >= 0; i--)
            yield return (byte)((value >> (8 * i)) & 0xFF);
    }

    public void Dispose()
    {
        StopTesterPresent();
        _gate.Dispose();
    }
}
