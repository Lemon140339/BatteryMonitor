using System;
using System.IO;

namespace BatteryMonitor
{
    /// <summary>--log 参数开启后在 exe 同目录写 battery.log，便于排查新设备。</summary>
    public static class DebugLog
    {
        public static bool Enabled;
        static readonly object _lock = new object();

        public static void Write(string msg)
        {
            if (!Enabled) return;
            try
            {
                lock (_lock)
                {
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "battery.log");
                    File.AppendAllText(path,
                        DateTime.Now.ToString("HH:mm:ss") + "  " + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
