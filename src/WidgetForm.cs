using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BatteryMonitor
{
    /// <summary>右上角透明文字层，按设备数量自动排版。</summary>
    public class WidgetForm : Form
    {
        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst,
            ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        const byte AC_SRC_OVER = 0;
        const byte AC_SRC_ALPHA = 1;
        const int ULW_ALPHA = 2;

        readonly List<DeviceWorker> _workers;
        readonly string _error;
        readonly int _width = 170;
        readonly int _height;
        System.Windows.Forms.Timer _timer;

        public WidgetForm(List<DeviceWorker> workers, string error)
        {
            _workers = workers ?? new List<DeviceWorker>();
            _error = error;

            int rows = _workers.Count > 0 ? _workers.Count : 1;
            _height = 12 + rows * 24;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(_width, _height);
            Location = new Point(Screen.PrimaryScreen.Bounds.Right - _width - 12,
                                 Screen.PrimaryScreen.Bounds.Top + 12);

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 1000;
            _timer.Tick += (s, e) => Render();
            _timer.Start();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00080000;  // layered
                cp.ExStyle |= 0x00000020;  // click-through
                cp.ExStyle |= 0x08000000;  // no activate
                cp.ExStyle |= 0x00000080;  // tool window
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Render();
        }

        static Color ColorOf(int pct)
        {
            if (pct < 0) return Color.Gray;
            if (pct > 50) return Color.FromArgb(46, 204, 113);
            if (pct > 20) return Color.FromArgb(241, 196, 15);
            return Color.FromArgb(231, 76, 60);
        }

        void Render()
        {
            if (!IsHandleCreated) return;
            using (var bmp = new Bitmap(_width, _height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                    if (_workers.Count == 0)
                    {
                        DrawRow(g, 5, "无设备配置", -1, "devices/ 为空");
                    }
                    else
                    {
                        int y = 6;
                        foreach (var w in _workers)
                        {
                            DrawRow(g, y, w.Name, w.Percent, null);
                            y += 24;
                        }
                    }
                }
                Apply(bmp);
            }
        }

        void DrawRow(Graphics g, int y, string name, int pct, string note)
        {
            Color c = ColorOf(pct);
            string num = note ?? (pct >= 0 ? pct + "%" : "--");

            using (var f = new Font("Microsoft YaHei", 10.5f, FontStyle.Bold))
            {
                var nw = g.MeasureString(name, f);
                var vw = g.MeasureString(num, f);
                float xNum = _width - 8 - vw.Width;
                float xName = xNum - 8 - nw.Width;

                using (var shadow = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                {
                    g.DrawString(name, f, shadow, xName + 1.2f, y + 1.2f);
                    g.DrawString(num, f, shadow, xNum + 1.2f, y + 1.2f);
                }
                using (var bn = new SolidBrush(Color.FromArgb(248, 248, 248)))
                {
                    g.DrawString(num, f, bn, xNum, y);
                    g.DrawString(num, f, bn, xNum + 0.45f, y);
                }
                using (var bc = new SolidBrush(c))
                {
                    g.DrawString(name, f, bc, xName, y);
                    g.DrawString(name, f, bc, xName + 0.45f, y);
                }
            }
        }

        void Apply(Bitmap bmp)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBmp = IntPtr.Zero;
            IntPtr old = IntPtr.Zero;
            try
            {
                hBmp = bmp.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(memDc, hBmp);
                var size = new SIZE();
                size.cx = bmp.Width; size.cy = bmp.Height;
                var src = new POINT();
                src.X = 0; src.Y = 0;
                var dst = new POINT();
                dst.X = Left; dst.Y = Top;
                var blend = new BLENDFUNCTION();
                blend.BlendOp = AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = AC_SRC_ALPHA;
                UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                if (old != IntPtr.Zero) SelectObject(memDc, old);
                if (hBmp != IntPtr.Zero) DeleteObject(hBmp);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }
}
