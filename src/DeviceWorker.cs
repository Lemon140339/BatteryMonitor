using System;
using System.Threading;

namespace BatteryMonitor
{
    /// <summary>按 profile 驱动一个设备，维护当前电量。</summary>
    public class DeviceWorker
    {
        /// <summary>同一接收器下换型号时，要连续出现这么多帧才切显示名，避免两只设备来回跳。</summary>
        const int ModelSwitchFrames = 3;

        readonly DeviceProfile _p;
        readonly object _lock = new object();
        int _percent = -1;
        DateTime _lastOk;
        volatile string _name;
        bool _present;
        int _modelId = -1;
        int _pendingModel = -1;
        int _pendingCount;

        /// <summary>多久没有成功读到，就认为旧值已经不可信（设备休眠是常态，短时间保留旧值）。</summary>
        static readonly TimeSpan StaleLimit = TimeSpan.FromMinutes(5);

        public DeviceProfile Profile { get { return _p; } }

        /// <summary>显示名。同一接收器配对多型号时，会随报文里的型号 ID 变化。</summary>
        public string Name { get { return _name; } }

        /// <summary>接口当前在不在。不在时界面不显示这一行（由 WidgetApp 每帧刷新）。</summary>
        public bool Present
        {
            get { lock (_lock) { return _present; } }
        }

        /// <summary>电量字节下标；配置缺失时返回 -1（此时不应取数）。</summary>
        int BatteryIndex { get { return _p.battery.HasValue ? _p.battery.Value : -1; } }

        public int Percent
        {
            get { lock (_lock) { return _percent; } }
        }

        public event Action<DeviceWorker> Updated;

        public DeviceWorker(DeviceProfile p)
        {
            _p = p;
            _name = p.name;
        }

        void Set(int v)
        {
            bool changed;
            lock (_lock)
            {
                changed = v != _percent;
                _percent = v;
                if (v >= 0) _lastOk = DateTime.UtcNow;
            }
            if (changed)
            {
                DebugLog.Write(_name + " = " + v);
                var h = Updated;
                if (h != null) { try { h(this); } catch { } }
            }
        }

        /// <summary>更新"接口在不在"。变成不在时清掉电量，插回来从 -- 重新开始。</summary>
        public void SetPresent(bool present)
        {
            lock (_lock)
            {
                if (present == _present) return;
                _present = present;
                if (!present)
                {
                    _percent = -1;
                    _lastOk = DateTime.MinValue;
                }
            }
        }

        /// <summary>依次尝试配置里的所有 PID（一个接收器家族可能列出多个接收器 PID）。</summary>
        HidDevice OpenAny()
        {
            ushort page, usage;
            bool byUsage = _p.TryGetUsage(out page, out usage);
            foreach (string pid in _p.EffectivePids())
            {
                HidDevice dev = null;
                if (byUsage)
                {
                    string path = HidDevice.FindPathByUsage(_p.vid, pid, _p.iface, page, usage);
                    if (path != null) dev = HidDevice.OpenPath(path);
                }
                else
                {
                    dev = HidDevice.Open(_p.vid, pid, _p.iface);
                }
                if (dev != null) return dev;
            }
            return null;
        }

        /// <summary>
        /// 报文里读到型号 ID：第一次识别立刻生效；之后换型号要连续稳定 ModelSwitchFrames 帧才切，
        /// 避免同一接收器下两只设备抢着改名字。切型号时清掉旧电量（那是上一只设备的数值）。
        /// 返回是否发生了切换（用于写日志）。
        /// </summary>
        bool ObserveModel(int modelId, string displayName)
        {
            bool apply = false;
            lock (_lock)
            {
                if (modelId == _modelId)
                {
                    _pendingModel = -1;
                    _pendingCount = 0;
                    return false;
                }

                if (_modelId < 0)
                {
                    apply = true;   // 第一次识别：立刻生效，不用等
                }
                else if (modelId == _pendingModel)
                {
                    _pendingCount++;
                    if (_pendingCount >= ModelSwitchFrames) apply = true;
                }
                else
                {
                    _pendingModel = modelId;
                    _pendingCount = 1;
                }

                if (apply)
                {
                    _modelId = modelId;
                    _name = displayName;
                    _pendingModel = -1;
                    _pendingCount = 0;
                    _percent = -1;
                    _lastOk = DateTime.MinValue;
                }
            }
            return apply;
        }

        /// <summary>
        /// 一次读取失败。设备休眠、按键才醒是常态，这时保留上一次的数字，
        /// 免得每秒闪成 --；但超过 StaleLimit 还是没有成功读取，就把数字撤下来，
        /// 不让一个旧值一直冒充实时值。
        /// </summary>
        void MarkFailed()
        {
            bool stale;
            lock (_lock)
            {
                stale = _percent < 0 || _lastOk == DateTime.MinValue || DateTime.UtcNow - _lastOk > StaleLimit;
            }
            if (stale) Set(-1);
        }

        public void Start()
        {
            var t = new Thread(Loop);
            t.IsBackground = true;
            t.Start();
        }

        void Loop()
        {
            if (_p.IsHidpp2) { HidppLoop(); return; }
            if (string.Equals(_p.mode, "query", StringComparison.OrdinalIgnoreCase))
                QueryLoop();
            else
                PassiveLoop();
        }

        // ---------------- 罗技 HID++（多步协议，见 src/Hidpp.cs） ----------------

        HidppSession _hidpp;

        void HidppLoop()
        {
            while (true)
            {
                int v = HidppOnce();
                if (v >= 0)
                {
                    Set(v);
                    Thread.Sleep(Math.Max(5000, _p.pollIntervalMs));
                }
                else
                {
                    MarkFailed();
                    if (_hidpp != null) { _hidpp.Dispose(); _hidpp = null; }
                    Thread.Sleep(Math.Max(5000, _p.retryDelayMs));
                }
            }
        }

        int HidppOnce()
        {
            if (_hidpp == null) _hidpp = HidppSession.Open(_p);
            if (_hidpp == null) return -1;

            int pct;
            bool charging, full;
            if (!_hidpp.TryReadBattery(out pct, out charging, out full))
            {
                _hidpp.Dispose();
                _hidpp = null;
                return -1;
            }
            // 充电时 0x1000 常报 0%（未知），别把它当"电量耗尽"显示出去
            if (pct <= 0 && charging) return -1;
            return pct;
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
                        dev = OpenAny();
                        if (dev == null) { MarkFailed(); Thread.Sleep(_p.retryDelayMs); continue; }
                    }

                    int len = _p.readLength > 0 ? _p.readLength : 64;
                    var buf = new byte[len];
                    int n = dev.Read(buf, 4000);

                    if (n <= 0)
                    {
                        // 0 字节也算失败：否则这里会立刻重读，变成忙循环空转 CPU
                        dev.Dispose();
                        dev = null;
                        MarkFailed();
                        Thread.Sleep(500);
                        continue;
                    }
                    if (n != len) continue;
                    if (!Matches(buf)) continue;

                    int idx = BatteryIndex;
                    int v = idx >= 0 && idx < n ? buf[idx] : -1;
                    if (v < 0) continue;

                    // 一个接收器可能配对多种型号：报文里的型号 ID 决定显示名和电量倍率
                    if (_p.modelIndex.HasValue)
                    {
                        int mi = _p.modelIndex.Value;
                        if (mi < 0 || mi >= n) continue;
                        int modelId = buf[mi];
                        ModelInfo model = _p.FindModel(modelId);
                        bool known = model != null && !string.IsNullOrEmpty(model.name);
                        if (ObserveModel(modelId, known ? model.name : _p.name))
                        {
                            DebugLog.Write(known
                                ? "识别到型号：" + model.name + "（0x" + modelId.ToString("X2") + "）"
                                : "未知型号 0x" + modelId.ToString("X2") + "，使用通用名 " + _p.name);
                        }
                        if (known && model.scale > 0) v *= model.scale;
                    }

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
                    MarkFailed();
                    Thread.Sleep(_p.retryDelayMs);
                }
            }
        }

        int QueryOnce()
        {
            if (_p.query == null) return -1;

            HidDevice opened = OpenAny();
            if (opened == null) return -1;
            try
            {
                var plans = ProtocolCodec.BuildQueries(_p);
                int attempts = _p.retries > 0 ? _p.retries : 1;
                int len = _p.response != null && _p.response.length > 0
                    ? _p.response.length
                    : (plans.Count > 0 ? plans[0].Frame.Length : 32);

                for (int i = 0; i < attempts; i++)
                {
                    foreach (var plan in plans)
                    {
                        if (!Send(opened, plan.Frame)) { Thread.Sleep(80); continue; }

                        var resp = new byte[len];
                        int n = ReadReply(opened, resp, len);
                        if (n != len) { Thread.Sleep(80); continue; }

                        int pct;
                        bool charging, full;
                        if (!ProtocolCodec.Decode(_p, plan, resp, out pct, out charging, out full)) continue;
                        return pct;
                    }
                }
                return -1;
            }
            finally { opened.Dispose(); }
        }

        /// <summary>发一帧。Feature 通道优先 HidD_SetFeature，失败再试 output 报告。</summary>
        bool Send(HidDevice dev, byte[] frame)
        {
            if (_p.IsFeatureIo)
            {
                if (dev.SetFeature(frame)) return true;
                return dev.SetOutputReport(frame);
            }
            if (dev.Write(frame, 1000)) return true;
            return dev.SetOutputReport(frame);
        }

        /// <summary>收应答。Feature 通道要等设备把回报准备好（wakeDelayMs）。</summary>
        int ReadReply(HidDevice dev, byte[] resp, int len)
        {
            if (_p.IsFeatureIo)
            {
                if (_p.wakeDelayMs > 0) Thread.Sleep(_p.wakeDelayMs);
                return dev.GetFeature(resp) ? len : -1;
            }
            return dev.Read(resp, 1500);
        }
    }
}
