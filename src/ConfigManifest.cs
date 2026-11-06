using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace BatteryMonitor
{
    /// <summary>
    /// devices/manifest.json：本版本随包发布的配置清单。
    ///
    /// 升级是"覆盖"而不是"替换"：新版本删掉或改名的配置文件会留在 devices\ 里，
    /// 变成重复/过期的配置。这里维护一份"本版本已经不用的文件"清单——启动时能删就删，
    /// 删不掉（没有管理员权限）也直接跳过不加载，用户不需要手动清理。
    /// 见 docs/DESIGN.md「升级：旧配置自动清理」。
    /// </summary>
    public static class ConfigManifest
    {
        public const string FileName = "manifest.json";

        /// <summary>与 manifest.json 对应的数据形状。</summary>
        sealed class Data
        {
            public string version { get; set; }
            public List<string> obsolete { get; set; }
        }

        public static string ManifestPath(string devicesDir)
        {
            return Path.Combine(devicesDir, FileName);
        }

        /// <summary>读取 obsolete 列表；文件缺失或内容损坏时返回空列表（绝不因此启动失败）。</summary>
        public static List<string> LoadObsolete(string devicesDir)
        {
            var list = new List<string>();
            try
            {
                string path = ManifestPath(devicesDir);
                if (!File.Exists(path)) return list;

                var ser = new JavaScriptSerializer();
                var data = ser.Deserialize<Data>(File.ReadAllText(path, Encoding.UTF8));
                if (data == null || data.obsolete == null) return list;

                foreach (string name in data.obsolete)
                {
                    if (!string.IsNullOrEmpty(name)) list.Add(name.Trim());
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write("读取配置清单失败，按「没有旧配置」处理：" + ex.Message);
            }
            return list;
        }

        /// <summary>
        /// 处理旧版遗留的配置文件：能删就顺手删掉，删不掉也不影响——返回的列表会交给加载器忽略。
        /// </summary>
        public static List<string> ApplyObsolete(string devicesDir)
        {
            var ignored = new List<string>();
            foreach (string name in LoadObsolete(devicesDir))
            {
                if (string.IsNullOrEmpty(name)) continue;

                string path = Path.Combine(devicesDir, name);
                if (File.Exists(path))
                {
                    try
                    {
                        File.Delete(path);
                        DebugLog.Write("已删除旧版配置：" + name);
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Write("旧版配置删不掉（" + ex.Message + "），本次忽略：" + name);
                    }
                }

                // 无论删除成功与否都不加载
                ignored.Add(name);
            }
            return ignored;
        }
    }
}
