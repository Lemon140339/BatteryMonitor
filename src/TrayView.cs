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
    ///
    /// 直接调 Shell_NotifyIcon，不用 WinForms 的 NotifyIcon ——
    /// 后者在注册失败时**不抛异常**（我们踩过：图标根本没出现，
    /// 但 Visible=true 一声不响，结果程序以为托盘可用、把悬浮层也藏了）。
    /// 这里以 NIM_ADD 的返回值作为唯一真相。
    /// </summary>
    public sealed class TrayView : IDisposable
    {
        const int IconSize = 32;
        const int MaxTipLength = 63;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA data);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern uint RegisterWindowMessage(string name);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool DestroyIcon(IntPtr hIcon);

        const uint NIM_ADD = 0;
        const uint NIM_MODIFY = 1;
        const uint NIM_DELETE = 2;
        const uint NIF_MESSAGE = 0x1;
        const uint NIF_ICON = 0x2;
        const uint NIF_TIP = 0x4;
        const int TrayCallbackMessage = 0x0400 + 42;
        const int WM_RBUTTONUP = 0x0205;
        const int WM_LBUTTONUP = 0x0202;
        const uint WM_NULL = 0x0000;

        /// <summary>托盘图标自己的消息窗口：只收托盘回调，不显示。</summary>
        sealed class TrayHost : Form
        {
            public int CallbackMessage;
            public int TaskbarCreatedMessage;
            public Action<int> MouseMessage;
            public Action TaskbarCreated;

            public TrayHost()
            {
                ShowInTaskbar = false;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(-32000, -32000, 1, 1);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == CallbackMessage)
                {
                    var h = MouseMessage;
                    if (h != null) h((int)m.LParam & 0xFFFF);
                    return;
                }
                if (TaskbarCreatedMessage != 0 && m.Msg == TaskbarCreatedMessage)
                {
                    // explorer 重启后通知区会清空，重新加一次
                    var h = TaskbarCreated;
                    if (h != null) h();
                    return;
                }
                base.WndProc(ref m);
            }
        }

        readonly TrayHost _host;
        readonly ContextMenuStrip _menu;
        readonly List<ToolStripItem> _deviceItems = new List<ToolStripItem>();
        readonly ToolStripMenuItem _autoItem;
        readonly ToolStripMenuItem _alwaysItem;
        readonly ToolStripMenuItem _hiddenItem;
        readonly uint _taskbarCreated;

        string[] _lastLines;
        string _lastSignature;
        Icon _icon;
        bool _added;
        bool _disposed;
        readonly bool _quiet;

        /// <summary>托盘是否真的加进去了（NIM_ADD 返回 true 才算）。</summary>
        public bool Available { get { return _added; } }

        public event Action<DisplayMode> ModeRequested;
        public event Action QuitRequested;

        public TrayView() : this(false)
        {
        }

        /// <param name="quiet">探测用：失败时不写日志，避免和真正那次重复。</param>
        public TrayView(bool quiet)
        {
            _quiet = quiet;
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

            _host = new TrayHost();
            _host.CallbackMessage = TrayCallbackMessage;
            _host.MouseMessage = OnTrayMouse;

            _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
            _host.TaskbarCreatedMessage = unchecked((int)_taskbarCreated);
            _host.TaskbarCreated = delegate { if (!_disposed) AddIcon(); };
        }

        /// <summary>把图标注册进通知区。失败返回 false，不抛异常。</summary>
        public bool Attach()
        {
            if (_disposed) return false;
            AddIcon();
            return _added;
        }

        void AddIcon()
        {
            if (_icon == null) _icon = MakeIcon(null);

            var data = BuildData();
            data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            data.szTip = Truncate("BatteryMonitor");

            _added = Shell_NotifyIcon(NIM_ADD, ref data);
            if (!_added && !_quiet)
                DebugLog.Write("托盘注册失败（err=" + Marshal.GetLastWin32Error() + "），改用悬浮层");
        }

        NOTIFYICONDATA BuildData()
        {
            var data = new NOTIFYICONDATA();
            data.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA));
            data.hWnd = _host.Handle;
            data.uID = 1;
            data.uCallbackMessage = TrayCallbackMessage;
            data.hIcon = _icon == null ? IntPtr.Zero : _icon.Handle;
            data.szTip = "BatteryMonitor";
            data.szInfo = "";
            data.szInfoTitle = "";
            return data;
        }

        public void Update(IList<WidgetRow> rows, DisplayMode mode)
        {
            if (_disposed) return;

            UpdateDeviceItems(rows);
            SetChecks(mode);

            List<int> percents = PercentsOf(rows);
            string signature = Signature(percents);
            if (signature != _lastSignature)
            {
                _lastSignature = signature;
                Icon next = MakeIcon(percents);
                Icon old = _icon;
                _icon = next;
                if (old != null) old.Dispose();

                if (_added)
                {
                    var data = BuildData();
                    data.uFlags = NIF_ICON;
                    Shell_NotifyIcon(NIM_MODIFY, ref data);
                }
            }

            if (_added)
            {
                var tip = BuildData();
                tip.uFlags = NIF_TIP;
                tip.szTip = Truncate(BuildTip(rows));
                Shell_NotifyIcon(NIM_MODIFY, ref tip);
            }
        }

        void OnTrayMouse(int mouseMessage)
        {
            if (mouseMessage != WM_RBUTTONUP && mouseMessage != WM_LBUTTONUP) return;
            ShowMenu();
        }

        void ShowMenu()
        {
            SetForegroundWindow(_host.Handle);
            _menu.Show(Cursor.Position);
            // 菜单关掉后把焦点还给原来的窗口，否则点别处菜单不消失
            PostMessage(_host.Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);
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

        /// <summary>按设备顺序取出电量，一台设备一个电池。</summary>
        static List<int> PercentsOf(IList<WidgetRow> rows)
        {
            var list = new List<int>();
            if (rows == null) return list;
            foreach (var r in rows)
            {
                if (r == null || string.IsNullOrEmpty(r.Name)) continue;
                list.Add(r.Percent);
            }
            return list;
        }

        static string Signature(List<int> percents)
        {
            var sb = new StringBuilder();
            foreach (var p in percents)
            {
                if (sb.Length > 0) sb.Append('|');
                sb.Append(p);
            }
            return sb.ToString();
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
            return sb.ToString();
        }

        static string Truncate(string text)
        {
            if (text == null) return "";
            return text.Length > MaxTipLength ? text.Substring(0, MaxTipLength) : text;
        }

        // ---------------- 图标：每台设备一个电池，竖着排 ----------------

        /// <summary>
        /// 一台设备画一个电池（各自着色、各自填充），两台就上下摞两个。
        /// 超过三个塞不进 16×16，退回「最低电量」的单电池，明细看 tooltip。
        /// </summary>
        static Icon MakeIcon(IList<int> percents)
        {
            int count = percents == null ? 0 : percents.Count;

            using (var bmp = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    if (count == 0)
                    {
                        DrawBattery(g, -1, new RectangleF(2.5f, 7.5f, 21f, 17f), 2.4f, 4f);
                    }
                    else if (count <= 3)
                    {
                        float slot = (float)IconSize / count;
                        float stroke = count == 1 ? 2.4f : 1.7f;
                        for (int i = 0; i < count; i++)
                        {
                            float bodyHeight = Math.Min(17f, slot - 5f);
                            float top = i * slot + (slot - bodyHeight) / 2f;
                            var body = new RectangleF(2.5f, top, 21f, bodyHeight);
                            DrawBattery(g, percents[i], body, stroke, Math.Min(4f, bodyHeight / 3f));
                        }
                    }
                    else
                    {
                        // 设备太多：只显示最差的那台
                        int min = int.MaxValue;
                        foreach (var p in percents)
                            if (p >= 0 && p < min) min = p;
                        if (min == int.MaxValue) min = -1;
                        DrawBattery(g, min, new RectangleF(2.5f, 7.5f, 21f, 17f), 2.4f, 4f);
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

        static void DrawBattery(Graphics g, int percent, RectangleF body, float stroke, float radius)
        {
            Color c = WidgetRender.ColorOf(percent);

            using (var path = Rounded(body, radius))
            using (var pen = new Pen(c, stroke))
                g.DrawPath(pen, path);

            // 正极凸起，高度跟着电池走，小电池上也不糊
            float capHeight = Math.Max(3f, Math.Min(6f, body.Height * 0.35f));
            var cap = new RectangleF(body.Right + 1f, body.Y + (body.Height - capHeight) / 2f, 4f, capHeight);
            using (var path = Rounded(cap, 1.2f))
            using (var pen = new Pen(c, Math.Max(1.1f, stroke * 0.7f)))
                g.DrawPath(pen, path);

            if (percent > 0)
            {
                float inset = stroke + 0.8f;
                float innerWidth = body.Width - 2f * inset - 0.5f;
                float fillWidth = innerWidth * Math.Min(percent, 100) / 100f;
                if (fillWidth < 1.6f) fillWidth = 1.6f;
                var inner = new RectangleF(body.X + inset, body.Y + inset,
                                           fillWidth, body.Height - 2f * inset);
                using (var fill = Rounded(inner, Math.Min(2f, radius - 1f)))
                using (var brush = new SolidBrush(c))
                    g.FillPath(brush, fill);
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

            if (_added)
            {
                try
                {
                    var data = BuildData();
                    Shell_NotifyIcon(NIM_DELETE, ref data);
                }
                catch { }
                _added = false;
            }

            try { _host.Dispose(); } catch { }
            _menu.Dispose();
            if (_icon != null) { _icon.Dispose(); _icon = null; }
        }
    }
}
