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

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint flags);

        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOACTIVATE = 0x0010;
        const uint SWP_NOOWNERZORDER = 0x0200;

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] static extern IntPtr GetTopWindow(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);

        const uint GW_HWNDNEXT = 2;
        const uint GW_HWNDPREV = 3;

        const byte AC_SRC_OVER = 0;
        const byte AC_SRC_ALPHA = 1;
        const int ULW_ALPHA = 2;

        const int ScreenRightMargin = 12;
        const int ScreenTopMargin = 12;

        readonly IList<DeviceWorker> _workers;
        readonly IList<string> _problems;

        /// <summary>显示在哪块屏幕上：全屏窗口在哪块就画到哪块的右上角。</summary>
        public Rectangle TargetScreen;

        public WidgetForm(IList<DeviceWorker> workers, IList<string> problems)
        {
            _workers = workers ?? new List<DeviceWorker>();
            _problems = problems ?? new List<string>();

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(170, 30);
            TargetScreen = Screen.PrimaryScreen.Bounds;
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

        /// <summary>
        /// 把自己顶回最上层。
        /// 全屏视频播放器之类的窗口自己也是 topmost，它一激活就排在我们前面；
        /// 我们的窗口又不能激活（会抢焦点），只能靠 SetWindowPos 把 Z 序顶回去。
        /// </summary>
        public void RaiseTopmost()
        {
            if (!IsHandleCreated || !Visible) return;
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
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

            var screen = TargetScreen;
            if (screen.Width <= 0) screen = Screen.PrimaryScreen.Bounds;
            var target = new Point(screen.Right - layout.Width - ScreenRightMargin,
                                   screen.Top + ScreenTopMargin);
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

        /// <summary>
        /// 紧挨在我们上面的那个窗口（Z 序）——诊断「到底谁压在上面」。
        /// 返回 null 表示我们就在最顶层。
        /// </summary>
        public string NeighborAbove()
        {
            if (!IsHandleCreated || !Visible) return null;
            IntPtr above = GetWindow(Handle, GW_HWNDPREV);
            if (above == IntPtr.Zero) return null;
            return FullscreenWatcher.Describe(above);
        }

        /// <summary>
        /// 自己是不是被别的窗口盖住了（诊断用）：沿 Z 序从最上面往下找，
        /// 遇到第一个把自己完全罩住的可见窗口就算被挡，返回它的类名/标题。
        /// </summary>
        public bool IsCovered(out string offender)
        {
            offender = null;
            if (!IsHandleCreated || !Visible) return false;

            RECT mine;
            if (!GetWindowRect(Handle, out mine)) return false;

            IntPtr w = GetTopWindow(IntPtr.Zero);
            int guard = 0;
            while (w != IntPtr.Zero && w != Handle && guard++ < 500)
            {
                if (IsWindowVisible(w) && !IsIconic(w))
                {
                    RECT r;
                    if (GetWindowRect(w, out r)
                        && r.Left <= mine.Left && r.Top <= mine.Top
                        && r.Right >= mine.Right && r.Bottom >= mine.Bottom)
                    {
                        offender = FullscreenWatcher.Describe(w);
                        return true;
                    }
                }
                w = GetWindow(w, GW_HWNDNEXT);
            }
            return false;
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
