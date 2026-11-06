using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BatteryMonitor;

namespace BatteryMonitorTests
{
    /// <summary>
    /// 配置模型 / 型号识别 / 接口路径匹配的逻辑测试。
    /// 用 test.bat 跑（编译 src\Profile.cs + src\HidDevice.cs + 本文件）。
    /// </summary>
    static class ProfileModelTest
    {
        static int _fail;

        static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            TestModelLookup();
            TestEffectivePids();
            TestPathMatching();
            TestProfileValidation();
            TestShippedConfigs();

            Console.WriteLine(_fail == 0 ? "ALL PASS" : ("FAILURES=" + _fail));
            return _fail;
        }

        static DeviceProfile SampleProfile()
        {
            var p = new DeviceProfile();
            p.name = "2.4G 无线鼠标";
            p.vid = "1D57";
            p.pid = "FA65";
            p.mode = "passive";
            p.readLength = 5;
            p.battery = 4;
            p.modelIndex = 1;
            p.models = new Dictionary<string, ModelInfo>();
            p.models["0x93"] = new ModelInfo { name = "IN10" };
            p.models["0x10"] = new ModelInfo { name = "R1", scale = 10 };
            return p;
        }

        static void TestModelLookup()
        {
            var p = SampleProfile();

            var in10 = p.FindModel(0x93);
            Check("model 0x93 -> IN10", in10 != null && in10.name == "IN10");
            var r1 = p.FindModel(0x10);
            Check("model 0x10 -> R1 scale 10", r1 != null && r1.name == "R1" && r1.scale == 10);
            Check("unknown model -> null", p.FindModel(0x55) == null);
            Check("no models -> null", new DeviceProfile().FindModel(1) == null);
        }

        static void TestEffectivePids()
        {
            var p = SampleProfile();
            Check("single pid fallback", p.EffectivePids().Count == 1 && p.EffectivePids()[0] == "FA65");

            p.pids = new List<string> { "FA60", "FA62", "FA65" };
            Check("pids override", p.EffectivePids().Count == 3 && p.EffectivePids()[0] == "FA60");
            Check("id uses first pid", p.Id.IndexOf("1d57_fa60", StringComparison.OrdinalIgnoreCase) >= 0);

            var q = new DeviceProfile();
            q.vid = "1D57";
            q.pid = " fa65 ";
            Check("pid trimmed", q.EffectivePids()[0] == "fa65");

            var empty = new DeviceProfile();
            Check("no pid -> empty", empty.EffectivePids().Count == 0);
        }

        static void TestPathMatching()
        {
            string path = @"\\?\hid#vid_1d57&pid_fa65&mi_02&col03#7&1a2b3c4d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
            Check("path match vid/pid/iface", HidDevice.MatchesPath(path, "1D57", "FA65", "col03"));
            Check("path match case-insensitive", HidDevice.MatchesPath(path, "1d57", "fa65", "COL03"));
            Check("path mismatch pid", !HidDevice.MatchesPath(path, "1D57", "FA60", "col03"));
            Check("path mismatch iface", !HidDevice.MatchesPath(path, "1D57", "FA65", "mi_03"));
            Check("null path -> false", !HidDevice.MatchesPath(null, "1D57", "FA65", null));
            Check("empty iface means any", HidDevice.MatchesPath(path, "1D57", "FA65", ""));
        }

        static string TempDir()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profile-test-data");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            return dir;
        }

        static List<DeviceProfile> LoadOne(string json, out List<string> problems)
        {
            string dir = TempDir();
            File.WriteAllText(Path.Combine(dir, "one.json"), json, Encoding.UTF8);
            return ProfileLoader.Load(dir, out problems);
        }

        static void TestProfileValidation()
        {
            List<string> problems;

            string good =
                "{ \"name\":\"2.4G 无线鼠标\",\"vid\":\"1D57\",\"pids\":[\"FA60\",\"FA65\"],\"iface\":\"col03\"," +
                "\"mode\":\"passive\",\"readLength\":5,\"match\":{\"0\":\"0x03\",\"2\":\"0x40\"}," +
                "\"battery\":4,\"modelIndex\":1,\"models\":{\"0x93\":{\"name\":\"IN10\"},\"0x10\":{\"name\":\"R1\",\"scale\":10}}}";
            var list = LoadOne(good, out problems);
            Check("valid family config loads", list.Count == 1 && problems.Count == 0);
            Check("scale kept", list.Count == 1 && list[0].FindModel(0x10).scale == 10);

            LoadOne("{ \"name\":\"X\",\"vid\":\"1D57\",\"pids\":[\"FA6\"],\"mode\":\"passive\",\"readLength\":5,\"battery\":4 }", out problems);
            Check("bad pids flagged", problems.Count >= 1);

            LoadOne("{ \"name\":\"X\",\"vid\":\"1D57\",\"pid\":\"FA65\",\"mode\":\"passive\",\"readLength\":5,\"battery\":4,\"modelIndex\":9,\"models\":{\"0x93\":{\"name\":\"IN10\"}} }", out problems);
            Check("modelIndex out of range flagged", problems.Count >= 1);

            LoadOne("{ \"name\":\"X\",\"vid\":\"1D57\",\"pid\":\"FA65\",\"mode\":\"passive\",\"readLength\":5,\"battery\":4,\"models\":{\"0x93\":{\"name\":\"IN10\"}} }", out problems);
            Check("models without modelIndex flagged", problems.Count >= 1);

            LoadOne("{ \"name\":\"X\",\"vid\":\"1D57\",\"mode\":\"passive\",\"readLength\":5,\"battery\":4 }", out problems);
            Check("missing pid/pids flagged", problems.Count >= 1);
        }

        static void TestShippedConfigs()
        {
            string devices = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "devices"));
            if (!Directory.Exists(devices))
            {
                Check("shipped devices dir found " + devices, false);
                return;
            }

            List<string> problems;
            var list = ProfileLoader.Load(devices, out problems);
            Check("shipped configs load without problems", problems.Count == 0);
            Check("shipped configs non-empty", list.Count >= 2);

            bool family = false;
            foreach (var p in list)
            {
                if (p.pids != null && p.pids.Count > 0 && p.modelIndex.HasValue) family = true;
            }
            Check("shipped config has beken family", family);
        }

        static void Check(string name, bool ok)
        {
            Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name);
            if (!ok) _fail++;
        }
    }
}
