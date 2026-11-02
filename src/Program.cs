using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace BatteryMonitor
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            if (args != null)
                foreach (var a in args)
                    if (string.Equals(a, "--log", StringComparison.OrdinalIgnoreCase))
                        DebugLog.Enabled = true;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "devices");
            List<string> problems;
            var profiles = ProfileLoader.Load(dir, out problems);

            if (problems.Count > 0)
            {
                try
                {
                    string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config-error.log");
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("配置检查发现 " + problems.Count + " 个问题（" + DateTime.Now + "）");
                    sb.AppendLine();
                    foreach (var m in problems) sb.AppendLine("  · " + m);
                    sb.AppendLine();
                    sb.AppendLine("字段说明见 docs/ADD_DEVICE.md");
                    File.WriteAllText(log, sb.ToString(), System.Text.Encoding.UTF8);
                }
                catch { }
            }

            var workers = new List<DeviceWorker>();
            foreach (var p in profiles)
            {
                var w = new DeviceWorker(p);
                workers.Add(w);
            }
            foreach (var w in workers) w.Start();

            Application.Run(new WidgetForm(workers, problems));
        }
    }
}
