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

        // ---- hid.dll：报告描述符与 Feature Report ----
        // 有些设备只认 Feature 报告（MCHOSE/Corsair 那类厂商通道），
        // 用 WriteFile/ReadFile 走中断端点反而拿不到；另一些相反。
        [StructLayout(LayoutKind.Sequential)]
        struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")]
        static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr data);

        [DllImport("hid.dll")]
        static extern bool HidD_FreePreparsedData(IntPtr data);

        [DllImport("hid.dll")]
        static extern int HidP_GetCaps(IntPtr data, ref HIDP_CAPS caps);

        [DllImport("hid.dll")]
        static extern bool HidD_GetFeature(IntPtr h, byte[] buf, int len);

        [DllImport("hid.dll")]
        static extern bool HidD_SetFeature(IntPtr h, byte[] buf, int len);

        [DllImport("hid.dll")]
        static extern bool HidD_SetOutputReport(IntPtr h, byte[] buf, int len);

        /// <summary>一个 HID 集合的描述：usage 页/用法 + 三种报告长度。</summary>
        public struct HidCaps
        {
            public ushort Usage;
            public ushort UsagePage;
            public int InputLength;
            public int OutputLength;
            public int FeatureLength;
        }

        const int DIGCF_PRESENT = 0x02;
        const int DIGCF_DEVICEINTERFACE = 0x10;
        const uint GENERIC_RW = 0xC0000000;
        const uint SHARE_RW = 0x03;
        const uint OPEN_EXISTING = 3;
        const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        const int ERROR_IO_PENDING = 997;
        const int WAIT_OBJECT_0 = 0;

        IntPtr _handle = new IntPtr(-1);
        string _path;
        HidCaps _caps;

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

        /// <summary>
        /// 读一个接口的报告描述符能力。用 0 权限打开：鼠标/键盘的顶层集合被系统独占，
        /// 只有 0 权限能读 caps（这也正是 docs/DESIGN.md 里"只读打开"那条约束的来源）。
        /// </summary>
        public static HidCaps? GetCaps(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            IntPtr h = CreateFile(path, 0, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == new IntPtr(-1)) return null;
            IntPtr prep = IntPtr.Zero;
            try
            {
                if (!HidD_GetPreparsedData(h, out prep) || prep == IntPtr.Zero) return null;
                var raw = new HIDP_CAPS();
                if (HidP_GetCaps(prep, ref raw) != 0) return null;
                var caps = new HidCaps();
                caps.Usage = raw.Usage;
                caps.UsagePage = raw.UsagePage;
                caps.InputLength = raw.InputReportByteLength;
                caps.OutputLength = raw.OutputReportByteLength;
                caps.FeatureLength = raw.FeatureReportByteLength;
                return caps;
            }
            finally
            {
                if (prep != IntPtr.Zero) HidD_FreePreparsedData(prep);
                CloseHandle(h);
            }
        }

        /// <summary>
        /// 按 usage 页/用法找接口。配置里写 usage 比写接口路径片段更稳：
        /// 同一个接收器常常拆成好几个集合（鼠标 / 键盘 / 厂商自定义），路径里的 colNN 会随固件变。
        /// usage 传 0 表示只匹配 usage 页。
        /// </summary>
        public static string FindPathByUsage(string vid, string pid, string iface, ushort usagePage, ushort usage)
        {
            if (string.IsNullOrEmpty(vid) || string.IsNullOrEmpty(pid)) return null;
            string best = null;
            int bestFeature = -1;
            foreach (string path in EnumeratePaths())
            {
                if (!MatchesPath(path, vid, pid, iface)) continue;
                HidCaps? c = GetCaps(path);
                if (c == null) continue;
                if (c.Value.UsagePage != usagePage) continue;
                if (usage != 0 && c.Value.Usage != usage) continue;
                // 同页多个集合时优先 Feature 报告更长的那个（厂商电量通道一般是最大的）
                if (c.Value.FeatureLength > bestFeature)
                {
                    bestFeature = c.Value.FeatureLength;
                    best = path;
                }
            }
            return best;
        }

        public static HidDevice Open(string vid, string pid, string iface)
        {
            string path = FindPath(vid, pid, iface);
            if (path == null) return null;
            return OpenPath(path);
        }

        /// <summary>按完整接口路径打开（已拿到路径时用，省一次枚举）。</summary>
        public static HidDevice OpenPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            IntPtr h = CreateFile(path, GENERIC_RW, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (h == new IntPtr(-1)) return null;
            var dev = new HidDevice();
            dev._handle = h;
            dev._path = path;
            HidCaps? caps = GetCaps(path);
            if (caps != null) dev._caps = caps.Value;
            return dev;
        }

        public bool IsOpen { get { return _handle != new IntPtr(-1); } }

        public string Path { get { return _path; } }

        public HidCaps Caps { get { return _caps; } }

        /// <summary>Feature 报告读：缓冲首字节是报告 ID，长度必须与描述符里的该报告长度一致。</summary>
        public bool GetFeature(byte[] buf)
        {
            if (!IsOpen || buf == null || buf.Length == 0) return false;
            return HidD_GetFeature(_handle, buf, buf.Length);
        }

        /// <summary>Feature 报告写：同样是覆盖式写入，首字节报告 ID。</summary>
        public bool SetFeature(byte[] buf)
        {
            if (!IsOpen || buf == null || buf.Length == 0) return false;
            return HidD_SetFeature(_handle, buf, buf.Length);
        }

        /// <summary>Output 报告的另一种下发方式：有些设备拒绝 WriteFile，但认 HidD_SetOutputReport。</summary>
        public bool SetOutputReport(byte[] buf)
        {
            if (!IsOpen || buf == null || buf.Length == 0) return false;
            return HidD_SetOutputReport(_handle, buf, buf.Length);
        }

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
