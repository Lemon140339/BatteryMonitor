using System;
using System.Collections.Generic;

namespace BatteryMonitor
{
    /// <summary>
    /// 配置驱动协议的纯逻辑：组帧、校验和、整包异或解码、从应答里取电量。
    /// 刻意不碰任何 HID API，这样能直接在 tests/ProfileModelTest.cs 里用字节流回放验证
    /// （真机验证不了的协议，至少把字节级逻辑钉死）。
    /// </summary>
    public static class ProtocolCodec
    {
        /// <summary>一帧待发报文 + 它对应的应答特征。</summary>
        public sealed class PacketPlan
        {
            public byte[] Frame;
            public Dictionary<string, string> Expect;
            public CheckSpec Verify;
        }

        /// <summary>
        /// 按配置组出全部候选帧。没有 variants 时只有一帧；
        /// 有 variants 时按配置顺序返回（同一 ODM 的多种方言）。
        /// </summary>
        public static List<PacketPlan> BuildQueries(DeviceProfile p)
        {
            var plans = new List<PacketPlan>();
            QuerySpec q = p == null ? null : p.query;
            int len = q != null && q.length > 0 ? q.length : 32;
            int mask = p == null ? -1 : p.XorMask;
            Dictionary<string, string> envelopeExpect = p == null || p.response == null ? null : p.response.expect;
            CheckSpec envelopeVerify = p == null || p.response == null ? null : p.response.checksum;

            if (q != null && q.variants != null && q.variants.Count > 0)
            {
                foreach (QueryVariant v in q.variants)
                {
                    if (v == null) continue;
                    var plan = new PacketPlan();
                    plan.Frame = BuildFrame(len, v.set, v.checksum, mask);
                    plan.Expect = v.expect != null ? v.expect : envelopeExpect;
                    plan.Verify = v.verify != null ? v.verify : envelopeVerify;
                    plans.Add(plan);
                }
                return plans;
            }

            var single = new PacketPlan();
            single.Frame = BuildQuery(p);
            single.Expect = envelopeExpect;
            single.Verify = envelopeVerify;
            plans.Add(single);
            return plans;
        }

        static byte[] BuildFrame(int len, Dictionary<string, string> set, CheckSpec checksum, int mask)
        {
            if (len <= 0) len = 32;
            var f = new byte[len];
            if (set != null)
            {
                foreach (var kv in set)
                {
                    int idx = DeviceProfile.ParseInt(kv.Key, -1);
                    int val = DeviceProfile.ParseInt(kv.Value, 0);
                    if (idx >= 0 && idx < len) f[idx] = (byte)val;
                }
            }
            if (checksum != null) ApplyChecksum(f, checksum);
            if (mask >= 0) XorRange(f, 1, len - 1, mask);
            return f;
        }

        /// <summary>按配置组一帧查询报文：先填固定字节，再算校验和，最后整包异或。</summary>
        public static byte[] BuildQuery(DeviceProfile p)
        {
            QuerySpec q = p == null ? null : p.query;
            int len = q != null && q.length > 0 ? q.length : 32;
            int mask = p == null ? -1 : p.XorMask;
            return BuildFrame(len, q == null ? null : q.set, q == null ? null : q.checksum, mask);
        }

        /// <summary>
        /// 把校验和写进帧里。
        /// sum8  = 区间字节和取低 8 位；
        /// sumTo = 让"区间字节和 + 校验字节"≡ target（mod 256），即补码型校验。
        /// </summary>
        public static void ApplyChecksum(byte[] f, CheckSpec spec)
        {
            if (f == null || spec == null || spec.at < 0 || spec.at >= f.Length) return;
            string type = spec.type == null ? "" : spec.type.Trim().ToLowerInvariant();
            if (type == "none") return;

            int sum = SumRange(f, spec);
            if (type == "sumto")
            {
                int target = DeviceProfile.ParseInt(spec.target, -1);
                if (target < 0) return;
                f[spec.at] = (byte)((target - (sum & 0xFF)) & 0xFF);
            }
            else
            {
                f[spec.at] = (byte)(sum & 0xFF);
            }
        }

        /// <summary>校验应答帧。sum8 比对 at 位置；sumTo 比对各字节和的模 256 是否等于 target。</summary>
        public static bool VerifyChecksum(byte[] buf, CheckSpec spec)
        {
            if (buf == null || spec == null) return true;
            string type = spec.type == null ? "" : spec.type.Trim().ToLowerInvariant();
            if (type == "none" || type == "") return true;

            if (type == "sumto")
            {
                int target = DeviceProfile.ParseInt(spec.target, -999);
                if (target < 0) return false;
                return (SumRange(buf, spec) & 0xFF) == target;
            }

            if (spec.at < 0 || spec.at >= buf.Length) return false;
            return (byte)(SumRange(buf, spec) & 0xFF) == buf[spec.at];
        }

        /// <summary>区间求和（闭区间）。没写 range 时默认 [0, at-1]。</summary>
        public static int SumRange(byte[] buf, CheckSpec spec)
        {
            if (buf == null || spec == null) return 0;
            int from = spec.range != null && spec.range.Length > 0 ? spec.range[0] : 0;
            int to = spec.range != null && spec.range.Length > 1 ? spec.range[1] : spec.at - 1;
            if (from < 0) from = 0;
            if (to >= buf.Length) to = buf.Length - 1;
            int s = 0;
            for (int i = from; i <= to; i++) s += buf[i];
            return s;
        }

        /// <summary>把 [start, end] 区间按 mask 异或（写帧/解帧共用）。</summary>
        public static void XorRange(byte[] buf, int start, int end, int mask)
        {
            if (buf == null || mask < 0) return;
            if (start < 0) start = 0;
            if (end >= buf.Length) end = buf.Length - 1;
            for (int i = start; i <= end; i++) buf[i] = (byte)(buf[i] ^ mask);
        }

        /// <summary>应答特征校验：expect 里列出的每个"下标 → 值"都要命中。</summary>
        public static bool MatchesExpect(byte[] buf, Dictionary<string, string> expect)
        {
            if (expect == null || expect.Count == 0) return true;
            if (buf == null) return false;
            foreach (var kv in expect)
            {
                int idx = DeviceProfile.ParseInt(kv.Key, -1);
                int want = DeviceProfile.ParseInt(kv.Value, -1);
                if (idx < 0 || want < 0 || idx >= buf.Length) return false;
                if (buf[idx] != (byte)want) return false;
            }
            return true;
        }

        /// <summary>
        /// 应答的解码候选。配置了 payloadXor 时，先试"跳过报告 ID 和回显字节后整包异或"，
        /// 再退回原始帧——有些固件版本不异或（两个 MIT 来源都见到过这种差异），
        /// 所以两个都试、谁通过校验算谁，比赌一个安全。
        /// </summary>
        public static List<byte[]> ResponseCandidates(DeviceProfile p, byte[] raw)
        {
            var list = new List<byte[]>();
            if (raw == null) return list;
            int mask = p == null ? -1 : p.XorMask;
            if (mask >= 0)
            {
                var copy = new byte[raw.Length];
                Array.Copy(raw, copy, raw.Length);
                XorRange(copy, 2, copy.Length - 1, mask);
                list.Add(copy);
            }
            list.Add(raw);
            return list;
        }

        public static bool Decode(DeviceProfile p, byte[] raw, out int percent, out bool charging, out bool full)
        {
            return Decode(p, null, raw, out percent, out charging, out full);
        }

        /// <summary>
        /// 从应答里取出电量。依次试每个候选帧：expect → checksum → 电量范围，
        /// 全通过才算数。返回 false 表示这一帧不可信（休眠、链路断开、回显）。
        /// </summary>
        public static bool Decode(DeviceProfile p, PacketPlan plan, byte[] raw, out int percent, out bool charging, out bool full)
        {
            percent = -1;
            charging = false;
            full = false;
            if (p == null || raw == null) return false;

            Dictionary<string, string> expect = plan != null && plan.Expect != null
                ? plan.Expect
                : (p.response == null ? null : p.response.expect);
            CheckSpec check = plan != null && plan.Verify != null
                ? plan.Verify
                : (p.response == null ? null : p.response.checksum);
            int idx = p.battery.HasValue ? p.battery.Value : -1;
            int scale = p.scale > 0 ? p.scale : 1;
            int min = p.minPercent;

            foreach (byte[] cand in ResponseCandidates(p, raw))
            {
                if (!MatchesExpect(cand, expect)) continue;
                if (!VerifyChecksum(cand, check)) continue;
                if (idx < 0 || idx >= cand.Length) continue;

                int v = cand[idx] * scale;
                if (v < min || v > 100) continue;

                bool ch = false;
                if (p.charging != null && p.charging.offset >= 0 && p.charging.offset < cand.Length)
                    ch = p.IsChargingByte(cand[p.charging.offset]);

                percent = v;
                charging = ch;
                full = ch && v >= 100;
                return true;
            }
            return false;
        }
    }
}
