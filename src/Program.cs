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
            string error;
            var profiles = ProfileLoader.Load(dir, out error);

            var workers = new List<DeviceWorker>();
            foreach (var p in profiles)
            {
                var w = new DeviceWorker(p);
                workers.Add(w);
            }
            foreach (var w in workers) w.Start();

            Application.Run(new WidgetForm(workers, error));
        }
    }
}
