using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace BatteryMonitor
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            DebugLog.Enabled = HasFlag(args, "--log");

            // 开机自启的提权助手：只改计划任务，不建窗口、不起托盘、不读设备配置。
            // 托盘菜单在普通权限下会用 runas 以这两个参数重启自己（用户会看到一次 UAC）。
            if (HasFlag(args, "--autostart-on") || HasFlag(args, "--autostart-off"))
            {
                bool enable = HasFlag(args, "--autostart-on");
                string error;
                bool ok = Autostart.SetEnabled(enable, Application.ExecutablePath, out error);
                DebugLog.Write("自启助手：" + (enable ? "开启" : "关闭") +
                    (ok ? " 成功" : " 失败 —— " + error));
                Environment.ExitCode = ok ? 0 : 1;
                return;
            }

            // 必须在创建任何窗口之前调用：下面的托盘探测会建窗口
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool admin = IsElevated();
            if (admin) DebugLog.Write("以管理员权限运行");

            // 托盘图标不一定需要提权：先真的注册一次试试，能注册就别打扰用户。
            // 注册不了（有些机器的 explorer 以更高权限运行，非提权进程进不了通知区）
            // 才提权重启自己；用户拒绝 UAC 就退回纯悬浮层，不阻塞启动。
            bool canTray = CanUseTray();
            if (!canTray && !admin && !HasFlag(args, "--no-elevate") && !HasFlag(args, "--elevated"))
            {
                if (TryRelaunchElevated(args)) return;
                DebugLog.Write("用户取消了提权，退回悬浮层");
            }
            else if (canTray)
            {
                DebugLog.Write("托盘可注册，无需提权");
            }

            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "devices");

            // 旧版本删掉/改名的配置可能还留在 devices\ 里：按清单清理（能删就删），
            // 删不掉的也直接忽略，避免升级后出现重复的行。
            List<string> ignoredFiles = ConfigManifest.ApplyObsolete(dir);

            List<string> problems;
            var profiles = ProfileLoader.Load(dir, out problems, ignoredFiles);
            ReportProblems(problems);

            var workers = new List<DeviceWorker>();
            foreach (var p in profiles) workers.Add(new DeviceWorker(p));
            foreach (var w in workers) w.Start();

            using (var app = new WidgetApp(workers, problems, HasFlag(args, "--selfcheck")))
                Application.Run(app);
        }

        // ---------------- 提权 ----------------

        /// <summary>
        /// 真往通知区注册一次再注销，判断当前权限能不能用托盘。
        /// 必须用原生 Shell_NotifyIcon 的返回值判断：WinForms 的 NotifyIcon
        /// 在注册失败时不抛异常，会把失败当成成功（我们踩过这个坑）。
        /// </summary>
        static bool CanUseTray()
        {
            var probe = new TrayView(true);
            try { return probe.Attach(); }
            catch { return false; }
            finally { probe.Dispose(); }
        }

        public static bool IsElevated()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        static bool TryRelaunchElevated(string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath, BuildArguments(args));
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                DebugLog.Write("已请求管理员权限，新进程接管");
                return true;
            }
            catch (Exception ex)
            {
                DebugLog.Write("提权失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>原样带走命令行参数，追加 --elevated 防止新进程再弹一次 UAC。</summary>
        static string BuildArguments(string[] args)
        {
            var list = new List<string>();
            if (args != null)
            {
                foreach (var a in args)
                    if (!string.Equals(a, "--elevated", StringComparison.OrdinalIgnoreCase))
                        list.Add(a);
            }
            list.Add("--elevated");

            var sb = new StringBuilder();
            foreach (var a in list)
            {
                if (sb.Length > 0) sb.Append(' ');
                if (a.IndexOf(' ') >= 0) sb.Append('"').Append(a).Append('"');
                else sb.Append(a);
            }
            return sb.ToString();
        }

        static bool HasFlag(string[] args, string flag)
        {
            if (args == null) return false;
            foreach (var a in args)
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ---------------- 配置错误 ----------------

        static void ReportProblems(List<string> problems)
        {
            if (problems == null || problems.Count == 0) return;

            try
            {
                string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config-error.log");
                var sb = new StringBuilder();
                sb.AppendLine("配置检查发现 " + problems.Count + " 个问题（" + DateTime.Now + "）");
                sb.AppendLine();
                foreach (var m in problems) sb.AppendLine("  · " + m);
                sb.AppendLine();
                sb.AppendLine("字段说明见 docs/ADD_DEVICE.md");
                File.WriteAllText(log, sb.ToString(), Encoding.UTF8);
            }
            catch { }

            foreach (var m in problems) DebugLog.Write("配置问题：" + m);
        }
    }
}
