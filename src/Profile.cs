using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace BatteryMonitor
{
    /// <summary>校验算法描述</summary>
    public class CheckSpec
    {
        public string type { get; set; }   // sum8 | sumTo | none
        public int[] range { get; set; }   // [from, to] 闭区间
        public int at { get; set; }        // 校验和所在下标（写入时用；sumTo 校验可省略）
        public string target { get; set; } // sumTo：整段字节和的模 256 目标，如 "0x55"
    }

    /// <summary>
    /// 同一族设备的"另一种方言"：有些 ODM 固件对同一 VID 有不止一套帧格式，
    /// 一套问不出来就换另一套。每个变体带自己的固定字节、校验和与应答特征。
    /// </summary>
    public class QueryVariant
    {
        public Dictionary<string, string> set { get; set; }
        public CheckSpec checksum { get; set; }
        public Dictionary<string, string> expect { get; set; }
        public CheckSpec verify { get; set; }
    }

    /// <summary>主动查询帧描述</summary>
    public class QuerySpec
    {
        public int length { get; set; }
        public Dictionary<string, string> set { get; set; }  // 下标 -> 值
        public CheckSpec checksum { get; set; }
        /// <summary>多方言候选帧；有它时按顺序逐个试，set/checksum 可以留在变体里。</summary>
        public List<QueryVariant> variants { get; set; }
    }

    /// <summary>回复帧描述</summary>
    public class ResponseSpec
    {
        public int length { get; set; }
        public CheckSpec checksum { get; set; }
        /// <summary>应答特征：下标 -> 期望值。全部命中才认这一帧（回显校验）。</summary>
        public Dictionary<string, string> expect { get; set; }
    }

    /// <summary>充电/有线标志的描述：某个字节非 0（或在 values 里）即认为在充电。</summary>
    public class ChargingSpec
    {
        public int offset { get; set; }
        /// <summary>true = 非 0 表示充电；不写按 true 处理。</summary>
        public bool? nonZero { get; set; }
        /// <summary>只认这些取值才算充电（例如 MCHOSE 的 0x01）。留空则按 nonZero 判断。</summary>
        public List<string> values { get; set; }
    }

    /// <summary>一个型号的显示信息：报文里认出来的型号 ID → 名字 / 电量倍率。</summary>
    public class ModelInfo
    {
        public string name { get; set; }
        /// <summary>电量倍率，默认 1；按 10% 档上报的型号填 10（例如 8 表示 80%）。</summary>
        public int scale { get; set; }
    }

    /// <summary>一个设备的完整描述，来自 devices/*.json</summary>
    public class DeviceProfile
    {
        public string name { get; set; }
        public string vid { get; set; }
        public string pid { get; set; }          // 单个 PID（旧写法）
        public List<string> pids { get; set; }   // 多个 PID：一个接收器家族共用一份配置
        public string iface { get; set; }        // 接口路径片段，如 col03 / mi_03
        /// <summary>
        /// 按 HID usage 选接口，写法 "FF02/0002"（usage 页 / usage，十六进制）。
        /// 比 iface 稳：同一接收器的多个集合在 Windows 上的 colNN 会随固件变。
        /// </summary>
        public string usage { get; set; }
        /// <summary>
        /// 协议判别式。留空 = 传统的"字节表"配置（passive / query）；
        /// "hidpp2" = 罗技 HID++ 2.0，电量逻辑走 src/Hidpp.cs，JSON 里只留身份信息。
        /// </summary>
        public string protocol { get; set; }
        /// <summary>query 模式的写入通道：output（WriteFile，默认）| feature（HidD_SetFeature）。</summary>
        public string io { get; set; }
        /// <summary>电量倍率：原始值 × scale。默认 1（按 5% 档上报的写 5）。</summary>
        public int scale { get; set; }
        /// <summary>有效电量下限：低于它当作无效包（有些协议拿 0 表示"休眠/链路断开"）。</summary>
        public int minPercent { get; set; }
        /// <summary>单次查询的重试次数。休眠设备常常第一个包不应答，默认 2。</summary>
        public int retries { get; set; }
        /// <summary>Feature 报告写入后等多久再读（毫秒）。</summary>
        public int wakeDelayMs { get; set; }
        /// <summary>整包异或的掩码，如 "0xFF"。只对 query 模式生效。</summary>
        public string payloadXor { get; set; }
        public ChargingSpec charging { get; set; }
        public string mode { get; set; }         // passive | query
        public int readLength { get; set; }      // passive 读取长度
        public Dictionary<string, string> match { get; set; }  // passive 帧特征
        public int? battery { get; set; }        // 电量字节下标（0 合法，用可空类型区分"没填"）
        public int? modelIndex { get; set; }     // 报文里"型号 ID"的字节下标（被动模式，可选）
        public Dictionary<string, ModelInfo> models { get; set; }  // 型号 ID -> 名字 / 倍率（可选）
        public QuerySpec query { get; set; }
        public ResponseSpec response { get; set; }
        public int pollIntervalMs { get; set; }  // query 模式轮询间隔
        public int retryDelayMs { get; set; }    // 失败重试间隔

        /// <summary>实际要尝试的 PID 列表：优先 pids，没写就退回单个 pid。</summary>
        public List<string> EffectivePids()
        {
            var list = new List<string>();
            if (pids != null)
            {
                foreach (string p in pids)
                    if (!string.IsNullOrEmpty(p)) list.Add(p.Trim());
            }
            if (list.Count == 0 && !string.IsNullOrEmpty(pid)) list.Add(pid.Trim());
            return list;
        }

        /// <summary>罗技 HID++ 协议（不走字节表，电量由代码里的 HID++ 会话读）。</summary>
        public bool IsHidpp2
        {
            get { return string.Equals(protocol, "hidpp2", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsFeatureIo
        {
            get { return string.Equals(io, "feature", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>整包异或掩码；没配置返回 -1。</summary>
        public int XorMask
        {
            get
            {
                int v = ParseInt(payloadXor, -1);
                if (v < 0 || v > 255) return -1;
                return v;
            }
        }

        /// <summary>解析 usage 字段（"FF02/0002"）。解析失败返回 false，调用方退回 iface 匹配。</summary>
        public bool TryGetUsage(out ushort page, out ushort usage)
        {
            page = 0; usage = 0;
            string s = (this.usage == null ? "" : this.usage.Trim());
            int slash = s.IndexOf('/');
            if (slash <= 0 || slash >= s.Length - 1) return false;
            int p = ParseIntOrHex(s.Substring(0, slash), -1);
            int u = ParseIntOrHex(s.Substring(slash + 1), -1);
            if (p < 0 || p > 0xFFFF || u < 0 || u > 0xFFFF) return false;
            page = (ushort)p;
            usage = (ushort)u;
            return true;
        }

        /// <summary>先按 0x / 十进制解析，失败再按裸十六进制解析（usage 里习惯写 FF02/0002）。</summary>
        public static int ParseIntOrHex(string s, int fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            int v = ParseInt(s, -999);
            if (v != -999) return v;
            try
            {
                return int.Parse(s.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            catch { return fallback; }
        }

        /// <summary>按配置判断这个标志字节是不是"在充电"。</summary>
        public bool IsChargingByte(byte b)
        {
            ChargingSpec c = charging;
            if (c == null) return false;
            if (c.values != null && c.values.Count > 0)
            {
                foreach (string s in c.values)
                {
                    int v = ParseInt(s, -1);
                    if (v == b) return true;
                }
                return false;
            }
            return c.nonZero.HasValue ? (c.nonZero.Value ? b != 0 : b == 0) : b != 0;
        }

        /// <summary>按报文里的型号 ID 找显示信息；找不到返回 null（调用方保持通用名）。</summary>
        public ModelInfo FindModel(int modelId)
        {
            if (models == null) return null;
            foreach (var kv in models)
            {
                if (ParseInt(kv.Key, -1) == modelId) return kv.Value;
            }
            return null;
        }

        public string Id
        {
            get
            {
                var list = EffectivePids();
                string p = list.Count > 0 ? list[0] : pid;
                return (vid + "_" + p + "_" + mode + "_" + name).ToLowerInvariant();
            }
        }

        public static int ParseInt(string s, int fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            s = s.Trim();
            try
            {
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    return int.Parse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return int.Parse(s, CultureInfo.InvariantCulture);
            }
            catch { return fallback; }
        }
    }

    public static class ProfileLoader
    {
        public static List<DeviceProfile> Load(string dir, out List<string> problems)
        {
            return Load(dir, out problems, null);
        }

        /// <summary>
        /// 加载 devices 目录下的设备配置。
        /// </summary>
        /// <param name="ignoreFiles">
        /// 要跳过的文件名（大小写不敏感）。旧版本废弃的配置由
        /// <see cref="ConfigManifest"/> 交过来，避免它们继续生成重复的行。
        /// </param>
        public static List<DeviceProfile> Load(string dir, out List<string> problems, IList<string> ignoreFiles)
        {
            problems = new List<string>();
            var list = new List<DeviceProfile>();
            if (!Directory.Exists(dir))
            {
                problems.Add("设备目录不存在：" + dir);
                return list;
            }

            var ignore = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (ignoreFiles != null)
            {
                foreach (string name in ignoreFiles)
                {
                    if (!string.IsNullOrEmpty(name)) ignore[name.Trim()] = name;
                }
            }

            var files = Directory.GetFiles(dir, "*.json");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            var ser = new JavaScriptSerializer();

            foreach (var f in files)
            {
                string file = Path.GetFileName(f);
                if (file.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(file, ConfigManifest.FileName, StringComparison.OrdinalIgnoreCase)) continue;
                if (ignore.ContainsKey(file)) continue;
                try
                {
                    string text = File.ReadAllText(f, Encoding.UTF8);
                    var p = ser.Deserialize<DeviceProfile>(text);
                    if (p == null)
                    {
                        problems.Add(file + "：内容为空");
                        continue;
                    }
                    Validate(p, file, problems);
                    list.Add(p);
                }
                catch (Exception ex)
                {
                    problems.Add(file + "：JSON 解析失败 — " + ex.Message);
                }
            }

            // 重名检查
            var seen = new Dictionary<string, string>();
            foreach (var p in list)
            {
                if (string.IsNullOrEmpty(p.name)) continue;
                string k = p.name.ToLowerInvariant();
                if (seen.ContainsKey(k))
                    problems.Add(p.name + "：与 " + seen[k] + " 重名，屏幕上会出现两行一样的名字");
                else seen[k] = p.name;
            }
            return list;
        }

        static bool IsHex4(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length != 4) return false;
            foreach (char c in s)
                if (!Uri.IsHexDigit(c)) return false;
            return true;
        }

        static void Validate(DeviceProfile p, string file, List<string> problems)
        {
            string tag = file + "：";

            if (string.IsNullOrEmpty(p.name))
                problems.Add(tag + "缺少 name（显示在屏幕上的名字）");
            if (!IsHex4(p.vid))
                problems.Add(tag + "vid 必须是 4 位十六进制，例如 \"1D57\"（当前：" + p.vid + "）");
            if (string.IsNullOrEmpty(p.pid) && (p.pids == null || p.pids.Count == 0))
                problems.Add(tag + "缺少 pid（或 pids）：例如 \"FA65\"");
            if (!string.IsNullOrEmpty(p.pid) && !IsHex4(p.pid))
                problems.Add(tag + "pid 必须是 4 位十六进制，例如 \"FA65\"（当前：" + p.pid + "）");
            if (p.pids != null)
            {
                foreach (string x in p.pids)
                {
                    if (!IsHex4(x))
                        problems.Add(tag + "pids 里有一项不是 4 位十六进制：\"" + x + "\"");
                }
            }

            if (p.IsHidpp2)
            {
                // HID++ 走代码里的会话（src/Hidpp.cs），不需要 battery / query / readLength。
                // models 在这里是 WPID → 显示名，键是 1~4 位十六进制。
                if (p.models != null)
                {
                    foreach (var kv in p.models)
                    {
                        int id = DeviceProfile.ParseInt(kv.Key, -1);
                        if (id < 0)
                            problems.Add(tag + "models 的键 \"" + kv.Key + "\" 不是十六进制 WPID");
                        if (kv.Value == null || string.IsNullOrEmpty(kv.Value.name))
                            problems.Add(tag + "models[\"" + kv.Key + "\"] 缺少 name");
                    }
                }
                if (p.battery.HasValue)
                    problems.Add(tag + "hidpp2 协议不需要 battery（电量由 HID++ 查询得到）");
                if (p.query != null || p.response != null)
                    problems.Add(tag + "hidpp2 协议不需要 query/response（报文由代码生成）");
            }
            else
            {
                if (string.IsNullOrEmpty(p.mode)) p.mode = "passive";
                bool passive2 = string.Equals(p.mode, "passive", StringComparison.OrdinalIgnoreCase);
                bool query2 = string.Equals(p.mode, "query", StringComparison.OrdinalIgnoreCase);
                if (!passive2 && !query2)
                    problems.Add(tag + "mode 只能是 \"passive\" 或 \"query\"（当前：" + p.mode + "）");

                if (!p.battery.HasValue)
                    problems.Add(tag + "缺少 battery：请填电量所在的字节下标（从 0 开始，第一个字节写 0）");
                else if (p.battery.Value < 0)
                    problems.Add(tag + "battery 不能为负数：下标从 0 开始，第一个字节写 0（当前：" + p.battery.Value + "）");

                if (passive2)
                {
                    if (p.readLength <= 0)
                        problems.Add(tag + "被动模式必须设置 readLength（每次读取的字节数）");
                    if (p.battery.HasValue && p.readLength > 0 && p.battery.Value >= p.readLength)
                        problems.Add(tag + "battery(" + p.battery.Value + ") 超出了 readLength(" + p.readLength + ") 的范围");
                    if (p.match != null)
                    {
                        foreach (var kv in p.match)
                        {
                            int idx;
                            if (!int.TryParse(kv.Key, out idx))
                                problems.Add(tag + "match 的下标 \"" + kv.Key + "\" 不是数字");
                            int v = DeviceProfile.ParseInt(kv.Value, -999);
                            if (v == -999)
                                problems.Add(tag + "match[\"" + kv.Key + "\"] 的值 \"" + kv.Value + "\" 无法解析，写 0x40 这种十六进制");
                            else if (v < 0 || v > 255)
                                problems.Add(tag + "match[\"" + kv.Key + "\"] 的值超出 0-255");
                        }
                    }
                }

                // 型号识别：报文里某个字节是型号 ID，用它决定显示名和电量倍率
                if (p.modelIndex.HasValue)
                {
                    if (query2)
                        problems.Add(tag + "modelIndex 只适用于 passive 模式");
                    if (p.models == null || p.models.Count == 0)
                        problems.Add(tag + "填了 modelIndex 就必须提供 models（型号 ID → 名字）");
                    else
                    {
                        foreach (var kv in p.models)
                        {
                            int id = DeviceProfile.ParseInt(kv.Key, -1);
                            if (id < 0 || id > 255)
                                problems.Add(tag + "models 的下标 \"" + kv.Key + "\" 不是 0-255（写 0x93 这种十六进制）");
                            if (kv.Value == null)
                            {
                                problems.Add(tag + "models[\"" + kv.Key + "\"] 是空的");
                                continue;
                            }
                            if (string.IsNullOrEmpty(kv.Value.name))
                                problems.Add(tag + "models[\"" + kv.Key + "\"] 缺少 name");
                            if (kv.Value.scale <= 0) kv.Value.scale = 1;
                        }
                    }
                    if (passive2 && p.readLength > 0 && (p.modelIndex.Value < 0 || p.modelIndex.Value >= p.readLength))
                        problems.Add(tag + "modelIndex(" + p.modelIndex.Value + ") 超出了 readLength(" + p.readLength + ") 的范围");
                }
                else if (p.models != null && p.models.Count > 0)
                    problems.Add(tag + "写了 models 但没写 modelIndex（型号 ID 在报文里的下标）");

                if (query2)
                {
                    if (p.query == null || p.query.length <= 0)
                        problems.Add(tag + "查询模式必须提供 query.length（发送帧总长度）");
                    bool hasVariants = p.query != null && p.query.variants != null && p.query.variants.Count > 0;
                    if (p.query != null && !hasVariants && p.query.set == null && p.query.checksum == null)
                        problems.Add(tag + "query 里既没有 set 也没有 checksum，等于发一帧全 0，多半不对");
                    if (p.response == null || p.response.length <= 0)
                        problems.Add(tag + "查询模式必须提供 response.length（期望回复长度）");
                    else if (p.battery.HasValue && p.battery.Value >= p.response.length)
                        problems.Add(tag + "battery(" + p.battery.Value + ") 超出了 response.length(" + p.response.length + ") 的范围");

                    ValidateCheck(tag + "query.checksum：", p.query == null ? null : p.query.checksum, true, problems);
                    ValidateCheck(tag + "response.checksum：", p.response == null ? null : p.response.checksum, false, problems);

                    if (p.response != null && p.response.expect != null)
                    {
                        foreach (var kv in p.response.expect)
                        {
                            int idx;
                            if (!int.TryParse(kv.Key, out idx) || idx < 0)
                                problems.Add(tag + "response.expect 的下标 \"" + kv.Key + "\" 不是数字");
                            int v = DeviceProfile.ParseInt(kv.Value, -999);
                            if (v == -999 || v < 0 || v > 255)
                                problems.Add(tag + "response.expect[\"" + kv.Key + "\"] 的值无法解析（写 0x11 这种十六进制）");
                        }
                    }

                    if (p.query != null && p.query.variants != null)
                    {
                        for (int i = 0; i < p.query.variants.Count; i++)
                        {
                            QueryVariant v = p.query.variants[i];
                            string vtag = tag + "query.variants[" + i + "]：";
                            if (v == null)
                            {
                                problems.Add(vtag + "是空的");
                                continue;
                            }
                            ValidateCheck(vtag + "checksum：", v.checksum, true, problems);
                            ValidateCheck(vtag + "verify：", v.verify, false, problems);
                            if (v.expect != null)
                            {
                                foreach (var kv in v.expect)
                                {
                                    int idx2;
                                    if (!int.TryParse(kv.Key, out idx2) || idx2 < 0)
                                        problems.Add(vtag + "expect 的下标 \"" + kv.Key + "\" 不是数字");
                                    int val2 = DeviceProfile.ParseInt(kv.Value, -999);
                                    if (val2 == -999 || val2 < 0 || val2 > 255)
                                        problems.Add(vtag + "expect[\"" + kv.Key + "\"] 的值无法解析");
                                }
                            }
                        }
                    }

                    if (p.charging != null && p.response != null && p.response.length > 0
                        && (p.charging.offset < 0 || p.charging.offset >= p.response.length))
                        problems.Add(tag + "charging.offset(" + p.charging.offset + ") 超出了 response.length(" + p.response.length + ") 的范围");

                }
                else if (p.charging != null && p.readLength > 0
                    && (p.charging.offset < 0 || p.charging.offset >= p.readLength))
                    problems.Add(tag + "charging.offset(" + p.charging.offset + ") 超出了 readLength(" + p.readLength + ") 的范围");

                if (p.charging != null && p.charging.values != null)
                {
                    foreach (string v in p.charging.values)
                    {
                        int x = DeviceProfile.ParseInt(v, -1);
                        if (x < 0 || x > 255)
                            problems.Add(tag + "charging.values 里的 \"" + v + "\" 不是 0-255");
                    }
                }
            }

            if (!string.IsNullOrEmpty(p.io) && !p.IsFeatureIo
                && !string.Equals(p.io, "output", StringComparison.OrdinalIgnoreCase))
                problems.Add(tag + "io 只能是 \"output\" 或 \"feature\"（当前：" + p.io + "）");
            ushort up, uu;
            if (!string.IsNullOrEmpty(p.usage) && !p.TryGetUsage(out up, out uu))
                problems.Add(tag + "usage 写法是 \"FF02/0002\"（usage 页 / usage，十六进制）");
            if (p.scale <= 0) p.scale = 1;
            if (p.minPercent < 0) p.minPercent = 0;
            if (p.minPercent > 100) p.minPercent = 100;
            if (p.retries <= 0) p.retries = 2;
            if (p.wakeDelayMs <= 0) p.wakeDelayMs = 120;
            if (!string.IsNullOrEmpty(p.payloadXor) && p.XorMask < 0)
                problems.Add(tag + "payloadXor 只能是 \"0xFF\" 这种单字节十六进制");

            if (p.pollIntervalMs <= 0) p.pollIntervalMs = 30000;
            if (p.retryDelayMs <= 0) p.retryDelayMs = 5000;
        }

        static void ValidateCheck(string tag, CheckSpec c, bool forWrite, List<string> problems)
        {
            if (c == null) return;
            string t = c.type == null ? "" : c.type.Trim().ToLowerInvariant();
            if (t != "sum8" && t != "sumto" && t != "none")
            {
                problems.Add(tag + "type 只能是 \"sum8\" / \"sumTo\" / \"none\"（当前：" + c.type + "）");
                return;
            }
            if (t == "none") return;
            if (forWrite || t == "sum8")
            {
                if (c.at < 0) problems.Add(tag + "缺少 at（校验和写到第几个字节）");
            }
            if (t == "sumto")
            {
                int target = DeviceProfile.ParseInt(c.target, -999);
                if (target < 0 || target > 255)
                    problems.Add(tag + "sumTo 需要 target（例如 \"0x55\"）");
            }
            if (c.range != null && c.range.Length >= 2 && c.range[0] > c.range[1])
                problems.Add(tag + "range 的起始下标比结束下标大");
        }
    }
}
