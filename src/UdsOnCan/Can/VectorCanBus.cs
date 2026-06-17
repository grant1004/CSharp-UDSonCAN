using System.Runtime.InteropServices;

namespace UdsOnCan.Can;

/// <summary>
/// Vector XL Driver Library (vxlapi) binding — SKELETON.
///
/// This compiles, but the channel-config / transmit / receive bodies are left for
/// you (the OEM) to complete against YOUR installed vxlapi version, because the
/// XLevent / XLcanRxEvent struct layout and the exact channel-mask setup differ
/// across vxlapi releases and CAN vs CAN-FD. Requirements at runtime:
///   • Vector XL Driver Library installed (vxlapi64.dll on PATH)
///   • a valid Vector licence / hardware (or the virtual CAN channel for testing)
///
/// Recommended call sequence (see the vxlapi manual):
///   xlOpenDriver()
///   xlGetApplConfig(...) / xlSetApplConfig(...)   // map "app" → channel
///   xlGetChannelMask(...) → accessMask
///   xlOpenPort(..., accessMask, &portHandle, ...)
///   xlCanSetChannelBitrate(portHandle, accessMask, bitrate)
///   xlActivateChannel(portHandle, accessMask, ...)
///   // TX: xlCanTransmitEx(portHandle, accessMask, ...)
///   // RX: background thread → xlCanReceive(portHandle, &event) (or WaitForSingleObject on the notify handle)
///   xlDeactivateChannel / xlClosePort / xlCloseDriver
///
/// For hardware-free development use <see cref="LoopbackCanBus"/> instead.
/// </summary>
public sealed class VectorCanBus : ICanBus
{
    private const string Dll = "vxlapi64.dll";

    // Minimal, stable entry points. Add the rest from the manual as you implement.
    [DllImport(Dll)] private static extern int xlOpenDriver();
    [DllImport(Dll)] private static extern int xlCloseDriver();

    private readonly uint _appChannel;
    private bool _driverOpen;

#pragma warning disable CS0067 // raised once you implement the RX thread (see class docs)
    public event Action<CanFrame>? FrameReceived;
#pragma warning restore CS0067

    /// <param name="appChannel">The application channel index configured in Vector Hardware Config.</param>
    public VectorCanBus(uint appChannel = 0) => _appChannel = appChannel;

    public void Open(int bitrate)
    {
        const int XL_SUCCESS = 0;
        if (xlOpenDriver() != XL_SUCCESS)
            throw new InvalidOperationException(
                "xlOpenDriver failed — is the Vector XL Driver Library installed and a channel configured?");
        _driverOpen = true;

        // TODO(OEM): open port, set bitrate, activate channel, and start the RX
        // thread that converts XLcanRxEvent → CanFrame and raises FrameReceived.
        throw new NotImplementedException(
            "VectorCanBus.Open: complete the vxlapi port/channel setup for your SDK version (see class docs). " +
            "Use LoopbackCanBus for hardware-free testing.");
    }

    public void Close()
    {
        if (_driverOpen) { xlCloseDriver(); _driverOpen = false; }
    }

    public void Send(in CanFrame frame)
        => throw new NotImplementedException("VectorCanBus.Send: implement xlCanTransmitEx for your SDK version.");

    public void Dispose() => Close();
}
