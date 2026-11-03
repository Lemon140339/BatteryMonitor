using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BatteryMonitor
{
    /// <summary>
    /// 判断前台窗口是否占满整个监视器 —— 视频全屏、无边框全屏游戏都靠这个识别。
    ///
    /// 不用 SHQueryUserNotificationState：实测在同一台机器上，前台只是普通窗口时
    /// 它也会返回 QUNS_BUSY，靠不住。
    ///
    /// 注意：真正的独占全屏（D3D exclusive）会接管交换链，分层窗口画不上去，
    /// 这种情况只能看到托盘（而且托盘也被游戏盖住）；无边框全屏才能浮出悬浮层。
    /// </summary>
    public static class FullscreenWatcher
    {
        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr h, out RECT r);

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);

        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        const uint MONITOR_DEFAULTTONEAREST = 2;

        /// <summary>允许的像素误差，避免 -8,-8 这种边框偏移漏判。</summary>
        const int Tolerance = 2;

        static readonly uint SelfPid = (uint)Process.GetCurrentProcess().Id;

        /// <summary>前台窗口是否铺满它所在的显示器（排除本程序自己的窗口）。</summary>
        public static bool IsForegroundFullscreen()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;

            uint pid;
            GetWindowThreadProcessId(fg, out pid);
            if (pid == SelfPid) return false;

            return CoversMonitor(fg);
        }

        /// <summary>给定窗口是否铺满所在显示器。</summary>
        public static bool CoversMonitor(IntPtr hwnd)
        {
            RECT r;
            if (!GetWindowRect(hwnd, out r)) return false;

            IntPtr mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(mon, ref mi)) return false;

            RECT m = mi.rcMonitor;
            return r.Left <= m.Left + Tolerance
                && r.Top <= m.Top + Tolerance
                && r.Right >= m.Right - Tolerance
                && r.Bottom >= m.Bottom - Tolerance;
        }
    }
}
