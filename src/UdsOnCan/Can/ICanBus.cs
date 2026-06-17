namespace UdsOnCan.Can;

/// <summary>
/// Hardware abstraction for a single CAN channel. Swap the implementation
/// (Vector / PCAN / SocketCAN / loopback) without touching the ISO-TP or UDS
/// layers above. Transmit is synchronous (the underlying driver call is fast and
/// the ISO-TP layer paces sending via flow control); receive is push-based via
/// <see cref="FrameReceived"/>, raised from the driver's RX thread.
/// </summary>
public interface ICanBus : IDisposable
{
    /// <summary>Open the channel at the given bit rate (bit/s, e.g. 500000).</summary>
    void Open(int bitrate);

    void Close();

    /// <summary>Transmit one frame. Must be safe to call from any thread.</summary>
    void Send(in CanFrame frame);

    /// <summary>Raised for every received frame (unfiltered — the ISO-TP layer filters by ID).</summary>
    event Action<CanFrame>? FrameReceived;
}
