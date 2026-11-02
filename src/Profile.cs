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
        public int battery { get; set; }         // 电量字节下标
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
        public static List<DeviceProfile> Load(string dir, out string error)
        {
            error = null;
            var list = new List<DeviceProfile>();
            if (!Directory.Exists(dir))
            {
                error = "设备目录不存在: " + dir;
                return list;
            }

            var files = Directory.GetFiles(dir, "*.json");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            var ser = new JavaScriptSerializer();
            var errors = new List<string>();

            foreach (var f in files)
            {
                try
                {
                    string text = File.ReadAllText(f, Encoding.UTF8);
                    var p = ser.Deserialize<DeviceProfile>(text);
                    if (p == null || string.IsNullOrEmpty(p.name))
                    {
                        errors.Add(Path.GetFileName(f) + ": 缺少 name");
                        continue;
                    }
                    if (string.IsNullOrEmpty(p.mode)) p.mode = "passive";
                    if (p.pollIntervalMs <= 0) p.pollIntervalMs = 30000;
                    if (p.retryDelayMs <= 0) p.retryDelayMs = 5000;
                    list.Add(p);
                }
                catch (Exception ex)
                {
                    errors.Add(Path.GetFileName(f) + ": " + ex.Message);
                }
            }
            if (errors.Count > 0) error = string.Join("; ", errors);
            return list;
        }
    }
}
