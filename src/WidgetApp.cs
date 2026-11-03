using System;
using System.Collections.Generic;
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
        readonly IList<DeviceWorker> _workers;
        readonly IList<string> _problems;
        readonly WidgetForm _widget;
        readonly TrayView _tray;
        readonly Timer _timer;

        DisplayMode _mode = DisplayMode.Auto;
        bool _overlayVisible;

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

            _timer = new Timer();
            _timer.Interval = 1000;
            _timer.Tick += delegate { Refresh(); };
            _timer.Start();

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

        // ---------------- 每秒一次：刷新数据、判断全屏、切换显示 ----------------

        void Refresh()
        {
            var rows = BuildRows(_workers);
            _tray.Update(rows, _mode);

            bool fullscreen = FullscreenWatcher.IsForegroundFullscreen();
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
                // 每秒顶一次：全屏播放器自己是 topmost，激活后会盖在我们上面
                _widget.RaiseTopmost();
                _widget.RenderNow();
            }
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
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _timer.Stop(); } catch { }
                try { _tray.Dispose(); } catch { }
                try { _widget.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}
