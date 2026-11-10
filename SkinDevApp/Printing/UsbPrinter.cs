// ============================================================================
// UsbPrinter.cs  -  namespace SkinDevApp.Printing
//
// The thermal receipt printer connects with its own USB cable. Windows shows it as a "USB printing" device ("gxmcu micro-printer",
// vendor 28E9) without a printer queue or driver, so LUMYVUE opens the device directly and writes the ESC/POS bytes to it.
// Nothing needs to be installed. Only known receipt-printer vendors are used, so a document printer (for example an inkjet) can never
// receive receipt data.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SkinDevApp.Printing
{
    public static class UsbPrinter
    {
        // USB vendor ids of thermal receipt printers (device paths contain "vid_xxxx").
        private static readonly string[] ReceiptVendors = { "28e9", "0416", "0483", "0519", "0fe6" };

        private static readonly Guid UsbPrintInterface = new Guid("28d78fad-5a12-11d1-ae5b-0000f803a8c2");

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid g, int index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr info);
        [DllImport("setupapi.dll")]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(SafeFileHandle h, byte[] buffer, int count, out int written, IntPtr overlapped);

        /// <summary>The device path of the connected receipt printer, or null when none is connected.</summary>
        public static string FindPath()
        {
            try
            {
                return Paths().FirstOrDefault(p => ReceiptVendors.Any(v => p.IndexOf("vid_" + v, StringComparison.OrdinalIgnoreCase) >= 0));
            }
            catch { return null; }
        }

        private static List<string> Paths()
        {
            var result = new List<string>();
            Guid g = UsbPrintInterface;
            IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);        // DIGCF_PRESENT | DIGCF_DEVICEINTERFACE
            if (set == (IntPtr)(-1)) return result;
            try
            {
                for (int i = 0; ; i++)
                {
                    var data = new SP_DEVICE_INTERFACE_DATA();
                    data.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref data)) break;
                    int required;
                    SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out required, IntPtr.Zero);
                    if (required <= 0) continue;
                    IntPtr buffer = Marshal.AllocHGlobal(required);
                    try
                    {
                        Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);                // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA
                        if (SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, required, out required, IntPtr.Zero))
                            result.Add(Marshal.PtrToStringAuto(new IntPtr(buffer.ToInt64() + 4)));
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return result;
        }

        /// <summary>Opens the printer for writing (throws a plain IOException when it cannot be opened).</summary>
        public static SafeFileHandle Open(string path)
        {
            SafeFileHandle h = CreateFile(path, 0x40000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);     // GENERIC_WRITE, share read/write, OPEN_EXISTING
            if (h.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                h.Dispose();
                throw new System.IO.IOException("The USB printer could not be opened (Windows error " + err + "). Switch the printer off and on, then reconnect the USB cable.");
            }
            return h;
        }

        public static void Write(SafeFileHandle h, byte[] bytes)
        {
            int offset = 0;
            while (offset < bytes.Length)
            {
                byte[] part = offset == 0 ? bytes : bytes.Skip(offset).ToArray();
                int written;
                if (!WriteFile(h, part, part.Length, out written, IntPtr.Zero) || written <= 0)
                    throw new System.IO.IOException("The USB printer stopped accepting data (Windows error " + Marshal.GetLastWin32Error() + "). Check that it is switched on and connected.");
                offset += written;
            }
        }
    }
}
