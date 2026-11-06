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
            TestNewFields();
            TestProtocolCodec();
            TestVariantProtocol();
            TestHidppCodec();
            TestConfigManifest();
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

        static string DeviceJson(string name)
        {
            return "{ \"name\":\"" + name + "\",\"vid\":\"1D57\",\"pid\":\"FA65\",\"iface\":\"col03\"," +
                   "\"mode\":\"passive\",\"readLength\":5,\"battery\":4 }";
        }

        static void TestConfigManifest()
        {
            // 清单缺失：不抛异常，按没有旧配置处理
            string dir = TempDir();
            Check("missing manifest -> empty obsolete", ConfigManifest.LoadObsolete(dir).Count == 0);

            // 清单损坏：容忍
            File.WriteAllText(Path.Combine(dir, "manifest.json"), "{ broken", Encoding.UTF8);
            Check("broken manifest -> empty obsolete", ConfigManifest.LoadObsolete(dir).Count == 0);

            // 正常清单 + 旧文件：忽略并顺手删除
            dir = TempDir();
            File.WriteAllText(Path.Combine(dir, "manifest.json"),
                "{\"version\":\"test\",\"obsolete\":[\"in10.json\"]}", Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "in10.json"), DeviceJson("IN10"), Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "ky87.json"), DeviceJson("KY87"), Encoding.UTF8);

            List<string> ignored = ConfigManifest.ApplyObsolete(dir);
            Check("obsolete listed", ignored.Count == 1 && ignored[0] == "in10.json");
            Check("obsolete file deleted when writable", !File.Exists(Path.Combine(dir, "in10.json")));

            // 模拟"没有权限、删不掉"：文件还在，但也不会被加载
            File.WriteAllText(Path.Combine(dir, "in10.json"), DeviceJson("IN10"), Encoding.UTF8);
            List<string> problems;
            var profiles = ProfileLoader.Load(dir, out problems, ignored);
            Check("obsolete config not loaded", profiles.Count == 1 && profiles[0].name == "KY87");
            Check("manifest.json not loaded as device", problems.Count == 0);

            // 没有清单时，manifest.json 本身也会被加载器跳过
            dir = TempDir();
            File.WriteAllText(Path.Combine(dir, "manifest.json"), "{\"obsolete\":[]}", Encoding.UTF8);
            profiles = ProfileLoader.Load(dir, out problems, null);
            Check("manifest.json skipped by loader", profiles.Count == 0 && problems.Count == 0);
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
            List<string> ignored = ConfigManifest.ApplyObsolete(devices);
            var list = ProfileLoader.Load(devices, out problems, ignored);
            Check("shipped configs load without problems", problems.Count == 0);
            if (problems.Count > 0)
            {
                foreach (string s in problems) Console.WriteLine("      " + s);
            }
            Check("shipped device configs count", list.Count == 10);

            bool family = false;
            bool compx = false;
            bool compx54 = false;
            bool mchose = false;
            bool hidpp = false;
            bool a8a4 = false;
            foreach (var p in list)
            {
                if (p.pids != null && p.pids.Count > 0 && p.modelIndex.HasValue) family = true;
                if (p.name == "ATK 无线鼠标")
                {
                    compx = true;
                    byte[] frame = ProtocolCodec.BuildQuery(p);
                    Check("compx query frame length", frame != null && frame.Length == 17);
                    Check("compx query report id", frame != null && frame[0] == 0x08 && frame[1] == 0x04);
                    Check("compx query checksum 0x49", frame != null && frame[16] == 0x49);
                }
                if (p.name == "ATK Zero 无线鼠标")
                {
                    compx54 = true;
                    var plans = ProtocolCodec.BuildQueries(p);
                    Check("54L15 query frame length", plans.Count == 1 && plans[0].Frame.Length == 64);
                    Check("54L15 link index", plans[0].Frame[5] == 0x01 && plans[0].Frame[6] == 0x07);
                }
                if (p.name == "MCHOSE A7 Pro") mchose = true;
                if (p.IsHidpp2) hidpp = true;
                if (p.name == "A8A5 无线鼠标")
                {
                    a8a4 = true;
                    Check("A8A5 has two dialect frames", ProtocolCodec.BuildQueries(p).Count == 2);
                }
            }
            Check("shipped config has beken family", family);
            Check("shipped config has compx", compx);
            Check("shipped config has compx 54L15", compx54);
            Check("shipped config has mchose", mchose);
            Check("shipped config has logitech hidpp", hidpp);
            Check("shipped config has a8a4 family", a8a4);

            bool listsIn10 = false;
            foreach (string name in ConfigManifest.LoadObsolete(devices))
            {
                if (string.Equals(name, "in10.json", StringComparison.OrdinalIgnoreCase)) listsIn10 = true;
            }
            Check("shipped manifest lists obsolete in10.json", listsIn10);
        }

        // ---------------- 新字段：usage / io / charging / payloadXor ----------------

        static void TestNewFields()
        {
            var p = new DeviceProfile();
            p.usage = "FF02/0002";
            ushort page, usage;
            Check("usage parsed", p.TryGetUsage(out page, out usage) && page == 0xFF02 && usage == 0x0002);
            p.usage = "bad";
            Check("bad usage rejected", !p.TryGetUsage(out page, out usage));

            List<string> problems;
            string hidpp = "{ \"name\":\"Logitech 无线设备\",\"vid\":\"046D\",\"pids\":[\"C52B\"],\"protocol\":\"hidpp2\" }";
            var list = LoadOne(hidpp, out problems);
            Check("hidpp2 loads without battery", list.Count == 1 && problems.Count == 0 && list[0].IsHidpp2);

            LoadOne("{ \"name\":\"X\",\"vid\":\"046D\",\"pids\":[\"C52B\"],\"protocol\":\"hidpp2\",\"battery\":5 }", out problems);
            Check("hidpp2 with battery flagged", problems.Count >= 1);

            LoadOne("{ \"name\":\"X\",\"vid\":\"046D\",\"pids\":[\"C52B\"],\"protocol\":\"hidpp2\",\"query\":{\"length\":7} }", out problems);
            Check("hidpp2 with query flagged", problems.Count >= 1);

            string compx =
                "{ \"name\":\"X\",\"vid\":\"373B\",\"pids\":[\"1031\"],\"usage\":\"FF02/0002\",\"mode\":\"query\"," +
                "\"io\":\"output\",\"battery\":6,\"query\":{\"length\":17,\"set\":{\"0\":\"0x08\",\"1\":\"0x04\"}," +
                "\"checksum\":{\"type\":\"sumTo\",\"target\":\"0x55\",\"range\":[0,15],\"at\":16}}," +
                "\"response\":{\"length\":17,\"expect\":{\"0\":\"0x08\",\"1\":\"0x04\"}} }";
            list = LoadOne(compx, out problems);
            Check("compx config loads", list.Count == 1 && problems.Count == 0);

            LoadOne("{ \"name\":\"X\",\"vid\":\"373B\",\"pid\":\"1031\",\"mode\":\"query\",\"battery\":6,\"usage\":\"ZZ/1\"," +
                "\"query\":{\"length\":17},\"response\":{\"length\":17} }", out problems);
            Check("bad usage flagged", problems.Count >= 1);

            LoadOne("{ \"name\":\"X\",\"vid\":\"5253\",\"pid\":\"1021\",\"mode\":\"query\",\"battery\":11,\"payloadXor\":\"0xFFFF\"," +
                "\"query\":{\"length\":65},\"response\":{\"length\":65} }", out problems);
            Check("bad payloadXor flagged", problems.Count >= 1);
        }

        // ---------------- 协议编解码（真机验证不了，字节级必须钉死） ----------------

        static DeviceProfile CompxProfile()
        {
            var p = new DeviceProfile();
            p.name = "ATK 无线鼠标";
            p.vid = "373B";
            p.pids = new List<string> { "1031" };
            p.usage = "FF02/0002";
            p.mode = "query";
            p.io = "output";
            p.battery = 6;
            p.scale = 1;
            p.charging = new ChargingSpec { offset = 7, nonZero = true };
            p.query = new QuerySpec
            {
                length = 17,
                set = new Dictionary<string, string> { { "0", "0x08" }, { "1", "0x04" } },
                checksum = new CheckSpec
                {
                    type = "sumTo",
                    at = 16,
                    target = "0x55",
                    range = new int[] { 0, 15 }
                }
            };
            p.response = new ResponseSpec
            {
                length = 17,
                expect = new Dictionary<string, string> { { "0", "0x08" }, { "1", "0x04" } }
            };
            return p;
        }

        static void TestProtocolCodec()
        {
            var p = CompxProfile();
            byte[] frame = ProtocolCodec.BuildQuery(p);
            Check("compx frame builds", frame.Length == 17 && frame[0] == 0x08 && frame[1] == 0x04);
            Check("compx frame checksum", frame[16] == 0x49);
            Check("compx frame rest zero", frame[2] == 0 && frame[15] == 0);

            var verifySpec = new CheckSpec { type = "sumTo", target = "0x55", range = new int[] { 0, 16 } };
            Check("compx frame verifies", ProtocolCodec.VerifyChecksum(frame, verifySpec));

            // 应答：17 字节，[6]=电量，[7]=有线/充电
            var resp = new byte[17];
            resp[0] = 0x08; resp[1] = 0x04; resp[6] = 77; resp[7] = 1;
            int pct; bool charging; bool full;
            Check("compx decode", ProtocolCodec.Decode(p, resp, out pct, out charging, out full)
                && pct == 77 && charging && !full);

            resp[7] = 0;
            Check("compx decode not charging", ProtocolCodec.Decode(p, resp, out pct, out charging, out full)
                && pct == 77 && !charging);

            resp[0] = 0x0A;    // 回显不对：不是我们发的命令
            Check("compx wrong echo rejected", !ProtocolCodec.Decode(p, resp, out pct, out charging, out full));

            // 倍率与下限
            var scaled = CompxProfile();
            scaled.scale = 10;
            scaled.minPercent = 1;
            var r10 = new byte[17];
            r10[0] = 0x08; r10[1] = 0x04; r10[6] = 8;
            Check("scale applied", ProtocolCodec.Decode(scaled, r10, out pct, out charging, out full) && pct == 80);
            r10[6] = 0;
            Check("minPercent rejects zero", !ProtocolCodec.Decode(scaled, r10, out pct, out charging, out full));

            // MCHOSE：写帧整包异或，读帧解异或后 [11]=电量、[12]==1 表示充电
            var m = new DeviceProfile();
            m.name = "MCHOSE A7 Pro";
            m.vid = "5253";
            m.pids = new List<string> { "1021" };
            m.mode = "query";
            m.io = "feature";
            m.battery = 11;
            m.minPercent = 1;
            m.payloadXor = "0xFF";
            m.charging = new ChargingSpec { offset = 12, nonZero = true, values = new List<string> { "0x01" } };
            m.query = new QuerySpec { length = 65, set = new Dictionary<string, string> { { "0", "0x11" }, { "1", "0x06" } } };
            m.response = new ResponseSpec { length = 65, expect = new Dictionary<string, string> { { "0", "0x11" } } };

            byte[] mf = ProtocolCodec.BuildQuery(m);
            Check("mchose frame report id", mf.Length == 65 && mf[0] == 0x11);
            Check("mchose frame xor", mf[1] == (0x06 ^ 0xFF) && mf[2] == 0xFF && mf[64] == 0xFF);

            var mresp = new byte[65];
            mresp[0] = 0x11;
            mresp[11] = (byte)(42 ^ 0xFF);      // 电量 42，异或后落盘
            mresp[12] = (byte)(1 ^ 0xFF);       // 充电中
            Check("mchose decode", ProtocolCodec.Decode(m, mresp, out pct, out charging, out full)
                && pct == 42 && charging);

            mresp[12] = (byte)(0 ^ 0xFF);       // 未充电
            Check("mchose decode charging values", ProtocolCodec.Decode(m, mresp, out pct, out charging, out full)
                && pct == 42 && !charging);

            // 不带异或的原始帧也应能兜底解出来（固件版本差异）
            var plain = new byte[65];
            plain[0] = 0x11;
            plain[11] = 33;
            plain[12] = 0;
            Check("mchose raw fallback", ProtocolCodec.Decode(m, plain, out pct, out charging, out full)
                && pct == 33 && !charging);
        }

        // ---------------- 追加协议：Compx 54L15 与 A8A4/A8A5 的两种方言 ----------------

        static void TestVariantProtocol()
        {
            // Compx / Nordic 54L15（ATK Zero）：64 字节，第 5 位是 link（0 有线 / 1 无线）
            var p = new DeviceProfile();
            p.name = "ATK Zero 无线鼠标";
            p.vid = "373B";
            p.pids = new List<string> { "1155", "124F" };
            p.usage = "FF05/0001";
            p.mode = "query";
            p.io = "output";
            p.battery = 7;
            p.query = new QuerySpec
            {
                length = 64,
                set = new Dictionary<string, string>
                {
                    { "0", "0x08" }, { "1", "0x7D" }, { "2", "0x72" }, { "3", "0x02" },
                    { "5", "0x01" }, { "6", "0x07" }, { "7", "0x01" }
                }
            };
            p.response = new ResponseSpec
            {
                length = 64,
                expect = new Dictionary<string, string>
                {
                    { "0", "0x08" }, { "1", "0x72" }, { "2", "0x00" }, { "5", "0x07" }
                }
            };

            var plans = ProtocolCodec.BuildQueries(p);
            Check("54L15 single plan", plans.Count == 1 && plans[0].Frame.Length == 64);
            Check("54L15 frame bytes", plans[0].Frame[0] == 0x08 && plans[0].Frame[1] == 0x7D
                && plans[0].Frame[2] == 0x72 && plans[0].Frame[3] == 0x02
                && plans[0].Frame[4] == 0x00 && plans[0].Frame[5] == 0x01
                && plans[0].Frame[6] == 0x07 && plans[0].Frame[7] == 0x01);

            var resp = new byte[64];
            resp[0] = 0x08; resp[1] = 0x72; resp[2] = 0x00; resp[5] = 0x07; resp[7] = 42;
            int pct; bool ch, full;
            Check("54L15 decode", ProtocolCodec.Decode(p, plans[0], resp, out pct, out ch, out full) && pct == 42);

            resp[2] = 0x01;   // 接收器转发了，但鼠标没答（休眠）→ 不能当电量
            Check("54L15 sleeping rejected", !ProtocolCodec.Decode(p, plans[0], resp, out pct, out ch, out full));

            // A8A4/A8A5：两种方言，一套配置里按顺序试
            var a = new DeviceProfile();
            a.name = "A8A5 无线鼠标";
            a.vid = "A8A5";
            a.pids = new List<string> { "2255", "0001" };
            a.usage = "FF01/0010";
            a.mode = "query";
            a.io = "output";
            a.battery = 8;
            a.minPercent = 1;
            a.charging = new ChargingSpec { offset = 9, nonZero = true };
            a.query = new QuerySpec
            {
                length = 64,
                variants = new List<QueryVariant>
                {
                    new QueryVariant
                    {
                        set = new Dictionary<string, string>
                        {
                            { "0", "0x55" }, { "1", "0x30" }, { "2", "0xA5" }, { "3", "0x0B" },
                            { "4", "0x2E" }, { "5", "0x01" }, { "6", "0x01" }, { "7", "0x01" }
                        },
                        expect = new Dictionary<string, string> { { "0", "0x55" }, { "1", "0x30" } }
                    },
                    new QueryVariant
                    {
                        set = new Dictionary<string, string>
                        {
                            { "0", "0xF0" }, { "1", "0x30" }, { "2", "0x01" }, { "4", "0x2E" }
                        },
                        checksum = new CheckSpec { type = "sum8", range = new int[] { 4, 63 }, at = 3 },
                        expect = new Dictionary<string, string> { { "0", "0xF0" }, { "1", "0x30" } },
                        verify = new CheckSpec { type = "sum8", range = new int[] { 4, 63 }, at = 3 }
                    }
                }
            };
            a.response = new ResponseSpec { length = 64 };

            var aPlans = ProtocolCodec.BuildQueries(a);
            Check("A8A5 two dialects", aPlans.Count == 2);
            Check("A8A5 dialect A frame", aPlans[0].Frame[0] == 0x55 && aPlans[0].Frame[1] == 0x30
                && aPlans[0].Frame[2] == 0xA5 && aPlans[0].Frame[4] == 0x2E);
            Check("A8A5 dialect B checksum", aPlans[1].Frame[0] == 0xF0 && aPlans[1].Frame[3] == 0x2E);

            var ra = new byte[64];
            ra[0] = 0x55; ra[1] = 0x30; ra[8] = 57; ra[9] = 0;
            Check("A8A5 dialect A decode", ProtocolCodec.Decode(a, aPlans[0], ra, out pct, out ch, out full)
                && pct == 57 && !ch);
            ra[9] = 1;
            Check("A8A5 charging flag", ProtocolCodec.Decode(a, aPlans[0], ra, out pct, out ch, out full) && ch);

            var rb = new byte[64];
            rb[0] = 0xF0; rb[1] = 0x30; rb[2] = 0x01; rb[4] = 0x2E; rb[8] = 88;
            int sum = 0;
            for (int i = 4; i <= 63; i++) sum += rb[i];
            rb[3] = (byte)(sum & 0xFF);
            Check("A8A5 dialect B decode", ProtocolCodec.Decode(a, aPlans[1], rb, out pct, out ch, out full)
                && pct == 88);
            rb[3] = 0x00;
            Check("A8A5 dialect B checksum rejected",
                !ProtocolCodec.Decode(a, aPlans[1], rb, out pct, out ch, out full));
        }

        // ---------------- HID++ 帧（罗技） ----------------

        static void TestHidppCodec()
        {
            byte[] shortReq = HidppCodec.BuildShort(0xFF, HidppCodec.RootFeature, 0x01, 0x00, 0x00, HidppCodec.PingMarker);
            Check("hidpp short length", shortReq.Length == 7);
            Check("hidpp short bytes", shortReq[0] == 0x10 && shortReq[1] == 0xFF && shortReq[2] == 0x00
                && shortReq[3] == ((0x01 << 4) | HidppCodec.SwId) && shortReq[6] == 0xAA);

            byte[] longReq = HidppCodec.BuildLong(0xFF, 0x00, 0x00, new byte[] { 0x10, 0x00 });
            Check("hidpp long length", longReq.Length == 20);
            Check("hidpp long header", longReq[0] == 0x11 && longReq[1] == 0xFF && longReq[2] == 0x00
                && longReq[3] == ((0x00 << 4) | HidppCodec.SwId));
            Check("hidpp long payload", longReq[4] == 0x10 && longReq[5] == 0x00);

            var reply = new byte[20];
            reply[0] = 0x11; reply[1] = 0xFF; reply[2] = 0x00; reply[3] = longReq[3];
            reply[4] = 0x08;
            Check("hidpp reply matches", HidppCodec.TryMatch(longReq, reply));
            reply[1] = 0x02;
            Check("hidpp wrong device index rejected", !HidppCodec.TryMatch(longReq, reply));

            var err = new byte[7];
            err[0] = 0x10; err[1] = 0xFF; err[2] = 0xFF; err[3] = 0x00; err[5] = 0x01;
            Check("hidpp error frame detected", HidppCodec.IsError(err, 0x00));
            Check("hidpp error frame not match", !HidppCodec.TryMatch(shortReq, err));

            var legacy = new byte[7];
            legacy[0] = 0x10; legacy[1] = 0xFF; legacy[2] = 0x8F;
            Check("hidpp 1.0 legacy error detected", HidppCodec.IsLegacyError(legacy));

            var s1000 = new byte[20];
            s1000[0] = 0x11; s1000[1] = 0xFF; s1000[2] = 0x08; s1000[3] = 0x0C;
            s1000[4] = 0x64; s1000[5] = 0x32; s1000[6] = 0x00;
            int status;
            Check("0x1000 parse 100%", HidppCodec.ParseBatteryLevelStatus(s1000, out status) == 100 && status == 0);
            Check("0x1000 status 3 means full", HidppCodec.ParseBatteryLevelStatus(MakeReply(0x2A, 0x11, 0x03), out status) == 100 && status == 3);
            Check("0x1004 parse 99%", HidppCodec.ParseUnifiedBattery(MakeReply(0x63, 0x08, 0x03), out status) == 100 && status == 3);
            Check("0x1004 parse 50% discharging", HidppCodec.ParseUnifiedBattery(MakeReply(0x32, 0x02, 0x00), out status) == 50 && status == 0);
            Check("0x1000 charging states", HidppCodec.LevelStatusCharging(1) && HidppCodec.LevelStatusCharging(2)
                && !HidppCodec.LevelStatusCharging(0) && !HidppCodec.LevelStatusCharging(3));
            Check("0x1004 charging states", HidppCodec.UnifiedCharging(1) && HidppCodec.UnifiedCharging(2)
                && !HidppCodec.UnifiedCharging(0) && !HidppCodec.UnifiedCharging(3));
        }

        static byte[] MakeReply(byte percent, byte level, byte status)
        {
            var r = new byte[20];
            r[0] = 0x11; r[1] = 0xFF; r[2] = 0x08; r[3] = 0x0C;
            r[4] = percent; r[5] = level; r[6] = status;
            return r;
        }

        static void Check(string name, bool ok)
        {
            Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name);
            if (!ok) _fail++;
        }
    }
}
