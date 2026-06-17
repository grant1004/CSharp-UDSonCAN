using System.Threading.Channels;

namespace UdsOnCan.Can;

/// <summary>
/// In-process virtual CAN bus for tests and the demo — no hardware required.
/// Attach two or more <see cref="LoopbackCanBus"/> ports to one
/// <see cref="VirtualCanMedium"/>; a frame sent on one port is delivered to every
/// OTHER port (never echoed to its sender, matching real CAN at this layer).
/// </summary>
public sealed class VirtualCanMedium
{
    private readonly List<LoopbackCanBus> _ports = new();

    internal void Attach(LoopbackCanBus port)
    {
        lock (_ports) _ports.Add(port);
    }

    internal void Detach(LoopbackCanBus port)
    {
        lock (_ports) _ports.Remove(port);
    }

    internal void Broadcast(LoopbackCanBus from, CanFrame frame)
    {
        LoopbackCanBus[] snapshot;
        lock (_ports) snapshot = _ports.ToArray();
        foreach (var p in snapshot)
            if (!ReferenceEquals(p, from))
                p.Deliver(frame);
    }
}

public sealed class LoopbackCanBus : ICanBus
{
    private readonly VirtualCanMedium _medium;
    // Single ordered queue + one pump task: frames are delivered in send order, one
    // at a time, off the sender's call stack (so multi-frame ISO-TP stays intact and
    // there's no reentrancy into the receiver's handler).
    private readonly Channel<CanFrame> _rx =
        Channel.CreateUnbounded<CanFrame>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();

    public LoopbackCanBus(VirtualCanMedium medium)
    {
        _medium = medium;
        _medium.Attach(this);
        _ = PumpAsync();
    }

    public event Action<CanFrame>? FrameReceived;

    public void Open(int bitrate) { }
    public void Close() { }

    public void Send(in CanFrame frame) => _medium.Broadcast(this, frame);

    internal void Deliver(CanFrame frame) => _rx.Writer.TryWrite(frame);

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var f in _rx.Reader.ReadAllAsync(_cts.Token))
                FrameReceived?.Invoke(f);
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _medium.Detach(this);
    }
}
