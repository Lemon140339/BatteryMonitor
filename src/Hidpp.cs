using System;
using System.Collections.Generic;
using System.Threading;

namespace BatteryMonitor
{
    /// <summary>
    /// HID++ 2.0 的帧构造与解析。纯逻辑，不碰 HID API，方便单测。
    ///
    /// 帧布局：短报告 <c>10 dev feat (func&lt;&lt;4|swid) p0 p1 p2</c>（7 字节），
    /// 长报告 <c>11 dev feat (func&lt;&lt;4|swid) p0..p15</c>（20 字节）。
    /// 回复与请求逐字节相同（含 swid），所以匹配回复只比 [2]+[3]；
    /// 错误帧是 <c>[2]==0xFF</c>、<c>[3]==featureIndex</c>、错误码在 <c>[5]</c>。
    ///
    /// 电池只有三个 feature（编号已核实）：
    ///   <c>0x1000</c> Battery Level Status，func 0 → 百分比 + 充电状态；
    ///   <c>0x1004</c> Unified Battery，<b>func 1</b> → 百分比 + 档位 + 充电状态；
    ///   <c>0x1001</c> Battery Voltage（本期不做：只有 mV，要自己拟合曲线）。
    /// </summary>
    public static class HidppCodec
    {
        public const int ShortReportId = 0x10;
        public const int LongReportId = 0x11;
        public const int ShortLength = 7;
        public const int LongLength = 20;

        /// <summary>软件 ID。别用 0x0A/0x0B：Solaar 与 LGSTrayEx 各占一个，会互相认领回复。</summary>
        public const byte SwId = 0x0C;

        public const byte RootFeature = 0x00;
        public const ushort FeatureBatteryLevel = 0x1000;
        public const ushort FeatureUnifiedBattery = 0x1004;

        public const byte PingMarker = 0xAA;

        public static byte FuncByte(byte function)
        {
            return (byte)((function << 4) | SwId);
        }

        public static byte[] BuildShort(byte devIndex, byte featureIndex, byte function, byte p0, byte p1, byte p2)
        {
            return new byte[]
            {
                (byte)ShortReportId, devIndex, featureIndex, FuncByte(function), p0, p1, p2
            };
        }

        public static byte[] BuildLong(byte devIndex, byte featureIndex, byte function, byte[] payload)
        {
            var f = new byte[LongLength];
            f[0] = (byte)LongReportId;
            f[1] = devIndex;
            f[2] = featureIndex;
            f[3] = FuncByte(function);
            if (payload != null)
            {
                int n = Math.Min(payload.Length, LongLength - 4);
                for (int i = 0; i < n; i++) f[4 + i] = payload[i];
            }
            return f;
        }

        /// <summary>这个回复是不是请求的应答（错误帧不算）。</summary>
        public static bool TryMatch(byte[] request, byte[] reply)
        {
            if (request == null || reply == null || reply.Length < 7) return false;
            if (reply[1] != request[1]) return false;
            if (reply[2] == 0xFF) return false;          // 错误帧
            if (reply[2] != request[2]) return false;
            if (reply[3] != request[3]) return false;
            return true;
        }

        /// <summary>错误帧：<c>[2]==0xFF</c> 且 <c>[3]</c> 回显 featureIndex，错误码在 <c>[5]</c>。</summary>
        public static bool IsError(byte[] reply, byte featureIndex)
        {
            return reply != null && reply.Length >= 6
                && reply[2] == 0xFF && reply[3] == featureIndex;
        }

        /// <summary>
        /// 解析 0x1000 getStatus 的参数区（调用方传 reply[4] 起的切片或整帧？这里统一传整帧，内部从 [4] 起）。
        /// 参数：p[0]=百分比 p[1]=下一档 p[2]=充电状态。
        /// </summary>
        public static int ParseBatteryLevelStatus(byte[] reply, out int status)
        {
            status = 0;
            if (reply == null || reply.Length < 7) return -1;
            status = reply[6];
            int pct = reply[4];
            if (status == 3) pct = 100;      // charge complete：内核语义直接按 100% 处理
            if (pct < 0 || pct > 100) return -1;
            return pct;
        }

        /// <summary>
        /// 解析 0x1004 func1 的参数区：p[0]=百分比 p[1]=档位位域 p[2]=充电状态 p[3]=外接电源。
        /// </summary>
        public static int ParseUnifiedBattery(byte[] reply, out int status)
        {
            status = 0;
            if (reply == null || reply.Length < 7) return -1;
            status = reply[6];
            int pct = reply[4];
            if (status == 3) pct = 100;
            if (pct < 0 || pct > 100) return -1;
            return pct;
        }

        /// <summary>HID++ 1.0 设备会回 <c>[2]=0x8F</c> 的错误帧（老 Nano 接收器），本会话不支持它们。</summary>
        public static bool IsLegacyError(byte[] reply)
        {
            return reply != null && reply.Length >= 3 && reply[2] == 0x8F;
        }

        /// <summary>0x1000 的充电状态枚举 → 是否在充电 / 是否已满。</summary>
        public static bool LevelStatusCharging(int status)
        {
            return status == 1 || status == 2 || status == 4;
        }

        /// <summary>0x1004 的充电状态枚举 → 是否在充电 / 是否已满。</summary>
        public static bool UnifiedCharging(int status)
        {
            return status == 1 || status == 2;
        }
    }

    /// <summary>
    /// 一次 HID++ 会话：打开短/长两个集合，ping 出应答的 device index，再读电池。
    /// 用法：<c>using (var s = HidppSession.Open(profile)) { s.TryReadBattery(...); }</c>
    /// </summary>
    public sealed class HidppSession : IDisposable
    {
        const int RequestTimeoutMs = 900;

        readonly HidDevice _short;
        readonly HidDevice _long;
        readonly int _shortInLen;
        readonly int _longInLen;
        int _deviceIndex = -1;      // -1 = 还没解析出来
        int _batteryFeatureId;      // 0 = 没查过；-1 = 没有电池 feature；否则 0x1000 / 0x1004
        int _batteryFeatureIndex;   // 对应的运行时 feature index

        HidppSession(HidDevice shortDev, HidDevice longDev)
        {
            _short = shortDev;
            _long = longDev;
            _shortInLen = shortDev.Caps.InputLength > 0 ? shortDev.Caps.InputLength : HidppCodec.ShortLength;
            _longInLen = longDev.Caps.InputLength > 0 ? longDev.Caps.InputLength : HidppCodec.LongLength;
            if (_shortInLen < HidppCodec.ShortLength) _shortInLen = HidppCodec.ShortLength;
            if (_longInLen < HidppCodec.LongLength) _longInLen = HidppCodec.LongLength;
        }

        /// <summary>找 usage page 0xFF00 上 usage=1（短）与 usage=2（长）的两个集合并打开。</summary>
        public static HidppSession Open(DeviceProfile p)
        {
            if (p == null || string.IsNullOrEmpty(p.vid)) return null;

            List<string> paths;
            try { paths = HidDevice.EnumeratePaths(); }
            catch { return null; }

            foreach (string pid in p.EffectivePids())
            {
                string shortPath = null;
                string longPath = null;
                foreach (string path in paths)
                {
                    if (!HidDevice.MatchesPath(path, p.vid, pid, p.iface)) continue;
                    HidDevice.HidCaps? caps = HidDevice.GetCaps(path);
                    if (caps == null || caps.Value.UsagePage != 0xFF00) continue;
                    if (caps.Value.Usage == 0x01 && shortPath == null) shortPath = path;
                    else if (caps.Value.Usage == 0x02 && longPath == null) longPath = path;
                }
                if (shortPath == null || longPath == null) continue;

                HidDevice s = HidDevice.OpenPath(shortPath);
                HidDevice l = HidDevice.OpenPath(longPath);
                if (s == null || l == null)
                {
                    if (s != null) s.Dispose();
                    if (l != null) l.Dispose();
                    continue;
                }
                DebugLog.Write("HID++ 集合已打开：pid=" + pid);
                return new HidppSession(s, l);
            }
            return null;
        }

        /// <summary>
        /// 读一次电量。失败返回 false（接收器不在/设备休眠/没有电池 feature 都算失败，
        /// 交给 DeviceWorker 决定保留旧值还是显示 --）。
        /// </summary>
        public bool TryReadBattery(out int percent, out bool charging, out bool full)
        {
            percent = -1;
            charging = false;
            full = false;

            // 已解析出来的设备索引先直接用；读失败就丢掉重新解析（换机/重配对后索引会变）
            if (_deviceIndex >= 0)
            {
                if (ReadAt((byte)_deviceIndex, out percent, out charging, out full)) return true;
                _deviceIndex = -1;
                _batteryFeatureId = 0;
                _batteryFeatureIndex = 0;
            }

            // 0xFF = 接收器本身或直连设备；1..6 = 接收器后面挂着的设备。
            // 接收器一般没有电池 feature，所以 0xFF 会"问得到、没电池"，自然走到 1..6。
            var indexes = new byte[] { 0xFF, 1, 2, 3, 4, 5, 6 };
            foreach (byte idx in indexes)
            {
                if (!Ping(idx)) continue;
                if (ReadAt(idx, out percent, out charging, out full))
                {
                    _deviceIndex = idx;
                    return true;
                }
            }
            return false;
        }

        bool ReadAt(byte devIndex, out int percent, out bool charging, out bool full)
        {
            percent = -1;
            charging = false;
            full = false;

            if (_batteryFeatureId == 0)
            {
                int fi = FeatureIndex(devIndex, HidppCodec.FeatureBatteryLevel);
                if (fi > 0)
                {
                    _batteryFeatureId = HidppCodec.FeatureBatteryLevel;
                    _batteryFeatureIndex = fi;
                }
                else
                {
                    fi = FeatureIndex(devIndex, HidppCodec.FeatureUnifiedBattery);
                    if (fi > 0)
                    {
                        _batteryFeatureId = HidppCodec.FeatureUnifiedBattery;
                        _batteryFeatureIndex = fi;
                    }
                    else
                    {
                        _batteryFeatureId = -1;    // 这台设备没有电池 feature
                    }
                }
            }
            if (_batteryFeatureId <= 0) return false;

            int status;
            int pct;
            if (_batteryFeatureId == HidppCodec.FeatureBatteryLevel)
            {
                byte[] r = Call(devIndex, (byte)_batteryFeatureIndex, 0x00, true, null);
                pct = HidppCodec.ParseBatteryLevelStatus(r, out status);
                if (pct < 0) return false;
                charging = HidppCodec.LevelStatusCharging(status);
            }
            else
            {
                // 0x1004 的 getStatus 是 func 1（不是 func 0，0 是 getCapabilities）
                byte[] r = Call(devIndex, (byte)_batteryFeatureIndex, 0x01, true, null);
                pct = HidppCodec.ParseUnifiedBattery(r, out status);
                if (pct < 0) return false;
                charging = HidppCodec.UnifiedCharging(status);
            }

            percent = pct;
            full = status == 3;
            return true;
        }

        /// <summary>ping：ROOT(0x0000) func 1，第三个参数放固定标记，应答 [6] 回显。</summary>
        bool Ping(byte devIndex)
        {
            byte[] r = Call(devIndex, HidppCodec.RootFeature, 0x01, false,
                new byte[] { 0x00, 0x00, HidppCodec.PingMarker });
            if (r == null || r.Length < 7) return false;
            return r[6] == HidppCodec.PingMarker;
        }

        /// <summary>把 feature ID 换成运行时 index。0 = 设备不支持。</summary>
        int FeatureIndex(byte devIndex, ushort featureId)
        {
            var payload = new byte[] { (byte)(featureId >> 8), (byte)(featureId & 0xFF) };
            byte[] r = Call(devIndex, HidppCodec.RootFeature, 0x00, true, payload);
            if (r == null || r.Length < 5) return 0;
            return r[4];
        }

        /// <summary>
        /// 发一帧并等应答。短/长两个句柄都要读：设备可能从任一个集合回（Windows 上是两个句柄），
        /// 期间还会收到鼠标移动之类的无关输入报告，所以按帧头匹配、匹配不上就继续读。
        /// </summary>
        byte[] Call(byte devIndex, byte featureIndex, byte function, bool longReport, byte[] payload)
        {
            HidDevice target = longReport ? _long : _short;
            byte[] req = longReport
                ? HidppCodec.BuildLong(devIndex, featureIndex, function, payload)
                : HidppCodec.BuildShort(devIndex, featureIndex, function,
                    payload != null && payload.Length > 0 ? payload[0] : (byte)0,
                    payload != null && payload.Length > 1 ? payload[1] : (byte)0,
                    payload != null && payload.Length > 2 ? payload[2] : (byte)0);

            if (!target.Write(req, 500)) return null;

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(RequestTimeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (left <= 0) break;
                int slice = left < 250 ? left : 250;

                byte[] r = ReadFrom(_short, _shortInLen, slice);
                if (r != null)
                {
                    if (HidppCodec.TryMatch(req, r)) return r;
                    if (HidppCodec.IsError(r, featureIndex) || HidppCodec.IsLegacyError(r)) return null;
                }

                left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (left <= 0) break;
                slice = left < 250 ? left : 250;

                r = ReadFrom(_long, _longInLen, slice);
                if (r != null)
                {
                    if (HidppCodec.TryMatch(req, r)) return r;
                    if (HidppCodec.IsError(r, featureIndex) || HidppCodec.IsLegacyError(r)) return null;
                }
            }
            return null;
        }

        static byte[] ReadFrom(HidDevice dev, int len, int timeoutMs)
        {
            var buf = new byte[len];
            int n = dev.Read(buf, timeoutMs);
            if (n < 7) return null;
            return buf;
        }

        public void Dispose()
        {
            try { if (_short != null) _short.Dispose(); } catch { }
            try { if (_long != null) _long.Dispose(); } catch { }
        }
    }
}
