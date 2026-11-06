using System;
using System.Collections.Generic;

namespace BatteryMonitor
{
    /// <summary>
    /// 当前系统里"确实存在"的设备接口。
    ///
    /// 界面上只显示这些设备：接收器拔掉后那一行立即消失，没插的设备不再占一行 "--"。
    /// 判据是 HID 接口路径（接收器一直在 USB 上，接口就一直在），**不是"最近有没有读到数"**——
    /// 后者会把休眠的键盘误判成设备没了（见 docs/DESIGN.md）。
    /// </summary>
    public static class HidPresence
    {
        /// <summary>重扫间隔：拔掉接收器后，界面最多这么久就会隐藏那一行。</summary>
        const int RefreshMs = 1000;

        static readonly object _lock = new object();
        static List<string> _paths = new List<string>();
        static DateTime _lastScan = DateTime.MinValue;

        /// <summary>按需刷新接口列表（带节流）。WidgetApp 每次刷新界面前调一次。</summary>
        public static void Refresh()
        {
            lock (_lock)
            {
                if ((DateTime.UtcNow - _lastScan).TotalMilliseconds < RefreshMs) return;
                _lastScan = DateTime.UtcNow;
                try
                {
                    _paths = HidDevice.EnumeratePaths();
                }
                catch
                {
                    _paths = new List<string>();
                }
            }
        }

        /// <summary>这个配置对应的接收器/设备接口现在在不在。</summary>
        public static bool IsPresent(DeviceProfile p)
        {
            if (p == null || string.IsNullOrEmpty(p.vid)) return false;

            List<string> snapshot;
            lock (_lock) { snapshot = _paths; }

            foreach (string pid in p.EffectivePids())
            {
                foreach (string path in snapshot)
                {
                    if (HidDevice.MatchesPath(path, p.vid, pid, p.iface)) return true;
                }
            }
            return false;
        }
    }
}
