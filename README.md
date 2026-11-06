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
- **只显示当前接入的设备** —— 接收器没插、或者拔掉之后，那一行立即消失，不会留一排 `--` 空行；
  一台都没检测到时显示「未检测到设备」
- **单文件 exe** —— 约 46 KB，无第三方依赖（只需 .NET Framework 4.x）；托盘图标是代码画的，不带资源文件
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
├── devices\
│   ├── beken-1d57.json
│   └── ky87.json
└── install-autostart.bat
```

1. 双击 `BatteryMonitor.exe`
2. 需要开机自启：右键 `install-autostart.bat` → 以管理员身份运行

启动时会先试注册托盘图标；注册不了才弹 UAC 提权重来一次（拒绝也能用，退化成一直显示悬浮层）。

退出：**托盘图标右键 → 退出**。也可以在任务管理器里结束 `BatteryMonitor.exe`。

## 托盘菜单

| 项 | 作用 |
|---|---|
| `IN10 100%` 等 | 只读，显示每台设备当前电量 |
| 自动：全屏时显示悬浮层 | 默认。平时只留托盘，全屏时浮出 |
| 总是显示悬浮层 | 不管全屏与否都显示 |
| 隐藏悬浮层，只留托盘 | 只在托盘里看 |
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

> IN10 也在上面的 1D57 家族里（型号 ID `0x93`），不再单独占一个配置文件；
> 同一接收器换了鼠标会自动改名（同一时刻只显示最近有数据的那一只）。
> 型号表与报文格式来自两个 MIT 项目，见 [设备数据来源与致谢](#设备数据来源与致谢)。

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
package.bat v1.0.1
```

产物在 `dist\BatteryMonitor-<版本>.zip`（例如 `dist\BatteryMonitor-v1.0.1.zip`），包含 exe、devices、文档和脚本。

## 目录结构

```
src/
  Profile.cs          设备描述模型 + JSON 加载
  HidDevice.cs        通用 HID 打开/读写
  HidPresence.cs      判断设备接口当前在不在（决定界面显示哪些设备）
  DeviceWorker.cs     按 profile 驱动设备（被动 / 查询；型号识别）
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
  device.schema.json  JSON Schema（编辑器自动补全）
docs/
  ADD_DEVICE.md       如何适配新设备
  DESIGN.md           设计取舍
  screenshot.png      悬浮层截图
  tray.png            托盘图标样张
  social-preview.png  仓库社交预览图（在 GitHub 设置里上传）
USAGE-zh.txt              中文使用说明
test.bat                  逻辑单测（外壳窗口判定 / 配置模型 / 接口路径匹配）
tests/
  ShellWindowRulesTest.cs
  ProfileModelTest.cs
install-autostart.bat     注册开机自启
uninstall-autostart.bat   取消开机自启
```

## 已知限制

- 键盘休眠时会读不到，保留上次数值；按键后自动恢复
  （连续 5 分钟都读不到才退回 `--`，避免旧值一直冒充实时值）
- 界面只显示接收器当前插着的设备；设备休眠不会让行消失——判据是接收器接口在不在，
  不是最近有没有数据。拔掉接收器那一行立即消失
- 同一接收器同时配了两只设备时，同一时刻只显示最近有数据的那一只（型号要连续 3 帧稳定才切换）
- 查询类设备默认 30 秒轮询一次
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
- 仅 Windows（依赖 setupapi / user32 / shell32）
- 需要 .NET Framework 4.x（Windows 10/11 自带）

## 路线图

- `v1.0.x`：修 bug、稳定性
- `v1.1.0`：托盘图标 + 全屏自动浮出 + 按需提权（已完成）
- 不做：灯光控制、按键重映射、跨平台（理由见 [docs/DESIGN.md](docs/DESIGN.md)）

## 更多文档

| 文档 | 内容 |
|---|---|
| [docs/DESIGN.md](docs/DESIGN.md) | 设计取舍：为什么是纯 HID、托盘与悬浮层怎么分工、为什么锁 C# 5 |
| [docs/ADD_DEVICE.md](docs/ADD_DEVICE.md) | 适配新设备的完整流程 |
| [RELEASING.md](RELEASING.md) | 发版清单 |
| [CONTRIBUTING.md](CONTRIBUTING.md) | 贡献指南（含 C# 5 语法约束） |
| [DISCLAIMER.md](DISCLAIMER.md) | 免责声明 |

## 设备数据来源与致谢

内置的 1D57 无线接收器家族配置（[`devices/beken-1d57.json`](devices/beken-1d57.json)：
48 个型号 ID，电量报文 `03 <型号ID> 40 <状态> <电量>`）用到了下面两个 **MIT** 项目公开的事实性信息，
按其许可保留版权声明：

| 项目 | 版权 | 用到的内容 |
|---|---|---|
| [attack-shark-x11-driver](https://github.com/HarukaYamamoto0/attack-shark-x11-driver) | Copyright (c) 2026 HarukaYamamoto0 | 型号 ID 表（`devices.ts`）、电量报文格式、充电态取值 |
| [mouse-battery-tray](https://github.com/incconutwo/mouse-battery-tray) | Copyright (c) 2026 incconu_two | Beken 接收器读取方式、接收器 PID、10% 档换算 |

本项目的实现是独立完成的，只使用了上述项目公开的协议事实，没有包含它们的代码；
完整 MIT 许可文本见各自仓库的 `LICENSE`。

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
- **Plugged-in devices only** — rows appear when a receiver is present and disappear
  the moment it is unplugged
- **Single ~46 KB exe** — only needs .NET Framework 4.x
- **Read-only** — it never writes to the device

Build with `build.bat`, package a release with `package.bat`.
See `docs/ADD_DEVICE.md` for adding devices (capture, locate the battery byte,
write a profile).

Not affiliated with any hardware vendor. See `DISCLAIMER.md` before use.
Licensed under MIT.
