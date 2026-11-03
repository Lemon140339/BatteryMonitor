using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BatteryMonitor
{
    /// <summary>右上角透明文字层：分层窗口 + 鼠标穿透，尺寸随内容自适应。</summary>
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

        const int ScreenRightMargin = 12;
        const int ScreenTopMargin = 12;

        readonly IList<DeviceWorker> _workers;
        readonly IList<string> _problems;

        public WidgetForm(IList<DeviceWorker> workers, IList<string> problems)
        {
            _workers = workers ?? new List<DeviceWorker>();
            _problems = problems ?? new List<string>();

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(170, 30);
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
            RenderNow();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) RenderNow();
        }

        /// <summary>由 WidgetApp 每秒调用；隐藏时不画，省掉无意义的合成。</summary>
        public void RenderNow()
        {
            if (!IsHandleCreated || !Visible) return;

            var rows = WidgetApp.BuildRows(_workers);
            string warn = WidgetApp.WarnText(_problems);
            var layout = WidgetRender.Measure(rows, warn);

            // 宽度跟着内容走：长设备名不会被裁，100% 也不会把整行挤歪
            if (Size.Width != layout.Width || Size.Height != layout.Height)
                Size = new Size(layout.Width, layout.Height);

            var target = new Point(Screen.PrimaryScreen.Bounds.Right - layout.Width - ScreenRightMargin,
                                   Screen.PrimaryScreen.Bounds.Top + ScreenTopMargin);
            if (Location != target) Location = target;

            using (var bmp = new Bitmap(layout.Width, layout.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    WidgetRender.Draw(g, rows, warn, layout);
                }
                Apply(bmp);
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
