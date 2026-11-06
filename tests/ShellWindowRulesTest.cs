using System;
using System.Text;

namespace BatteryMonitorTests
{
    /// <summary>
    /// ShellWindowRules 的单元测试：纯逻辑，不需要开窗口。
    /// 用 test.bat 跑（编译 src\ShellWindowRules.cs + 本文件）。
    /// </summary>
    static class ShellWindowRulesTest
    {
        static int _fail;

        static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            // ---- 外壳窗口：应该忽略 ----
            string[] shellClasses =
            {
                "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
                "TaskListThumbnailWnd", "TaskSwitcherWnd", "MultitaskingViewFrame",
                "XamlExplorerHostIslandWindow", "ForegroundStaging"
            };
            foreach (string cls in shellClasses)
                Check("ignore " + cls, BatteryMonitor.ShellWindowRules.IsShellClass(cls));

            // 大小写不敏感
            Check("case-insensitive progman", BatteryMonitor.ShellWindowRules.IsShellClass("progman"));
            Check("case-insensitive WORKERW", BatteryMonitor.ShellWindowRules.IsShellClass("WORKERW"));

            // ---- 正常窗口：不能误伤 ----
            string[] normalClasses =
            {
                "Chrome_WidgetWin_1", "ApplicationFrameWindow", "MozillaWindowClass",
                "UnityWndClass", "SDL_app", "vlc", "PotPlayer64", "Qt5QWindowIcon",
                "HwndWrapper[DefaultDomain;;00000000-0000-0000-0000-000000000000]"
            };
            foreach (string cls in normalClasses)
                Check("keep " + cls, !BatteryMonitor.ShellWindowRules.IsShellClass(cls));

            // 空类名 / null 不当成外壳：宁可多显示，也不要漏掉真正的全屏
            Check("null class -> keep", !BatteryMonitor.ShellWindowRules.IsShellClass(null));
            Check("empty class -> keep", !BatteryMonitor.ShellWindowRules.IsShellClass(""));

            // ---- 句柄规则 ----
            IntPtr shell = new IntPtr(0x1111);
            IntPtr desktop = new IntPtr(0x2222);
            IntPtr app = new IntPtr(0x3333);
            Check("ignore shell window handle",
                BatteryMonitor.ShellWindowRules.ShouldIgnore(shell, shell, desktop, "Progman"));
            Check("ignore desktop handle",
                BatteryMonitor.ShellWindowRules.ShouldIgnore(desktop, shell, desktop, ""));
            Check("ignore null handle",
                BatteryMonitor.ShellWindowRules.ShouldIgnore(IntPtr.Zero, shell, desktop, "Chrome_WidgetWin_1"));
            Check("keep app window",
                !BatteryMonitor.ShellWindowRules.ShouldIgnore(app, shell, desktop, "Chrome_WidgetWin_1"));
            // 拿不到桌面句柄（0）时，不能把所有窗口都判成桌面
            Check("unknown shell handles -> keep app",
                !BatteryMonitor.ShellWindowRules.ShouldIgnore(app, IntPtr.Zero, IntPtr.Zero, "Chrome_WidgetWin_1"));

            Console.WriteLine(_fail == 0 ? "ALL PASS" : ("FAILURES=" + _fail));
            return _fail;
        }

        static void Check(string name, bool ok)
        {
            Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name);
            if (!ok) _fail++;
        }
    }
}
