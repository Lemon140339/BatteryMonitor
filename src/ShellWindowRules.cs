using System;

namespace BatteryMonitor
{
    /// <summary>
    /// 判断一个前台窗口是不是"桌面 / 外壳窗口"。
    ///
    /// 这些窗口也会铺满整块屏幕（桌面、任务视图、Alt+Tab），
    /// 但它们不是用户想看的"全屏视频/游戏"。不排除的话，
    /// 点一下桌面、按 Win+D 都会把悬浮层叫出来（见 docs/DESIGN.md）。
    ///
    /// 规则单独放在一个文件里、不依赖窗口 API，方便直接测（tests/ShellWindowRulesTest.cs）。
    /// </summary>
    public static class ShellWindowRules
    {
        /// <summary>
        /// 已知的外壳窗口类名。大小写不敏感；只收"可能铺满屏幕"或"明显是外壳"的，
        /// 不要往里加班窗口、UWP 应用窗口这类可能是正常全屏应用的类名。
        /// </summary>
        static readonly string[] ShellClasses =
        {
            "Progman",                        // 桌面（图标层）
            "WorkerW",                        // 桌面（壁纸层；动态壁纸/某些显卡驱动下前台会是它）
            "Shell_TrayWnd",                  // 主任务栏
            "Shell_SecondaryTrayWnd",         // 副屏任务栏
            "TaskListThumbnailWnd",           // Alt+Tab 缩略图宿主
            "TaskSwitcherWnd",                // Alt+Tab 切换器
            "MultitaskingViewFrame",          // Win10 任务视图
            "XamlExplorerHostIslandWindow",   // Win11 任务视图 / 开始菜单宿主
            "ForegroundStaging"               // shell 的前台过渡窗口
        };

        /// <summary>类名是不是已知的外壳窗口。</summary>
        public static bool IsShellClass(string className)
        {
            if (string.IsNullOrEmpty(className)) return false;
            foreach (string c in ShellClasses)
            {
                if (string.Equals(c, className, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 这个前台窗口是否应该被忽略（不当作全屏应用）。
        /// </summary>
        /// <param name="hwnd">前台窗口句柄</param>
        /// <param name="shellWindow">GetShellWindow() 的返回值（桌面窗口）</param>
        /// <param name="desktopWindow">GetDesktopWindow() 的返回值</param>
        /// <param name="className">前台窗口的类名</param>
        public static bool ShouldIgnore(IntPtr hwnd, IntPtr shellWindow, IntPtr desktopWindow, string className)
        {
            if (hwnd == IntPtr.Zero) return true;
            if (shellWindow != IntPtr.Zero && hwnd == shellWindow) return true;
            if (desktopWindow != IntPtr.Zero && hwnd == desktopWindow) return true;
            return IsShellClass(className);
        }
    }
}
