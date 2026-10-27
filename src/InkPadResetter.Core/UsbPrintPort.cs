using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace InkPadResetter.Core;

/// <summary>
/// Talks to a USB printer through usbprint.sys, the driver Windows already uses for it,
/// so no driver swap (Zadig, libusb) is needed.
/// </summary>
public sealed unsafe class UsbPrintPort : IPrinterTransport
{
    static readonly Guid UsbPrintInterface = new("28d78fad-5a12-11d1-ae5b-0000f803a8c2");
    public const ushort EpsonVendorId = 0x04B8;
    const int ReadBufferSize = 4096;
    const int WriteTimeoutMs = 3000;

    readonly SafeFileHandle _handle;

    public string DevicePath { get; }

    UsbPrintPort(string devicePath, SafeFileHandle handle)
    {
        DevicePath = devicePath;
        _handle = handle;
    }

    /// <summary>Device interface paths of present USB printers from the given vendor.</summary>
    public static IReadOnlyList<string> FindDevicePaths(ushort vendorId = EpsonVendorId)
    {
        var paths = new List<string>();
        var guid = UsbPrintInterface;
        nint set = SetupDiGetClassDevsW(ref guid, null, 0, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var data = new SP_DEVICE_INTERFACE_DATA { cbSize = sizeof(SP_DEVICE_INTERFACE_DATA) };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, 0, ref guid, i, ref data); i++)
            {
                SetupDiGetDeviceInterfaceDetailW(set, ref data, 0, 0, out uint needed, 0);
                nint detail = Marshal.AllocHGlobal((int)needed);
                try
                {
                    // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W: the DWORD plus one WCHAR, padded.
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetailW(set, ref data, detail, needed, out _, 0))
                        paths.Add(Marshal.PtrToStringUni(detail + 4)!);
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        string vid = $"vid_{vendorId:x4}";
        return paths.Where(p => p.Contains(vid, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public static UsbPrintPort Open(string devicePath)
    {
        var handle = CreateFileW(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                 0, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, 0);
        if (handle.IsInvalid)
            throw new PrinterException("Couldn't open the printer's USB port.",
                                       new Win32Exception(Marshal.GetLastWin32Error()));
        return new UsbPrintPort(devicePath, handle);
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        var buffer = data.ToArray();
        if (Transfer(buffer, buffer.Length, write: true, WriteTimeoutMs) != buffer.Length)
            throw new PrinterBusyException();
    }

    public byte[] Read(int timeoutMs)
    {
        var buffer = new byte[ReadBufferSize];
        int n = Transfer(buffer, buffer.Length, write: false, timeoutMs);
        return buffer[..n];
    }

    int Transfer(byte[] buffer, int count, bool write, int timeoutMs)
    {
        using var done = new ManualResetEvent(false);
        var overlapped = (NativeOverlapped*)NativeMemory.AllocZeroed((nuint)sizeof(NativeOverlapped));
        try
        {
            overlapped->EventHandle = done.SafeWaitHandle.DangerousGetHandle();
            fixed (byte* p = buffer)
            {
                bool started = write
                    ? WriteFile(_handle, p, (uint)count, 0, overlapped)
                    : ReadFile(_handle, p, (uint)count, 0, overlapped);
                if (!started)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != ERROR_IO_PENDING) throw new PrinterException("USB transfer failed.", new Win32Exception(error));
                }
                if (!done.WaitOne(timeoutMs)) CancelIoEx(_handle, overlapped);
                // Waits for the cancel to land too, so the buffer and OVERLAPPED stay valid until the driver lets go.
                if (!GetOverlappedResult(_handle, overlapped, out uint transferred, true))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == ERROR_OPERATION_ABORTED) return 0;
                    throw new PrinterException("USB transfer failed.", new Win32Exception(error));
                }
                return (int)transferred;
            }
        }
        finally
        {
            NativeMemory.Free(overlapped);
        }
    }

    public void Dispose() => _handle.Dispose();

    const uint DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;
    const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;
    const int ERROR_IO_PENDING = 997, ERROR_OPERATION_ABORTED = 995;

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public nint Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern nint SetupDiGetClassDevsW(ref Guid classGuid, string? enumerator, nint hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(nint deviceInfoSet, nint deviceInfoData, ref Guid interfaceClassGuid,
                                                   uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool SetupDiGetDeviceInterfaceDetailW(nint deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
                                                        nint detail, uint detailSize, out uint requiredSize, nint deviceInfoData);

    [DllImport("setupapi.dll")]
    static extern bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFileW(string fileName, uint access, uint share, nint security,
                                             uint creation, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool WriteFile(SafeFileHandle file, byte* buffer, uint count, nint written, NativeOverlapped* overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool ReadFile(SafeFileHandle file, byte* buffer, uint count, nint read, NativeOverlapped* overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetOverlappedResult(SafeFileHandle file, NativeOverlapped* overlapped, out uint transferred, bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CancelIoEx(SafeFileHandle file, NativeOverlapped* overlapped);
}
