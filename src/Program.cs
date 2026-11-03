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
            List<string> problems;
            var profiles = ProfileLoader.Load(dir, out problems);
            ReportProblems(problems);

            var workers = new List<DeviceWorker>();
            foreach (var p in profiles) workers.Add(new DeviceWorker(p));
            foreach (var w in workers) w.Start();

            using (var app = new WidgetApp(workers, problems))
                Application.Run(app);
        }

        // ---------------- 提权 ----------------

        /// <summary>
        /// 真往通知区注册一次再注销，判断当前权限能不能用托盘。
        /// 比按“是不是管理员”猜可靠：同样是普通用户，有的机器能注册，有的会被 explorer 挡掉。
        /// </summary>
        static bool CanUseTray()
        {
            try
            {
                using (var probe = new NotifyIcon())
                {
                    probe.Icon = SystemIcons.Application;
                    probe.Text = "BatteryMonitor";
                    probe.Visible = true;
                    probe.Visible = false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        static bool IsElevated()
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
