using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace BatteryMonitor
{
    /// <summary>悬浮层的显示策略。</summary>
    public enum DisplayMode
    {
        /// <summary>自动：全屏（视频 / 无边框游戏）时显示悬浮层，平时只留托盘。</summary>
        Auto = 0,
        /// <summary>总是显示悬浮层。</summary>
        AlwaysOn = 1,
        /// <summary>隐藏悬浮层，只留托盘。</summary>
        Hidden = 2
    }

    /// <summary>
    /// 托盘图标：代码画的简约电池（按最低电量着色）+ 悬停提示 + 右键菜单。
    /// 注册失败（没提权时 Shell_NotifyIcon 返回 ACCESS_DENIED）不会崩，
    /// 只是把 Available 置为 false，由上层退回悬浮层。
    /// </summary>
    public sealed class TrayView : IDisposable
    {
        const int IconSize = 32;
        const int MaxTipLength = 63;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool DestroyIcon(IntPtr hIcon);

        readonly NotifyIcon _icon;
        readonly ContextMenuStrip _menu;
        readonly List<ToolStripItem> _deviceItems = new List<ToolStripItem>();
        readonly ToolStripMenuItem _autoItem;
        readonly ToolStripMenuItem _alwaysItem;
        readonly ToolStripMenuItem _hiddenItem;

        string[] _lastLines;
        int _lastPercent = int.MinValue;
        Icon _current;
        bool _disposed;

        /// <summary>托盘是否真的注册成功（非提权时通常为 false）。</summary>
        public bool Available { get; private set; }

        public event Action<DisplayMode> ModeRequested;
        public event Action QuitRequested;

        public TrayView()
        {
            _menu = new ContextMenuStrip();

            _autoItem = MakeModeItem("自动：全屏时显示悬浮层", DisplayMode.Auto);
            _alwaysItem = MakeModeItem("总是显示悬浮层", DisplayMode.AlwaysOn);
            _hiddenItem = MakeModeItem("隐藏悬浮层，只留托盘", DisplayMode.Hidden);

            var quit = new ToolStripMenuItem("退出");
            quit.Click += delegate { Raise(QuitRequested); };

            _menu.Items.AddRange(new ToolStripItem[]
            {
                _autoItem,
                _alwaysItem,
                _hiddenItem,
                new ToolStripSeparator(),
                quit
            });

            _icon = new NotifyIcon();
            _icon.Icon = MakeIcon(-1);
            _icon.Text = "BatteryMonitor";
            _icon.ContextMenuStrip = _menu;
        }

        /// <summary>尝试注册到通知区。失败不抛，只返回 false。</summary>
        public bool Attach()
        {
            try
            {
                _icon.Visible = true;
                Available = true;
            }
            catch (Exception ex)
            {
                Available = false;
                DebugLog.Write("托盘注册失败：" + ex.Message);
            }
            return Available;
        }

        public void Update(IList<WidgetRow> rows, DisplayMode mode)
        {
            if (_disposed) return;

            UpdateDeviceItems(rows);
            SetChecks(mode);

            int min = MinPercent(rows);
            if (min != _lastPercent)
            {
                _lastPercent = min;
                Icon next = MakeIcon(min);
                Icon old = _current;
                _current = next;
                if (Available) _icon.Icon = next;
                if (old != null) old.Dispose();
            }

            if (Available) _icon.Text = BuildTip(rows);
        }

        void SetChecks(DisplayMode mode)
        {
            _autoItem.Checked = mode == DisplayMode.Auto;
            _alwaysItem.Checked = mode == DisplayMode.AlwaysOn;
            _hiddenItem.Checked = mode == DisplayMode.Hidden;
        }

        ToolStripMenuItem MakeModeItem(string text, DisplayMode mode)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += delegate { var h = ModeRequested; if (h != null) h(mode); };
            return item;
        }

        void UpdateDeviceItems(IList<WidgetRow> rows)
        {
            var lines = new List<string>();
            if (rows != null)
            {
                foreach (var r in rows)
                {
                    if (r == null || string.IsNullOrEmpty(r.Name)) continue;
                    lines.Add(r.Name + "  " + r.ValueText);
                }
            }

            if (SameLines(lines)) return;

            foreach (var it in _deviceItems) _menu.Items.Remove(it);
            _deviceItems.Clear();
            for (int i = 0; i < lines.Count; i++)
            {
                var item = new ToolStripMenuItem(lines[i]);
                item.Enabled = false;
                _menu.Items.Insert(i, item);
                _deviceItems.Add(item);
            }
            _lastLines = lines.ToArray();
        }

        bool SameLines(List<string> lines)
        {
            if (_lastLines == null || _lastLines.Length != lines.Count) return false;
            for (int i = 0; i < lines.Count; i++)
                if (_lastLines[i] != lines[i]) return false;
            return true;
        }

        static int MinPercent(IList<WidgetRow> rows)
        {
            int min = -1;
            if (rows == null) return min;
            foreach (var r in rows)
            {
                if (r == null) continue;
                if (r.Percent < 0) continue;
                if (min < 0 || r.Percent < min) min = r.Percent;
            }
            return min;
        }

        static string BuildTip(IList<WidgetRow> rows)
        {
            var sb = new StringBuilder("BatteryMonitor");
            if (rows != null)
            {
                foreach (var r in rows)
                {
                    if (r == null || string.IsNullOrEmpty(r.Name)) continue;
                    sb.Append("\r\n").Append(r.Name).Append(' ').Append(r.ValueText);
                }
            }
            string s = sb.ToString();
            if (s.Length > MaxTipLength) s = s.Substring(0, MaxTipLength);
            return s;
        }

        // ---------------- 图标：一个电池轮廓，按电量着色并填充 ----------------

        static Icon MakeIcon(int percent)
        {
            using (var bmp = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    Color c = WidgetRender.ColorOf(percent);

                    using (var body = Rounded(new RectangleF(2.5f, 7.5f, 21f, 17f), 4f))
                    using (var pen = new Pen(c, 2.4f))
                        g.DrawPath(pen, body);

                    using (var cap = Rounded(new RectangleF(24.5f, 13f, 4.5f, 6f), 1.5f))
                    using (var pen = new Pen(c, 1.6f))
                        g.DrawPath(pen, cap);

                    if (percent > 0)
                    {
                        float innerWidth = 14f;
                        float fillWidth = innerWidth * Math.Min(percent, 100) / 100f;
                        if (fillWidth < 2f) fillWidth = 2f;
                        var rect = new RectangleF(5.5f, 10.5f, fillWidth, 11f);
                        using (var fill = Rounded(rect, 2f))
                        using (var brush = new SolidBrush(c))
                            g.FillPath(brush, fill);
                    }
                }

                IntPtr h = bmp.GetHicon();
                try
                {
                    using (var tmp = Icon.FromHandle(h))
                        return (Icon)tmp.Clone();
                }
                finally { DestroyIcon(h); }
            }
        }

        static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2f;
            p.AddArc(r.X, r.Y, d, d, 180f, 90f);
            p.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
            p.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
            p.CloseFigure();
            return p;
        }

        static void Raise(Action handler)
        {
            if (handler != null) handler();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _icon.Visible = false; } catch { }
            _icon.Dispose();
            _menu.Dispose();
            if (_current != null) { _current.Dispose(); _current = null; }
        }
    }
}
