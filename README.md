# BatteryMonitor

[![build](https://github.com/Lemon140339/BatteryMonitor/actions/workflows/build.yml/badge.svg)](https://github.com/Lemon140339/BatteryMonitor/actions/workflows/build.yml)
[![release](https://img.shields.io/github/v/release/Lemon140339/BatteryMonitor?label=release)](https://github.com/Lemon140339/BatteryMonitor/releases)
[![license](https://img.shields.io/github/license/Lemon140339/BatteryMonitor)](LICENSE)

在 Windows 桌面右上角显示无线键鼠电量的轻量工具。
平时待在托盘里，全屏看视频或玩游戏时自动浮到右上角。

**纯 HID 直读，不安装任何厂商驱动或后台服务。** 加一台新设备只需要改一个 JSON 文件。

---

## 特性

- **两种形态** —— 平时待在托盘（悬停看电量），全屏看视频或玩游戏时自动浮到右上角
- **透明文字层** —— 右上层背景完全透明，鼠标穿透，不影响任何点击
- **配置驱动** —— 设备协议写在 `devices/*.json`，新增设备不用改代码
- **多品牌直读** —— 罗技 Lightspeed/Bolt/Unifying 走 HID++ 2.0；ATK / VXE·VGN / 达尔优 / G-Wolves /
  MCHOSE / A8A5 等 2.4G 接收器走各自的查询协议，都写在 `devices/*.json` 里
- **只显示当前接入的设备** —— 接收器没插、或者拔掉之后，那一行立即消失，不会留一排 `--` 空行；
  一台都没检测到时显示「未检测到设备」
- **单文件 exe** —— 约 75 KB，无第三方依赖（只需 .NET Framework 4.x）；托盘图标是代码画的，不带资源文件
- **只读** —— 不写设备、不改设置，仅做查询与监听

## 显示效果

平时托盘里**一台设备画一个电池**（竖着排，各自按电量着色），鼠标悬停看具体数值：

![托盘图标](docs/tray.png)

全屏（浏览器 / 无边框游戏）时悬浮层自动浮出右上角：

![悬浮层](docs/screenshot.png)

- 设备名按电量变色：绿 >50% / 黄 20–50% / 红 <20%
- 百分比为浅白色，带淡黑描边，浅色壁纸下也清晰
- 设备名与百分比各占一列、右边缘对齐：`100%` 和 `7%` 都不会把设备名挤歪
- 宽度按内容自适应，设备名多长都不会被裁
- 悬浮层固定右上角、置顶、不可拖动、鼠标穿透

## 快速开始

```
BatteryMonitor\
├── BatteryMonitor.exe
├── devices\           内置 10 份配置（罗技、ATK、VXE/VGN、达尔优、G-Wolves、MCHOSE、A8A5、INPHIC…）
└── USAGE-zh.txt
```

1. 双击 `BatteryMonitor.exe`
2. 需要开机自启：托盘图标右键 → **开机自启**（登录时以最高权限启动；第一次设置会弹一次 UAC）

启动时会先试注册托盘图标；注册不了才弹 UAC 提权重来一次（拒绝也能用，退化成一直显示悬浮层）。

退出：**托盘图标右键 → 退出**。也可以在任务管理器里结束 `BatteryMonitor.exe`。

## 升级

直接把新版本的文件覆盖到旧目录即可，不需要先卸载，也不用手动删文件。

旧版本删掉或改名的配置文件会留在 `devices\` 里，程序启动时会按
[`devices/manifest.json`](devices/manifest.json) 的 `obsolete` 列表自动忽略
（有写权限时顺手删除），所以不会出现"同一个设备显示两行"这种升级残留问题。

## 托盘菜单

| 项 | 作用 |
|---|---|
| `IN10 100%` 等 | 只读，显示每台设备当前电量 |
| 自动：全屏时显示悬浮层 | 默认。平时只留托盘，全屏时浮出 |
| 总是显示悬浮层 | 不管全屏与否都显示 |
| 隐藏悬浮层，只留托盘 | 只在托盘里看 |
| 开机自启 | 登录时以最高权限启动（计划任务）。勾选状态表示当前已开启；改动时可能需要一次管理员授权 |
| 退出 | 结束程序 |

> 选中的模式会被记住（存在 `%APPDATA%\BatteryMonitor\settings.json`），下次启动沿用，不用每次重选。

## 调试模式

```
BatteryMonitor.exe --log
```

会在 exe 同目录生成 `battery.log`，记录每次电量变化：

```
17:04:23  KY87 = 74
17:04:24  IN10 = 38
```

排查新设备时非常有用。

## 添加新设备

见 [docs/ADD_DEVICE.md](docs/ADD_DEVICE.md)。

核心流程：抓一次包 → 找到电量字节 → 写一个 JSON → 用 `--log` 验证。

写错了不会崩：屏幕右上角会多一行红字「配置有误 N 项」，
同目录生成 `config-error.log` 逐条说明哪一行、哪个字段有问题。

## 内置设备描述

| 名称 | 接收器 VID:PID | 接口 | 模式 |
|---|---|---|---|
| 2.4G 无线鼠标（Beken 1D57 家族） | `1D57:FA60` / `FA62` / `FA65` | `col03` | 被动读 5 字节，按报文里的型号 ID 识别（INPHIC IN10、Attack Shark X11 等 48 个型号） |
| KY87 | `0C45:FEFE` | `mi_03` | 查询 33 字节 |
| ATK 无线鼠标 | `373B:1031` / `102E` / `11D9` / `11B6` / … | usage `FF02/02` | 查询 17 字节，命令 `08 04`，整帧和恒为 `0x55` |
| ATK Zero 无线鼠标 | `373B:1155` / `124F` | usage `FF05/01` | 查询 64 字节，命令 `08 7D 72 02 00 <link> 07 01`，电量在第 7 字节 |
| VXE / VGN 无线鼠标 | `3554:F58A` / `F58C` / `F58E` / … | usage `FF02/02` | 同上（Compx/Nordic 52840 同协议） |
| 达尔优无线鼠标 | `260D:1175` / `1193` / `1154` / `1196` | usage `FF02/02` | 同上 |
| G-Wolves 无线鼠标 | `33E4:3854` / `4719` | usage `FF02/02` | 同上 |
| A8A5 无线鼠标 | `A8A5:2255` / `0001` | usage `FF01/10` | 64 字节，命令 `0x30`，电量在第 8 字节；同一 ODM 有 `55…` / `F0…` 两种方言，两种都试 |
| MCHOSE A7 Pro | `5253:1021` / `0010` | usage `FF01` | Feature 报告 `0x11`，整包 XOR `0xFF`，电量在第 11 字节 |
| 罗技 Lightspeed / Bolt / Unifying | `046D:C54D` / `C548` / `C52B` 等 | HID++ 2.0 | feature `0x1000` / `0x1004`，**一套实现覆盖所有型号**，不需要逐型号配置 |

> 从 ATK 往下的这些新协议按公开事实实现，**尚未在真机上逐一验证**（作者手上没有这些设备）。
> 读数不对时请带 `--log` 生成的 `battery.log` 反馈，日志里能看到原始报文与失败原因。
> 罗技这条线只支持 HID++ 2.0；老 HID++ 1.0 接收器（Nano 那代）会回 `0x8F` 错误帧，暂不支持。

> IN10 也在上面的 1D57 家族里（型号 ID `0x93`），不再单独占一个配置文件；
> 同一接收器换了鼠标会自动改名（同一时刻只显示最近有数据的那一只）。
> 型号表与报文格式来自多个 MIT 项目，见 [设备数据来源与致谢](#设备数据来源与致谢)。

## 构建

需要 Windows 自带的 .NET Framework 编译器，直接运行：

```bat
build.bat
```

脚本会自动找 `csc.exe`（优先 64 位），产物为 `BatteryMonitor.exe`。

手动编译（等价命令）：

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe ^
  /nologo /target:winexe /out:BatteryMonitor.exe ^
  src\*.cs ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll
```

生成发布包（编译 + 打包成 zip），版本号必须显式传：

```bat
package.bat v1.2.1
```

产物在 `dist\BatteryMonitor-<版本>.zip`（例如 `dist\BatteryMonitor-v1.2.1.zip`），包含 exe、devices、文档和脚本。

## 目录结构

```
src/
  Profile.cs          设备描述模型 + JSON 加载
  ConfigManifest.cs   读取 devices/manifest.json，清理旧版本遗留的配置
  HidDevice.cs        通用 HID 打开/读写；Feature 报告、usage 选择、报告描述符
  Hidpp.cs            罗技 HID++ 2.0 会话（feature 发现、多设备索引、电量解析）
  ProtocolCodec.cs    配置驱动的组帧/校验/异或解码（纯逻辑，可单测）
  Autostart.cs        开机自启：计划任务的查询/注册/删除
  HidPresence.cs      判断设备接口当前在不在（决定界面显示哪些设备）
  DeviceWorker.cs     按 profile 驱动设备（被动 / 查询 / HID++；型号识别）
  WidgetRender.cs     排版与绘制（列对齐、宽度自适应，可离屏渲染）
  WidgetForm.cs       透明文字层（悬浮层）
  TrayView.cs         托盘图标（代码画的电池）+ 悬停提示 + 右键菜单
  FullscreenWatcher.cs 判断前台是否全屏
  ShellWindowRules.cs 判断前台窗口是不是桌面/外壳窗口（不算全屏）
  WidgetApp.cs        显示策略：托盘 / 悬浮层怎么分工
  DebugLog.cs         --log 支持
  Program.cs          入口（含按需提权）
devices/
  beken-1d57.json     1D57 无线接收器家族（48 个型号 ID 共用一份配置）
  ky87.json           查询模式示例
  compx-*.json        Compx/Nordic 家族（ATK 17B / ATK Zero 54L15 / VXE·VGN / 达尔优 / G-Wolves）
  a8a4.json           A8A5 ODM 无线鼠标（55… / F0… 两种方言都试）
  mchose-a7pro.json   MCHOSE Feature 报告 + 整包异或示例
  logitech-hidpp.json 罗技 HID++ 2.0（只写身份，电量由代码读）
  device.schema.json  JSON Schema（编辑器自动补全）
  manifest.json       本版本配置清单（obsolete = 升级时要清理的旧文件）
docs/
  ADD_DEVICE.md       如何适配新设备
  DESIGN.md           设计取舍
  screenshot.png      悬浮层截图
  tray.png            托盘图标样张
  social-preview.png  仓库社交预览图（在 GitHub 设置里上传）
USAGE-zh.txt              中文使用说明
test.bat                  逻辑单测（外壳窗口判定 / 配置模型 / 协议编解码 / HID++ 帧）
tests/
  ShellWindowRulesTest.cs
  ProfileModelTest.cs
```

## 已知限制

- 键盘休眠时会读不到，保留上次数值；按键后自动恢复
  （连续 5 分钟都读不到才退回 `--`，避免旧值一直冒充实时值）
- 界面只显示接收器当前插着的设备；设备休眠不会让行消失——判据是接收器接口在不在，
  不是最近有没有数据。拔掉接收器那一行立即消失
- 同一接收器同时配了两只设备时，同一时刻只显示最近有数据的那一只（型号要连续 3 帧稳定才切换）
- 查询类设备默认 30 秒轮询一次；新增的国产 2.4G 与罗技默认 60 秒（每次查询会唤醒设备，别太勤）
- **v1.2.0 新增的协议族尚未在真机上逐一验证**（作者没有对应设备）：
  Compx 52840（ATK / VXE·VGN / 达尔优 / G-Wolves）、Compx 54L15（ATK Zero）、
  A8A4/A8A5 ODM、MCHOSE A7 Pro、罗技 Lightspeed/Bolt/Unifying 都属于"按公开协议事实实现"，
  读数不对请带 `--log` 反馈
- 罗技只支持 HID++ 2.0：老 HID++ 1.0 接收器（Nano 那代）不支持；一个接收器后挂多台设备时只显示第一台能读到电量的
- 暂不支持：Razer（90 字节帧走 USB 控制传输，Windows 上要验证描述符是否声明对应 Feature 报告）、
  黑爵 AJAZZ、雷柏 VT9、MCHOSE 耳机
- **独占全屏（D3D exclusive）下看不到悬浮层**：游戏接管了屏幕合成，分层窗口画不上去；
  无边框窗口化全屏可以。这时只能靠托盘（它也被盖住，要切出来才看得到）
- **Windows 11「媒体播放器」全屏时也一样看不到**：它走直通呈现（fullscreen optimization），
  整个桌面合成层被绕过——实测**连不透明的置顶窗口都不显示**，所以不是画法问题。
  浏览器全屏、VLC/MPV 这类普通窗口全屏不受影响；要覆盖这类应用只能做注入，本项目不做
  （完整实验数据见 [docs/DESIGN.md](docs/DESIGN.md)）
- Windows 11 默认把新图标收进任务栏的「^」溢出区，想常驻可见就把它拖出来
- 托盘图标最多画 3 个电池（16×16 塞不下更多），设备超过 3 台时只显示最差的那台，
  完整数值看悬停提示和右键菜单
- 有些机器的通知区只允许高权限进程注册图标，此时启动会弹一次 UAC（拒绝也能用，退回纯悬浮层）
- 开机自启在托盘菜单里开关（对应计划任务 `BatteryMonitor`，登录时以最高权限启动）；
  程序以普通权限运行时，切换它会弹一次 UAC
- 仅 Windows（依赖 setupapi / user32 / shell32）
- 需要 .NET Framework 4.x（Windows 10/11 自带）

## 路线图

- `v1.0.x`：修 bug、稳定性（已完成）
- `v1.1.0`：托盘图标 + 全屏自动浮出 + 按需提权（已完成）
- `v1.2.0`：五个新协议族（Compx 52840 / Compx 54L15、MCHOSE、A8A4/A8A5、罗技 HID++ 2.0）（已完成）
- 下一步：按用户反馈修补协议与型号；不做灯光控制、按键重映射、跨平台（理由见 [docs/DESIGN.md](docs/DESIGN.md)）

## 更多文档

| 文档 | 内容 |
|---|---|
| [docs/DESIGN.md](docs/DESIGN.md) | 设计取舍：为什么是纯 HID、托盘与悬浮层怎么分工、为什么锁 C# 5 |
| [docs/ADD_DEVICE.md](docs/ADD_DEVICE.md) | 适配新设备的完整流程 |
| [RELEASING.md](RELEASING.md) | 发版清单 |
| [CONTRIBUTING.md](CONTRIBUTING.md) | 贡献指南（含 C# 5 语法约束） |
| [DISCLAIMER.md](DISCLAIMER.md) | 免责声明 |

## 设备数据来源与致谢

内置设备配置用到了以下 **MIT** 项目公开的事实性信息，按其许可保留版权声明
（其中 1D57 家族的 48 个型号与报文 `03 <型号ID> 40 <状态> <电量>` 来自前两个项目）：

| 项目 | 版权 | 用到的内容 |
|---|---|---|
| [attack-shark-x11-driver](https://github.com/HarukaYamamoto0/attack-shark-x11-driver) | Copyright (c) 2026 HarukaYamamoto0 | 型号 ID 表（`devices.ts`）、电量报文格式、充电态取值 |
| [mouse-battery-tray](https://github.com/incconutwo/mouse-battery-tray) | Copyright (c) 2026 incconu_two | Beken 接收器读取方式、接收器 PID、10% 档换算 |
| [InphicBatteryTray](https://github.com/Tan-TanZi/InphicBatteryTray) | Copyright (c) 2026 Tan-TanZi | 1D57 被动 5 字节报文的独立出处（`03 <类型> 40 <设备#> <电量>`）、INPHIC IN9 的 Feature 协议 |
| [power-island](https://github.com/lingdiaoluo/power-island) | Copyright (c) 2026 lingdiaoluo | Compx/Nordic 52840 的 17 字节查询帧、电量/有线字节位置、usage 集合 |
| [vxe-monitor](https://github.com/twinkle311/vxe-monitor) | Copyright (c) 2026 twinkle311 | Compx go 包帧格式与校验公式（`0x55 - (0x08 + Σ)`，解释了固定尾字节 `0x49`） |
| [GearPower](https://github.com/XAVIER0229/GearPower) | Copyright (c) 2026 XAVIER0229 | Compx 家族的接收器 PID 集合、协议交叉核对 |
| [mchose-2.4g-battery-tray](https://github.com/Sanye223/mchose-2.4g-battery-tray) | Copyright (c) 2026 Sanye223 | MCHOSE 的 `0x11` + 整包 XOR `0xFF` 协议、电量/充电字节位置、"不要发 `F4 55`"的真机结论 |

本项目的实现是独立完成的，只使用了上述项目公开的协议事实，没有包含它们的代码；
完整 MIT 许可文本见各自仓库的 `LICENSE`。

罗技 HID++ 2.0 的协议事实参考了 [Solaar](https://github.com/pwr-Solaar/Solaar)（GPL-2.0）、
[logiops](https://github.com/PixlOne/logiops)（GPL-3.0）与 Linux 内核的 `hid-logitech-hidpp.c`（GPL-2.0）。
**只取事实**（feature 编号、报文布局、状态枚举），没有复制它们的代码、设备表或电压曲线；
mV→% 曲线若后续实现将自行拟合并注明为经验值。GPL 与本项目的 MIT 不兼容，故不引入任何 GPL 文件。

也感谢 [OpenMouse](https://github.com/OpenMouse-Project/openmouse)（AGPL-3.0）：
它帮助我们交叉核对了 Beken 家族的型号表与报文语义。**没有复制它的代码或数据表**——
AGPL-3.0 与本项目的 MIT 不兼容，只作为参考资料。

## 参与贡献

- 报告问题：用 [Issue 模板](https://github.com/Lemon140339/BatteryMonitor/issues/new/choose)
- 适配新设备：照着 [docs/ADD_DEVICE.md](docs/ADD_DEVICE.md) 走一遍，用 `--log` 验证后提 PR
- 改代码：先看 [CONTRIBUTING.md](CONTRIBUTING.md)，注意必须保持 C# 5 语法

## 免责声明

本项目与任何硬件厂商无关，协议来自对公开行为的观察分析，只做读取，不写设备。
完整条款见 [DISCLAIMER.md](DISCLAIMER.md)，请在使用前阅读。

## License

MIT，见 [LICENSE](LICENSE)。

---

## English

A tiny Windows tray app that shows wireless mouse and keyboard battery levels.
It stays in the notification area and pops a small overlay into the top-right
corner when you go fullscreen (browser or borderless games).

- **Direct HID access** — no vendor drivers, no background services
- **Config-driven** — device protocols live in `devices/*.json`; supporting a new
  device is a JSON edit, not a code change
- **Wide device support** — Logitech HID++ 2.0 plus ATK / VXE / VGN / Dareu / G-Wolves /
  MCHOSE / A8A5 2.4G receivers
- **Plugged-in devices only** — rows appear when a receiver is present and disappear
  the moment it is unplugged
- **Upgrade-safe** — just overwrite the folder; configs removed by a release are
  cleaned up / ignored automatically (`devices/manifest.json`)
- **Single ~75 KB exe** — only needs .NET Framework 4.x
- **Read-only** — it never writes to the device

Build with `build.bat`, package a release with `package.bat`.
See `docs/ADD_DEVICE.md` for adding devices (capture, locate the battery byte,
write a profile).

Not affiliated with any hardware vendor. See `DISCLAIMER.md` before use.
Licensed under MIT.
