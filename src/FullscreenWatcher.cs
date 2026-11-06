using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace BatteryMonitor
{
    /// <summary>
    /// 判断前台窗口是否占满整个监视器 —— 视频全屏、无边框全屏游戏都靠这个识别。
    ///
    /// 不用 SHQueryUserNotificationState 当主判据：实测在同一台机器上，
    /// 前台只是普通窗口时它也会返回 QUNS_BUSY。这里的 QUNS 只用于诊断日志。
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
        static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr h, out RECT r);

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);

        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr h, StringBuilder name, int max);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr h, StringBuilder text, int max);

        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr h, int index);

        [DllImport("shell32.dll")]
        static extern int SHQueryUserNotificationState(out int state);

        const uint MONITOR_DEFAULTTONEAREST = 2;
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TOPMOST = 0x00000008;
        const int WS_EX_TOOLWINDOW = 0x00000080;

        /// <summary>允许的像素误差，避免 -8,-8 这种边框偏移漏判。</summary>
        const int Tolerance = 2;

        static readonly uint SelfPid = (uint)Process.GetCurrentProcess().Id;

        /// <summary>前台窗口是否铺满它所在的显示器（排除本程序自己的窗口）。</summary>
        public static bool IsForegroundFullscreen()
        {
            IntPtr hwnd;
            Rectangle bounds;
            return TryGetForegroundFullscreen(out hwnd, out bounds);
        }

        /// <summary>
        /// 前台全屏窗口的句柄与它所在显示器的矩形。
        /// 悬浮层要画到「这块屏幕」的右上角，而不是固定画在主屏。
        /// </summary>
        public static bool TryGetForegroundFullscreen(out IntPtr hwnd, out Rectangle monitorBounds)
        {
            hwnd = IntPtr.Zero;
            monitorBounds = Rectangle.Empty;

            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;

            uint pid;
            GetWindowThreadProcessId(fg, out pid);
            if (pid == SelfPid) return false;

            // 桌面、任务视图这类外壳窗口的矩形也是整块屏幕，必须先把它们排掉：
            // 否则点一下桌面 / 按 Win+D，悬浮层就会误以为自己该出现。
            var cls = new StringBuilder(256);
            GetClassName(fg, cls, cls.Capacity);
            if (ShellWindowRules.ShouldIgnore(fg, GetShellWindow(), GetDesktopWindow(), cls.ToString()))
                return false;

            RECT r;
            if (!GetWindowRect(fg, out r)) return false;

            IntPtr mon = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(mon, ref mi)) return false;

            RECT m = mi.rcMonitor;
            if (!(r.Left <= m.Left + Tolerance
                  && r.Top <= m.Top + Tolerance
                  && r.Right >= m.Right - Tolerance
                  && r.Bottom >= m.Bottom - Tolerance))
                return false;

            hwnd = fg;
            monitorBounds = Rectangle.FromLTRB(m.Left, m.Top, m.Right, m.Bottom);
            return true;
        }

        /// <summary>给日志用的描述：类名、标题、是否 topmost。</summary>
        public static string Describe(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "(null)";

            var cls = new StringBuilder(256);
            GetClassName(hwnd, cls, cls.Capacity);
            var title = new StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);

            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            var sb = new StringBuilder();
            sb.Append("class=").Append(cls);
            sb.Append(" topmost=").Append((ex & WS_EX_TOPMOST) != 0);
            sb.Append(" tool=").Append((ex & WS_EX_TOOLWINDOW) != 0);
            if (title.Length > 0) sb.Append(" title=").Append(title);
            return sb.ToString();
        }

        /// <summary>系统眼里的通知状态；3 = QUNS_RUNNING_D3D_FULL_SCREEN（独占全屏）。</summary>
        public static int NotificationState()
        {
            int state;
            if (SHQueryUserNotificationState(out state) != 0) return -1;
            return state;
        }
    }
}
