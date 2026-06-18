using Microsoft.Win32.SafeHandles;
using UdsOnCan.Can;
using vxlapi_NET;

namespace UdsOnCan.Vector;

/// <summary>
/// <see cref="ICanBus"/> over Vector's <c>vxlapi_NET</c> managed wrapper — no
/// P/Invoke. The call sequence (OpenDriver → GetDriverConfig → OpenPort →
/// SetNotification → SetChannelBitrate → ActivateChannel → XL_Receive / XL_CanTransmit)
/// mirrors a proven VectorAdapter; verify the exact wrapper signatures against your
/// installed vxlapi_NET version.
///
/// Requires <c>vxlapi_NET.dll</c> (Vector XL Driver Library, licensed/proprietary)
/// in <c>libs/</c> — see this project's .csproj. This project is intentionally NOT
/// part of the default solution so the core library + demo build without the DLL.
/// </summary>
public sealed class VectorCanBus : ICanBus
{
    private readonly XLDriver _drv = new();
    private readonly string _appName;
    private readonly ulong _accessMask;

    private int _portHandle;
    private int _notifyHandle;
    private XLClass.xl_driver_config _config = new();

    private Thread? _rxThread;
    private volatile bool _running;

    public event Action<CanFrame>? FrameReceived;

    /// <param name="accessMask">Channel access mask (from <see cref="ListChannels"/> / driver config).</param>
    /// <param name="appName">Application name registered with the XL driver.</param>
    public VectorCanBus(ulong accessMask, string appName = "UdsOnCan")
    {
        _accessMask = accessMask;
        _appName = appName;
    }

    /// <summary>Enumerate available CAN channels so a GUI can let the user pick one.</summary>
    public static IReadOnlyList<(string Name, ulong Mask, string Transceiver)> ListChannels()
    {
        var drv = new XLDriver();
        if (drv.XL_OpenDriver() != XLDefine.XL_Status.XL_SUCCESS)
            return Array.Empty<(string, ulong, string)>();
        try
        {
            var cfg = new XLClass.xl_driver_config();
            drv.XL_GetDriverConfig(ref cfg);
            var list = new List<(string, ulong, string)>();
            for (uint i = 0; i < cfg.channelCount; i++)
                list.Add((cfg.channel[i].name, cfg.channel[i].channelMask, cfg.channel[i].transceiverName));
            return list;
        }
        finally { drv.XL_CloseDriver(); }
    }

    public void Open(int bitrate)
    {
        Check(_drv.XL_OpenDriver(), "XL_OpenDriver");
        Check(_drv.XL_GetDriverConfig(ref _config), "XL_GetDriverConfig");

        ulong permission = _accessMask;
        Check(_drv.XL_OpenPort(ref _portHandle, _appName, _accessMask, ref permission, 256,
                XLDefine.XL_InterfaceVersion.XL_INTERFACE_VERSION, XLDefine.XL_BusTypes.XL_BUS_TYPE_CAN),
            "XL_OpenPort");

        _drv.XL_CanSetChannelBitrate(_portHandle, _accessMask, (uint)bitrate);
        Check(_drv.XL_SetNotification(_portHandle, ref _notifyHandle, 1), "XL_SetNotification");
        Check(_drv.XL_ActivateChannel(_portHandle, _accessMask, XLDefine.XL_BusTypes.XL_BUS_TYPE_CAN,
                XLDefine.XL_AC_Flags.XL_ACTIVATE_RESET_CLOCK),
            "XL_ActivateChannel");

        _running = true;
        _rxThread = new Thread(RxLoop) { IsBackground = true, Name = "vxlapi-rx" };
        _rxThread.Start();
    }

    private void RxLoop()
    {
        // Wait on the XL notification handle, then drain the RX queue. (Polling with
        // a short timeout also works if the handle wrapping misbehaves on your SDK.)
        using var wait = new AutoResetEvent(false)
        {
            SafeWaitHandle = new SafeWaitHandle((IntPtr)_notifyHandle, ownsHandle: false),
        };

        while (_running)
        {
            wait.WaitOne(50);
            XLClass.xl_event ev = null!;
            while (_drv.XL_Receive(_portHandle, ref ev) != XLDefine.XL_Status.XL_ERR_QUEUE_IS_EMPTY)
            {
                if (ev.tag != XLDefine.XL_EventTags.XL_RECEIVE_MSG) continue; // skip TX receipts / chip-state
                var msg = ev.tagData.can_Msg;

                bool extended = (msg.id & 0x80000000u) != 0;          // XL_CAN_EXT_MSG_ID
                uint id = msg.id & 0x1FFFFFFFu;
                int len = Math.Min((int)msg.dlc, msg.data?.Length ?? 0);
                var data = new byte[len];
                if (len > 0) Array.Copy(msg.data!, data, len);

                FrameReceived?.Invoke(new CanFrame(id, extended, data));
            }
        }
    }

    public void Send(in CanFrame frame)
    {
        uint id = frame.Id | (frame.IsExtended ? 0x80000000u : 0u);
        var ev = new XLClass.xl_event
        {
            tag = XLDefine.XL_EventTags.XL_TRANSMIT_MSG,
            tagData = new XLClass.xl_tag_data
            {
                can_Msg = new XLClass.xl_can_msg
                {
                    id = id,
                    dlc = (ushort)frame.Data.Length,
                    flags = XLDefine.XL_MessageFlags.XL_CAN_MSG_FLAG_NONE,
                    data = Pad8(frame.Data),
                },
            },
        };
        Check(_drv.XL_CanTransmit(_portHandle, _accessMask, ev), "XL_CanTransmit");
    }

    public void Close()
    {
        _running = false;
        _rxThread?.Join(300);
        if (_portHandle != 0)
        {
            _drv.XL_DeactivateChannel(_portHandle, _accessMask);
            _drv.XL_ClosePort(_portHandle);
            _portHandle = 0;
        }
        _drv.XL_CloseDriver();
    }

    public void Dispose() => Close();

    private static byte[] Pad8(byte[] d)
    {
        if (d.Length == 8) return d;
        var p = new byte[8];
        Array.Copy(d, p, Math.Min(8, d.Length));
        return p;
    }

    private static void Check(XLDefine.XL_Status status, string op)
    {
        if (status != XLDefine.XL_Status.XL_SUCCESS)
            throw new InvalidOperationException($"{op} failed: {status}");
    }
}
