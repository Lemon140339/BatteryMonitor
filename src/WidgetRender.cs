using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;

namespace BatteryMonitor
{
    /// <summary>悬浮层上的一行：设备名 + 电量（-1 表示未知）。</summary>
    public class WidgetRow
    {
        public readonly string Name;
        public readonly int Percent;
        public readonly string Note;

        public WidgetRow(string name, int percent, string note)
        {
            Name = name;
            Percent = percent;
            Note = note;
        }

        public string ValueText
        {
            get
            {
                if (Note != null) return Note;
                return Percent >= 0 ? Percent + "%" : "--";
            }
        }
    }

    /// <summary>
    /// 悬浮层的排版与绘制。数字列按最宽的可能值（100%）定宽，
    /// 所以 100% 和 7% 不会把设备名推到不同位置。
    /// 这里不碰窗口 API，可以离屏渲染出来核对。
    /// </summary>
    public static class WidgetRender
    {
        public const int TopMargin = 6;
        public const int RowHeight = 24;
        public const int RightMargin = 8;
        public const int LeftMargin = 8;
        public const int ColumnGap = 10;
        public const int BottomMargin = 6;

        /// <summary>数字列定宽的参照文本：取可能出现的最大值。</summary>
        public const string ValueColumnSample = "100%";

        static readonly Font RowFont = new Font("Microsoft YaHei", 10.5f, FontStyle.Bold);
        static readonly Font WarnFont = new Font("Microsoft YaHei", 9f, FontStyle.Bold);
        static readonly StringFormat Typographic = StringFormat.GenericTypographic;

        static readonly SolidBrush ShadowBrush = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
        static readonly SolidBrush ValueBrush = new SolidBrush(Color.FromArgb(248, 248, 248));
        static readonly SolidBrush WarnBrush = new SolidBrush(Color.FromArgb(255, 120, 90));
        static readonly SolidBrush GreenBrush = new SolidBrush(Color.FromArgb(46, 204, 113));
        static readonly SolidBrush YellowBrush = new SolidBrush(Color.FromArgb(241, 196, 15));
        static readonly SolidBrush RedBrush = new SolidBrush(Color.FromArgb(231, 76, 60));
        static readonly SolidBrush GreyBrush = new SolidBrush(Color.Gray);

        /// <summary>排版结果：窗口尺寸与各列右边缘。</summary>
        public sealed class Layout
        {
            public int Width;
            public int Height;
            public float ValueRight;
            public float ValueWidth;
            public float NameRight;
        }

        public static Layout Measure(IList<WidgetRow> rows, string warnText)
        {
            using (var probe = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(probe))
            {
                return Compute(g, rows, warnText);
            }
        }

        public static Layout Compute(Graphics g, IList<WidgetRow> rows, string warnText)
        {
            float maxName = 0f;
            float maxValue = MeasureText(g, ValueColumnSample, RowFont).Width;

            if (rows != null)
            {
                foreach (var r in rows)
                {
                    if (r == null) continue;
                    if (!string.IsNullOrEmpty(r.Name))
                    {
                        float nw = MeasureText(g, r.Name, RowFont).Width;
                        if (nw > maxName) maxName = nw;
                    }
                    // 百分比都不会比 "100%" 宽，但备注文本（如 devices/ 为空）可能更宽，
                    // 数字列要跟着放宽，否则会压到设备名上
                    float vw = MeasureText(g, r.ValueText, RowFont).Width;
                    if (vw > maxValue) maxValue = vw;
                }
            }

            // 描第二遍时整体右移 0.45px，给数字列留一点余量
            float valueWidth = maxValue + 1f;

            float width = LeftMargin + maxName + ColumnGap + valueWidth + RightMargin;
            if (warnText != null)
            {
                float need = LeftMargin + MeasureText(g, warnText, WarnFont).Width + RightMargin;
                if (need > width) width = need;
            }
            if (width < LeftMargin + RightMargin + 24f) width = LeftMargin + RightMargin + 24f;

            int lines = (rows == null ? 0 : rows.Count) + (warnText != null ? 1 : 0);

            var layout = new Layout();
            layout.Width = (int)Math.Ceiling(width);
            layout.Height = TopMargin + lines * RowHeight + BottomMargin;
            layout.ValueWidth = valueWidth;
            layout.ValueRight = layout.Width - RightMargin;
            layout.NameRight = layout.ValueRight - valueWidth - ColumnGap;
            return layout;
        }

        public static void Draw(Graphics g, IList<WidgetRow> rows, string warnText, Layout layout)
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            float y = TopMargin;
            if (rows != null)
            {
                foreach (var r in rows)
                {
                    if (r != null) DrawRow(g, layout, r, y);
                    y += RowHeight;
                }
            }
            if (warnText != null) DrawWarn(g, layout, warnText, y);
        }

        static void DrawRow(Graphics g, Layout layout, WidgetRow row, float y)
        {
            if (string.IsNullOrEmpty(row.Name)) return;

            string value = row.ValueText;
            float nameWidth = MeasureText(g, row.Name, RowFont).Width;
            float valueWidth = MeasureText(g, value, RowFont).Width;

            // 名字与数字各自按列的右边缘对齐：小数点、百分号都落在同一竖直线上
            float xn = layout.NameRight - nameWidth;
            float xv = layout.ValueRight - valueWidth;

            g.DrawString(row.Name, RowFont, ShadowBrush, xn + 1.2f, y + 1.2f, Typographic);
            g.DrawString(value, RowFont, ShadowBrush, xv + 1.2f, y + 1.2f, Typographic);

            g.DrawString(value, RowFont, ValueBrush, xv, y, Typographic);
            g.DrawString(value, RowFont, ValueBrush, xv + 0.45f, y, Typographic);

            Brush nameBrush = BrushOf(row.Percent);
            g.DrawString(row.Name, RowFont, nameBrush, xn, y, Typographic);
            g.DrawString(row.Name, RowFont, nameBrush, xn + 0.45f, y, Typographic);
        }

        static void DrawWarn(Graphics g, Layout layout, string text, float y)
        {
            float width = MeasureText(g, text, WarnFont).Width;
            float x = layout.ValueRight - width;
            g.DrawString(text, WarnFont, ShadowBrush, x + 1.2f, y + 1.2f, Typographic);
            g.DrawString(text, WarnFont, WarnBrush, x, y, Typographic);
        }

        static SizeF MeasureText(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return SizeF.Empty;
            return g.MeasureString(text, font, new PointF(0f, 0f), Typographic);
        }

        /// <summary>电量对应的颜色；悬浮层文字和托盘图标共用这一套。</summary>
        public static Color ColorOf(int percent)
        {
            if (percent < 0) return Color.Gray;
            if (percent > 50) return Color.FromArgb(46, 204, 113);
            if (percent > 20) return Color.FromArgb(241, 196, 15);
            return Color.FromArgb(231, 76, 60);
        }

        static Brush BrushOf(int percent)
        {
            if (percent < 0) return GreyBrush;
            if (percent > 50) return GreenBrush;
            if (percent > 20) return YellowBrush;
            return RedBrush;
        }
    }
}
