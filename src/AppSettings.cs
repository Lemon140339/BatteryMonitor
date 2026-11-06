using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace BatteryMonitor
{
    /// <summary>
    /// 用户设置。目前只有一项：悬浮层的显示模式（自动 / 总是显示 / 只留托盘）。
    ///
    /// 存在 %APPDATA%\BatteryMonitor\settings.json，而不是 exe 旁边：
    /// 程序常被装在 C:\Program Files\BatteryMonitor（普通权限写不进去），
    /// 开机自启任务也是从那儿运行的。放用户目录里，提权与不提权都能读写。
    /// 读写失败一律按默认值处理——设置丢失是小事，不能让程序因此起不来。
    /// </summary>
    public static class AppSettings
    {
        /// <summary>与 settings.json 对应的数据形状。</summary>
        sealed class Data
        {
            public string mode { get; set; }
        }

        static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "BatteryMonitor");
                return Path.Combine(dir, "settings.json");
            }
        }

        /// <summary>读取显示模式；文件不存在或内容不合法时返回 Auto（保持旧行为）。</summary>
        public static DisplayMode LoadMode()
        {
            return LoadMode(FilePath);
        }

        /// <summary>读取指定文件里的显示模式（测试用，生产代码走无参重载）。</summary>
        internal static DisplayMode LoadMode(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return DisplayMode.Auto;

                var ser = new JavaScriptSerializer();
                var data = ser.Deserialize<Data>(File.ReadAllText(path, Encoding.UTF8));
                return ParseMode(data == null ? null : data.mode);
            }
            catch (Exception ex)
            {
                DebugLog.Write("读取设置失败，按默认值处理：" + ex.Message);
                return DisplayMode.Auto;
            }
        }

        /// <summary>写回显示模式；写不进去只记日志，不影响本次运行。</summary>
        public static void SaveMode(DisplayMode mode)
        {
            SaveMode(mode, FilePath);
        }

        /// <summary>写入指定文件的显示模式（测试用，生产代码走无参重载）。</summary>
        internal static void SaveMode(DisplayMode mode, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var data = new Data();
                data.mode = ModeText(mode);
                var ser = new JavaScriptSerializer();
                File.WriteAllText(path, ser.Serialize(data), Encoding.UTF8);
                DebugLog.Write("设置已保存：mode=" + data.mode);
            }
            catch (Exception ex)
            {
                DebugLog.Write("保存设置失败：" + ex.Message);
            }
        }

        /// <summary>枚举 —— 文件里的字符串：auto / always / hidden。</summary>
        static string ModeText(DisplayMode mode)
        {
            switch (mode)
            {
                case DisplayMode.AlwaysOn: return "always";
                case DisplayMode.Hidden: return "hidden";
                default: return "auto";
            }
        }

        static DisplayMode ParseMode(string text)
        {
            if (string.IsNullOrEmpty(text)) return DisplayMode.Auto;
            switch (text.Trim().ToLowerInvariant())
            {
                case "always": return DisplayMode.AlwaysOn;
                case "hidden": return DisplayMode.Hidden;
                default: return DisplayMode.Auto;
            }
        }
    }
}
