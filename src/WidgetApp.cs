using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BatteryMonitor
{
    /// <summary>
    /// 把数据、托盘和悬浮层组装起来，并决定悬浮层什么时候出现：
    ///
    ///   托盘可用（已提权）  → 自动模式：全屏时浮出，平时只留托盘
    ///   托盘不可用（没提权）→ 退回「总是显示悬浮层」，否则用户什么都看不到
    ///
    /// 策略是纯判断，不碰窗口；窗口只是被动 Show/Hide。
    /// </summary>
    public sealed class WidgetApp : ApplicationContext
    {
        delegate void WinEventDelegate(IntPtr hook, uint evt, IntPtr hwnd,
            int idObject, int idChild, uint thread, uint time);

        [DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmod,
            WinEventDelegate proc, uint pid, uint thread, uint flags);

        [DllImport("user32.dll")]
        static extern bool UnhookWinEvent(IntPtr hook);

        const uint EVENT_SYSTEM_FOREGROUND = 3;
        const uint WINEVENT_OUTOFCONTEXT = 0;
        const uint WINEVENT_SKIPOWNPROCESS = 2;

        readonly IList<DeviceWorker> _workers;
        readonly IList<string> _problems;
        readonly WidgetForm _widget;
        readonly TrayView _tray;
        readonly Timer _timer;
        readonly WinEventDelegate _winEventProc;
        readonly IntPtr _hook;

        DisplayMode _mode = DisplayMode.Auto;
        bool _overlayVisible;
        bool _refreshing;
        bool _disposed;
        IntPtr _lastFullscreenWindow = IntPtr.Zero;
        bool _lastCovered;
        string _lastOffender;
        string _lastNeighborAbove;
        readonly bool _selfCheck;
        int _selfCheckTicks;

        public WidgetApp(IList<DeviceWorker> workers, IList<string> problems, bool selfCheck)
        {
            _workers = workers ?? new List<DeviceWorker>();
            _problems = problems ?? new List<string>();
            _selfCheck = selfCheck;

            // 显示模式是用户的选择，要跨进程记住：上次选了「只留托盘」，
            // 下次开机就还是「只留托盘」，不要每次启动都回到「自动」。
            _mode = AppSettings.LoadMode();

            _widget = new WidgetForm(_workers, _problems);
            _tray = new TrayView();
            _tray.ModeRequested += OnModeRequested;
            _tray.QuitRequested += Quit;

            bool trayOk = _tray.Attach();
            DebugLog.Write("启动：托盘=" + (trayOk ? "可用" : "不可用") + "，模式=" + ModeText(_mode));

            // 前台窗口一变就重算，别等下一秒；定时器仍是兜底
            _winEventProc = OnForegroundChanged;
            _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

            _timer = new Timer();
            _timer.Interval = 1000;
            _timer.Tick += delegate { Refresh(); };
            _timer.Start();

            Refresh();
        }

        void OnForegroundChanged(IntPtr hook, uint evt, IntPtr hwnd,
            int idObject, int idChild, uint thread, uint time)
        {
            if (_disposed) return;
            Refresh();
        }

        // ---------------- 供 WidgetForm 复用的数据整理 ----------------

        public static List<WidgetRow> BuildRows(IList<DeviceWorker> workers)
        {
            var rows = new List<WidgetRow>();
            if (workers == null || workers.Count == 0)
            {
                rows.Add(new WidgetRow("无设备配置", -1, "devices/ 为空"));
                return rows;
            }

            // 只显示接口确实存在的设备：接收器没插/已拔掉的配置不占行
            foreach (var w in workers)
            {
                if (w.Present) rows.Add(new WidgetRow(w.Name, w.Percent, null));
            }

            if (rows.Count == 0) rows.Add(new WidgetRow("未检测到设备", -1, null));
            return rows;
        }

        public static string WarnText(IList<string> problems)
        {
            if (problems == null || problems.Count == 0) return null;
            return "配置有误 " + problems.Count + " 项";
        }

        /// <summary>
        /// 该不该显示悬浮层。抽成静态方法是为了能直接验证策略，不用开窗口。
        /// </summary>
        public static bool ShouldShowOverlay(bool trayAvailable, DisplayMode mode, bool fullscreen)
        {
            if (!trayAvailable) return true;          // 没托盘，只剩悬浮层这一条路
            if (mode == DisplayMode.AlwaysOn) return true;
            if (mode == DisplayMode.Hidden) return false;
            return fullscreen;                        // Auto：全屏才浮出来
        }

        // ---------------- 每秒一次 / 前台变化时：刷新数据、判断全屏、切换显示 ----------------

        void Refresh()
        {
            // 前台变化回调可能和定时器撞在同一拍，简单起见直接重入保护
            if (_refreshing || _disposed) return;
            _refreshing = true;
            try
            {
                RefreshCore();
            }
            finally { _refreshing = false; }
        }

        void RefreshCore()
        {
            // 接口存在与否决定显示哪些设备（接收器拔掉后立刻隐藏那一行）
            HidPresence.Refresh();
            for (int i = 0; i < _workers.Count; i++)
            {
                var w = _workers[i];
                bool present = HidPresence.IsPresent(w.Profile);
                if (present != w.Present)
                {
                    w.SetPresent(present);
                    DebugLog.Write((present ? "设备接入：" : "设备移除：") + w.Name);
                }
            }

            var rows = BuildRows(_workers);
            _tray.Update(rows, _mode);

            IntPtr fullscreenWindow;
            Rectangle fullscreenScreen;
            bool fullscreen = FullscreenWatcher.TryGetForegroundFullscreen(out fullscreenWindow, out fullscreenScreen);

            if (fullscreen && fullscreenWindow != _lastFullscreenWindow)
            {
                _lastFullscreenWindow = fullscreenWindow;
                _widget.TargetScreen = fullscreenScreen;
                DebugLog.Write("全屏窗口：" + FullscreenWatcher.Describe(fullscreenWindow)
                    + " screen=" + fullscreenScreen.Width + "x" + fullscreenScreen.Height
                    + " quns=" + FullscreenWatcher.NotificationState());
            }
            else if (!fullscreen && _lastFullscreenWindow != IntPtr.Zero)
            {
                _lastFullscreenWindow = IntPtr.Zero;
                _widget.TargetScreen = Screen.PrimaryScreen.Bounds;
            }

            bool show = ShouldShowOverlay(_tray.Available, _mode, fullscreen);

            if (show != _overlayVisible)
            {
                _overlayVisible = show;
                if (show) _widget.Show();
                else _widget.Hide();
                DebugLog.Write("悬浮层 " + (show ? "显示" : "隐藏")
                    + "（全屏=" + fullscreen + "，托盘=" + _tray.Available + "，模式=" + ModeText(_mode) + "）");
            }

            if (show)
            {
                // 每秒顶一次就够：前台一变还有 SetWinEventHook 立刻重算。
                // 曾经试过 200ms 高频顶，但对「直通呈现」的全屏应用（媒体播放器）毫无作用，
                // 只多烧 CPU，所以退回 1 秒一次。
                // 先看上一秒顶完之后有没有被谁盖住，再顶——顺序反过来就永远测不到竞争。
                string offender;
                if (_widget.IsCovered(out offender))
                {
                    if (!_lastCovered || offender != _lastOffender)
                    {
                        _lastCovered = true;
                        _lastOffender = offender;
                        DebugLog.Write("悬浮层被挡住：" + offender);
                    }
                }
                else if (_lastCovered)
                {
                    _lastCovered = false;
                    _lastOffender = null;
                    DebugLog.Write("悬浮层在最前");
                }

                string above = _widget.NeighborAbove();
                if (above != _lastNeighborAbove)
                {
                    _lastNeighborAbove = above;
                    DebugLog.Write(above == null
                        ? "悬浮层上方没有窗口（已在最顶层）"
                        : "悬浮层上方：" + above);
                }

                _widget.RaiseTopmost();
                _widget.RenderNow();

                // --selfcheck：每两秒从屏幕上抓自己那块，数白像素。
                // 数字接近 0 说明窗口根本没合成出来，而不是被谁压住。
                if (_selfCheck && (++_selfCheckTicks % 2) == 0)
                {
                    int w, h;
                    int visible = _widget.CountOwnPixels(out w, out h);
                    DebugLog.Write("自检：屏幕上的白像素=" + visible + "（区域 " + w + "x" + h + "）");
                }
            }
        }


        void OnModeRequested(DisplayMode mode)
        {
            _mode = mode;
            DebugLog.Write("显示模式切换为 " + ModeText(mode));
            AppSettings.SaveMode(mode);
            Refresh();
        }

        static string ModeText(DisplayMode mode)
        {
            switch (mode)
            {
                case DisplayMode.AlwaysOn: return "总是显示";
                case DisplayMode.Hidden: return "只留托盘";
                default: return "自动";
            }
        }

        void Quit()
        {
            DebugLog.Write("退出");
            _timer.Stop();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _disposed = true;
                try { if (_hook != IntPtr.Zero) UnhookWinEvent(_hook); } catch { }
                try { _timer.Stop(); } catch { }
                try { _tray.Dispose(); } catch { }
                try { _widget.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}
