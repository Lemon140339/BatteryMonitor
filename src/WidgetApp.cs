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
        readonly Timer _topTimer;
        readonly WinEventDelegate _winEventProc;
        readonly IntPtr _hook;

        DisplayMode _mode = DisplayMode.Auto;
        bool _overlayVisible;
        bool _refreshing;
        bool _disposed;
        IntPtr _lastFullscreenWindow = IntPtr.Zero;
        bool _lastCovered;
        string _lastOffender;

        public WidgetApp(IList<DeviceWorker> workers, IList<string> problems)
        {
            _workers = workers ?? new List<DeviceWorker>();
            _problems = problems ?? new List<string>();

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

            // 全屏期间单独用高频计时器把 Z 序顶回去：
            // 有的播放器每帧都在给自己 SetWindowPos，1 秒一次抢不过它
            _topTimer = new Timer();
            _topTimer.Interval = 200;
            _topTimer.Tick += delegate { KeepOnTop(); };

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
            foreach (var w in workers) rows.Add(new WidgetRow(w.Name, w.Percent, null));
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
                _widget.RaiseTopmost();
                _widget.RenderNow();
                if (!_topTimer.Enabled) _topTimer.Start();
            }
            else if (_topTimer.Enabled)
            {
                _topTimer.Stop();
            }
        }

        /// <summary>
        /// 高频把悬浮层顶回最前，并在被挡住时记一次日志（不刷屏）。
        /// 只在显示期间运行。
        /// </summary>
        void KeepOnTop()
        {
            if (_disposed || !_overlayVisible)
            {
                _topTimer.Stop();
                return;
            }

            // 先看「此刻是否被压住」，再往上顶。
            // 顺序反过来的话，检查永远发生在我们自己顶完之后，测不到真实的竞争结果。
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

            _widget.RaiseTopmost();
        }

        void OnModeRequested(DisplayMode mode)
        {
            _mode = mode;
            DebugLog.Write("显示模式切换为 " + ModeText(mode));
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
            _topTimer.Stop();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _disposed = true;
                try { if (_hook != IntPtr.Zero) UnhookWinEvent(_hook); } catch { }
                try { _timer.Stop(); } catch { }
                try { _topTimer.Stop(); } catch { }
                try { _tray.Dispose(); } catch { }
                try { _widget.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}
