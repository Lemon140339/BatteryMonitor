using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace BatteryMonitor
{
    /// <summary>通用 HID 设备句柄：按 VID/PID/接口片段查找并打开。</summary>
    public sealed class HidDevice : IDisposable
    {
        static readonly Guid GUID_HID = new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct OVERLAPPED
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public int Offset;
            public int OffsetHigh;
            public IntPtr hEvent;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr h, int f);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr h, IntPtr info, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA d);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr h, ref SP_DEVICE_INTERFACE_DATA d, IntPtr buf, int size, ref int required, IntPtr info);

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr templ);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadFile(IntPtr h, byte[] buf, int n, IntPtr read, ref OVERLAPPED ov);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteFile(IntPtr h, byte[] buf, int n, IntPtr written, ref OVERLAPPED ov);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetOverlappedResult(IntPtr h, ref OVERLAPPED ov, ref int n, bool wait);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CancelIoEx(IntPtr h, ref OVERLAPPED ov);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateEvent(IntPtr sa, bool manual, bool initial, IntPtr name);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern int WaitForSingleObject(IntPtr h, int ms);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr h);

        const int DIGCF_PRESENT = 0x02;
        const int DIGCF_DEVICEINTERFACE = 0x10;
        const uint GENERIC_RW = 0xC0000000;
        const uint SHARE_RW = 0x03;
        const uint OPEN_EXISTING = 3;
        const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        const int ERROR_IO_PENDING = 997;
        const int WAIT_OBJECT_0 = 0;

        IntPtr _handle = new IntPtr(-1);

        /// <summary>枚举当前系统里所有 HID 接口路径（一次 SetupAPI 扫描）。</summary>
        public static List<string> EnumeratePaths()
        {
            var paths = new List<string>();

            Guid g = GUID_HID;
            IntPtr hdev = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (hdev == new IntPtr(-1)) return paths;
            try
            {
                for (int i = 0; ; i++)
                {
                    var did = new SP_DEVICE_INTERFACE_DATA();
                    did.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                    if (!SetupDiEnumDeviceInterfaces(hdev, IntPtr.Zero, ref g, i, ref did)) break;

                    int need = 0;
                    SetupDiGetDeviceInterfaceDetail(hdev, ref did, IntPtr.Zero, 0, ref need, IntPtr.Zero);
                    if (need <= 0) continue;

                    IntPtr buf = Marshal.AllocHGlobal(need);
                    try
                    {
                        // SP_DEVICE_INTERFACE_DETAIL_DATA.cbSize：64 位是 8，32 位是 6
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        int got = 0;
                        if (!SetupDiGetDeviceInterfaceDetail(hdev, ref did, buf, need, ref got, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4));
                        if (!string.IsNullOrEmpty(path)) paths.Add(path);
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(hdev); }
            return paths;
        }

        /// <summary>
        /// 接口路径是否匹配 VID/PID（路径里是 "vid_1d57&amp;pid_fa65" 这种）以及可选的接口片段。
        /// 抽成纯函数，方便直接测（tests/ProfileModelTest.cs）。
        /// </summary>
        public static bool MatchesPath(string path, string vid, string pid, string iface)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(vid) || string.IsNullOrEmpty(pid)) return false;
            string low = path.ToLowerInvariant();
            if (!low.Contains(("vid_" + vid + "&pid_" + pid).ToLowerInvariant())) return false;
            if (!string.IsNullOrEmpty(iface) && !low.Contains(iface.ToLowerInvariant())) return false;
            return true;
        }

        public static string FindPath(string vid, string pid, string iface)
        {
            if (string.IsNullOrEmpty(vid) || string.IsNullOrEmpty(pid)) return null;
            foreach (string path in EnumeratePaths())
            {
                if (MatchesPath(path, vid, pid, iface)) return path;
            }
            return null;
        }

        public static HidDevice Open(string vid, string pid, string iface)
        {
            string path = FindPath(vid, pid, iface);
            if (path == null) return null;
            IntPtr h = CreateFile(path, GENERIC_RW, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (h == new IntPtr(-1)) return null;
            var dev = new HidDevice();
            dev._handle = h;
            return dev;
        }

        public bool IsOpen { get { return _handle != new IntPtr(-1); } }

        public int Read(byte[] buf, int timeoutMs)
        {
            if (!IsOpen) return -1;
            IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
            if (ev == IntPtr.Zero) return -1;
            try
            {
                var ov = new OVERLAPPED();
                ov.hEvent = ev;
                bool pending = false;
                if (!ReadFile(_handle, buf, buf.Length, IntPtr.Zero, ref ov))
                {
                    if (Marshal.GetLastWin32Error() != ERROR_IO_PENDING) return -1;
                    pending = true;
                }
                return Finish(ev, ref ov, pending, timeoutMs);
            }
            finally { CloseHandle(ev); }
        }

        public bool Write(byte[] buf, int timeoutMs)
        {
            if (!IsOpen) return false;
            IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
            if (ev == IntPtr.Zero) return false;
            try
            {
                var ov = new OVERLAPPED();
                ov.hEvent = ev;
                bool pending = false;
                if (!WriteFile(_handle, buf, buf.Length, IntPtr.Zero, ref ov))
                {
                    if (Marshal.GetLastWin32Error() != ERROR_IO_PENDING) return false;
                    pending = true;
                }
                return Finish(ev, ref ov, pending, timeoutMs) >= 0;
            }
            finally { CloseHandle(ev); }
        }

        /// <summary>
        /// 收尾这一笔 overlapped IO，返回传输字节数（失败 -1）。
        /// 超时后必须先 CancelIoEx 再等它真正结束：否则内核还会往这块栈上的
        /// OVERLAPPED 写结果，而调用方已经往下走了。
        /// </summary>
        int Finish(IntPtr ev, ref OVERLAPPED ov, bool pending, int timeoutMs)
        {
            if (pending)
            {
                if (WaitForSingleObject(ev, timeoutMs) != WAIT_OBJECT_0)
                {
                    int ignored = 0;
                    CancelIoEx(_handle, ref ov);
                    GetOverlappedResult(_handle, ref ov, ref ignored, true);
                    return -1;
                }
            }
            int n = 0;
            if (!GetOverlappedResult(_handle, ref ov, ref n, false)) return -1;
            return n;
        }

        public void Dispose()
        {
            if (IsOpen) { CloseHandle(_handle); _handle = new IntPtr(-1); }
        }
    }
}
