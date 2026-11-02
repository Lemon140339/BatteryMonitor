using System;
using System.Threading;

namespace BatteryMonitor
{
    /// <summary>按 profile 驱动一个设备，维护当前电量。</summary>
    public class DeviceWorker
    {
        readonly DeviceProfile _p;
        readonly object _lock = new object();
        int _percent = -1;

        public DeviceProfile Profile { get { return _p; } }
        public string Name { get { return _p.name; } }

        public int Percent
        {
            get { lock (_lock) { return _percent; } }
        }

        public event Action<DeviceWorker> Updated;

        public DeviceWorker(DeviceProfile p) { _p = p; }

        void Set(int v)
        {
            bool changed;
            lock (_lock)
            {
                changed = v != _percent;
                _percent = v;
            }
            if (changed)
            {
                DebugLog.Write(_p.name + " = " + v);
                var h = Updated;
                if (h != null) { try { h(this); } catch { } }
            }
        }

        public void Start()
        {
            var t = new Thread(Loop);
            t.IsBackground = true;
            t.Start();
        }

        void Loop()
        {
            if (string.Equals(_p.mode, "query", StringComparison.OrdinalIgnoreCase))
                QueryLoop();
            else
                PassiveLoop();
        }

        // ---------------- 被动读取 ----------------
        void PassiveLoop()
        {
            HidDevice dev = null;
            try
            {
                while (true)
                {
                    if (dev == null)
                    {
                        dev = HidDevice.Open(_p.vid, _p.pid, _p.iface);
                        if (dev == null) { Set(-1); Thread.Sleep(_p.retryDelayMs); continue; }
                    }

                    int len = _p.readLength > 0 ? _p.readLength : 64;
                    var buf = new byte[len];
                    int n = dev.Read(buf, 4000);

                    if (n < 0)
                    {
                        dev.Dispose();
                        dev = null;
                        Set(-1);
                        Thread.Sleep(500);
                        continue;
                    }
                    if (n != len) continue;
                    if (!Matches(buf)) continue;

                    int v = _p.battery >= 0 && _p.battery < n ? buf[_p.battery] : -1;
                    if (v < 0 || v > 100) continue;
                    Set(v);
                }
            }
            finally
            {
                if (dev != null) dev.Dispose();
            }
        }

        bool Matches(byte[] buf)
        {
            if (_p.match == null) return true;
            foreach (var kv in _p.match)
            {
                int idx = DeviceProfile.ParseInt(kv.Key, -1);
                int want = DeviceProfile.ParseInt(kv.Value, -1);
                if (idx < 0 || idx >= buf.Length) return false;
                if (buf[idx] != (byte)want) return false;
            }
            return true;
        }

        // ---------------- 主动查询 ----------------
        void QueryLoop()
        {
            while (true)
            {
                int v = QueryOnce();
                if (v >= 0)
                {
                    Set(v);
                    Thread.Sleep(_p.pollIntervalMs);
                }
                else
                {
                    Set(-1);
                    Thread.Sleep(_p.retryDelayMs);
                }
            }
        }

        int QueryOnce()
        {
            if (_p.query == null) return -1;

            using (var dev = HidDevice.Open(_p.vid, _p.pid, _p.iface))
            {
                if (dev == null) return -1;

                var frame = BuildQuery();
                if (!dev.Write(frame, 1000)) return -1;

                int len = _p.response != null && _p.response.length > 0 ? _p.response.length : frame.Length;
                var resp = new byte[len];
                int n = dev.Read(resp, 1500);
                if (n != len) return -1;

                if (_p.response != null && _p.response.checksum != null)
                    if (!Verify(resp, _p.response.checksum)) return -1;

                int v = _p.battery >= 0 && _p.battery < n ? resp[_p.battery] : -1;
                if (v < 0 || v > 100) return -1;
                return v;
            }
        }

        byte[] BuildQuery()
        {
            var q = _p.query;
            int len = q.length > 0 ? q.length : 32;
            var f = new byte[len];
            if (q.set != null)
            {
                foreach (var kv in q.set)
                {
                    int idx = DeviceProfile.ParseInt(kv.Key, -1);
                    int val = DeviceProfile.ParseInt(kv.Value, 0);
                    if (idx >= 0 && idx < len) f[idx] = (byte)val;
                }
            }
            if (q.checksum != null && string.Equals(q.checksum.type, "sum8", StringComparison.OrdinalIgnoreCase))
                f[q.checksum.at] = Sum8(f, q.checksum);
            return f;
        }

        static byte Sum8(byte[] buf, CheckSpec spec)
        {
            int from = spec.range != null && spec.range.Length > 0 ? spec.range[0] : 0;
            int to = spec.range != null && spec.range.Length > 1 ? spec.range[1] : spec.at - 1;
            int s = 0;
            if (from < 0) from = 0;
            if (to >= buf.Length) to = buf.Length - 1;
            for (int i = from; i <= to; i++) s += buf[i];
            return (byte)(s & 0xFF);
        }

        static bool Verify(byte[] buf, CheckSpec spec)
        {
            if (spec.at < 0 || spec.at >= buf.Length) return false;
            return Sum8(buf, spec) == buf[spec.at];
        }
    }
}
