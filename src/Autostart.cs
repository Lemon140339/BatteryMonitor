using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace BatteryMonitor
{
    /// <summary>
    /// 开机自启 = 一个"登录时触发、最高权限"的计划任务（任务名 BatteryMonitor）。
    ///
    /// 以前靠 install-autostart.bat / uninstall-autostart.bat 两个外置脚本，
    /// 现在收进托盘菜单（右键 → 开机自启）。原因是不装脚本也能开关，且老用户已经用脚本
    /// 注册过同名任务时，菜单能直接认出来。
    ///
    /// 权限：注册任务需要管理员。当前进程是普通权限时，调用方会用 runas 重启自己一次
    /// （隐藏参数 --autostart-on / --autostart-off），所以点菜单可能弹一次 UAC——
    /// 和以前右键"以管理员身份运行"那个 bat 是同一回事。
    /// </summary>
    public static class Autostart
    {
        public const string TaskName = "BatteryMonitor";

        /// <summary>任务存在就算"已开启"（不要求当前进程是提权状态）。</summary>
        public static bool IsEnabled()
        {
            string output;
            int code = Run("schtasks.exe", "/Query /TN \"" + TaskName + "\"", out output);
            return code == 0;
        }

        /// <summary>
        /// 注册（enable=true）或删除（false）计划任务。**调用方必须保证当前进程已是管理员**，
        /// 普通权限下会失败——由 WidgetApp 决定是否先用 runas 起助手进程。
        /// </summary>
        public static bool SetEnabled(bool enable, string exePath, out string error)
        {
            error = "";
            if (enable)
            {
                if (string.IsNullOrEmpty(exePath))
                {
                    error = "拿不到当前 exe 路径";
                    return false;
                }

                string script = BuildInstallScript(exePath);
                string output;
                int code = Run("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -Command \"" + script + "\"", out output);
                if (code != 0)
                {
                    error = "注册计划任务失败（exit=" + code + "）" + Trim(output);
                    return false;
                }
                // 注册成功不等于真的生效：回读一次，避免假装成功
                if (!IsEnabled())
                {
                    error = "计划任务没有出现在系统里";
                    return false;
                }
                return true;
            }

            string ignored;
            Run("schtasks.exe", "/End /TN \"" + TaskName + "\"", out ignored);   // 先停掉正在跑的实例，失败无所谓
            int del = Run("schtasks.exe", "/Delete /TN \"" + TaskName + "\" /F", out ignored);
            if (del != 0 && IsEnabled())
            {
                error = "删除计划任务失败（exit=" + del + "）" + Trim(ignored);
                return false;
            }
            return true;
        }

        /// <summary>
        /// 注册任务用的 PowerShell 脚本。任务定义和原来的 install-autostart.bat 完全一致：
        /// 登录时触发、交互令牌、最高权限、不因电池停止、无执行时限。
        /// 放在这里是为了能单测（断言任务名/权限/触发器都在，且不含双引号以便塞进 -Command）。
        /// </summary>
        public static string BuildInstallScript(string exePath)
        {
            // PowerShell 单引号字符串里，单引号本身要写两遍
            string quoted = (exePath ?? "").Replace("'", "''");

            var sb = new StringBuilder();
            sb.Append("$exe='").Append(quoted).Append("'; ");
            sb.Append("$a=New-ScheduledTaskAction -Execute $exe; ");
            sb.Append("$t=New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME; ");
            sb.Append("$p=New-ScheduledTaskPrincipal -UserId ($env:USERDOMAIN+'\\'+$env:USERNAME) ");
            sb.Append("-LogonType Interactive -RunLevel Highest; ");
            sb.Append("$s=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries ");
            sb.Append("-DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero); ");
            sb.Append("Register-ScheduledTask -TaskName '").Append(TaskName)
              .Append("' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null");
            return sb.ToString();
        }

        static int Run(string file, string args, out string output)
        {
            output = "";
            try
            {
                var psi = new ProcessStartInfo(file, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                using (var p = Process.Start(psi))
                {
                    if (p == null) return -1;
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(20000))
                    {
                        try { p.Kill(); } catch { }
                        output = "超时";
                        return -1;
                    }
                    output = stdout + stderr;
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return -1;
            }
        }

        static string Trim(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim();
            if (s.Length > 200) s = s.Substring(0, 200) + "…";
            return "：" + s.Replace("\r", " ").Replace("\n", " ");
        }
    }
}
