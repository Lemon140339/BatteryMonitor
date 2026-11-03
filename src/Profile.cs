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
        public string type { get; set; }   // sum8 | none
        public int[] range { get; set; }   // [from, to] 闭区间
        public int at { get; set; }        // 校验和所在下标
    }

    /// <summary>主动查询帧描述</summary>
    public class QuerySpec
    {
        public int length { get; set; }
        public Dictionary<string, string> set { get; set; }  // 下标 -> 值
        public CheckSpec checksum { get; set; }
    }

    /// <summary>回复帧描述</summary>
    public class ResponseSpec
    {
        public int length { get; set; }
        public CheckSpec checksum { get; set; }
    }

    /// <summary>一个设备的完整描述，来自 devices/*.json</summary>
    public class DeviceProfile
    {
        public string name { get; set; }
        public string vid { get; set; }
        public string pid { get; set; }
        public string iface { get; set; }        // 接口路径片段，如 col03 / mi_03
        public string mode { get; set; }         // passive | query
        public int readLength { get; set; }      // passive 读取长度
        public Dictionary<string, string> match { get; set; }  // passive 帧特征
        public int? battery { get; set; }        // 电量字节下标（0 合法，用可空类型区分"没填"）
        public QuerySpec query { get; set; }
        public ResponseSpec response { get; set; }
        public int pollIntervalMs { get; set; }  // query 模式轮询间隔
        public int retryDelayMs { get; set; }    // 失败重试间隔

        public string Id { get { return (vid + "_" + pid + "_" + mode + "_" + name).ToLowerInvariant(); } }

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
            problems = new List<string>();
            var list = new List<DeviceProfile>();
            if (!Directory.Exists(dir))
            {
                problems.Add("设备目录不存在：" + dir);
                return list;
            }

            var files = Directory.GetFiles(dir, "*.json");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            var ser = new JavaScriptSerializer();

            foreach (var f in files)
            {
                string file = Path.GetFileName(f);
                if (file.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase)) continue;
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
            if (!IsHex4(p.pid))
                problems.Add(tag + "pid 必须是 4 位十六进制，例如 \"FA65\"（当前：" + p.pid + "）");

            if (string.IsNullOrEmpty(p.mode)) p.mode = "passive";
            bool passive = string.Equals(p.mode, "passive", StringComparison.OrdinalIgnoreCase);
            bool query = string.Equals(p.mode, "query", StringComparison.OrdinalIgnoreCase);
            if (!passive && !query)
                problems.Add(tag + "mode 只能是 \"passive\" 或 \"query\"（当前：" + p.mode + "）");

            if (!p.battery.HasValue)
                problems.Add(tag + "缺少 battery：请填电量所在的字节下标（从 0 开始，第一个字节写 0）");
            else if (p.battery.Value < 0)
                problems.Add(tag + "battery 不能为负数：下标从 0 开始，第一个字节写 0（当前：" + p.battery.Value + "）");

            if (passive)
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

            if (query)
            {
                if (p.query == null || p.query.length <= 0)
                    problems.Add(tag + "查询模式必须提供 query.length（发送帧总长度）");
                if (p.query != null && p.query.set == null && p.query.checksum == null)
                    problems.Add(tag + "query 里既没有 set 也没有 checksum，等于发一帧全 0，多半不对");
                if (p.response == null || p.response.length <= 0)
                    problems.Add(tag + "查询模式必须提供 response.length（期望回复长度）");
                else if (p.battery.HasValue && p.battery.Value >= p.response.length)
                    problems.Add(tag + "battery(" + p.battery.Value + ") 超出了 response.length(" + p.response.length + ") 的范围");
            }

            if (p.pollIntervalMs <= 0) p.pollIntervalMs = 30000;
            if (p.retryDelayMs <= 0) p.retryDelayMs = 5000;
        }
    }
}
