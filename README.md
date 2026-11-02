# BatteryMonitor

在 Windows 桌面右上角显示无线键鼠电量的轻量工具。

**纯 HID 直读，不安装任何厂商驱动或后台服务。** 加一台新设备只需要改一个 JSON 文件。

![screenshot](docs/screenshot.png)

---

## 特性

- **透明文字层** —— 屏幕右上角，背景完全透明，鼠标穿透，不影响任何点击
- **配置驱动** —— 设备协议写在 `devices/*.json`，新增设备不用改代码
- **单文件 exe** —— 约 25 KB，无第三方依赖（只需 .NET Framework 4.x）
- **只读** —— 不写设备、不改设置，仅做查询与监听

## 显示效果

```
        IN10  35%
        KY87  76%
```

- 设备名按电量变色：绿 >50% / 黄 20–50% / 红 <20%
- 百分比为浅白色，带淡黑描边，浅色壁纸下也清晰
- 固定右上角、置顶、不可拖动、鼠标穿透

## 快速开始

```
BatteryMonitor\
├── BatteryMonitor.exe
├── devices\
│   ├── in10.json
│   └── ky87.json
└── install-autostart.bat
```

1. 双击 `BatteryMonitor.exe`
2. 需要开机自启：右键 `install-autostart.bat` → 以管理员身份运行

退出：任务管理器结束 `BatteryMonitor.exe`（界面鼠标穿透，没有可点按钮）。

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
| IN10 | `1D57:FA65` | `col03` | 被动读 5 字节 |
| KY87 | `0C45:FEFE` | `mi_03` | 查询 33 字节 |

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

生成发布包（编译 + 打包成 zip）：

```bat
package.bat
```

产物在 `dist\BatteryMonitor-v1.0.0.zip`，包含 exe、devices、文档和脚本。

## 目录结构

```
src/
  Profile.cs        设备描述模型 + JSON 加载
  HidDevice.cs      通用 HID 打开/读写
  DeviceWorker.cs   按 profile 驱动设备（被动 / 查询）
  WidgetForm.cs     透明文字层
  DebugLog.cs       --log 支持
  Program.cs        入口
devices/
  *.json            设备描述
  device.schema.json  JSON Schema（编辑器自动补全）
docs/
  ADD_DEVICE.md     如何适配新设备
  screenshot.png    界面截图
USAGE-zh.txt              中文使用说明
install-autostart.bat     注册开机自启
uninstall-autostart.bat   取消开机自启
```

## 已知限制

- 键盘休眠时会读不到，保留上次数值；按键后自动恢复
- 查询类设备默认 30 秒轮询一次
- 仅 Windows（依赖 setupapi / user32）
- 需要 .NET Framework 4.x（Windows 10/11 自带）

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

A tiny overlay for Windows that shows wireless mouse and keyboard battery levels
in the top-right corner of the screen.

- **Direct HID access** — no vendor drivers, no background services
- **Config-driven** — device protocols live in `devices/*.json`; supporting a new
  device is a JSON edit, not a code change
- **Single ~25 KB exe** — only needs .NET Framework 4.x
- **Read-only** — it never writes to the device

Build with `build.bat`, package a release with `package.bat`.
See `docs/ADD_DEVICE.md` for adding devices (capture, locate the battery byte,
write a profile).

Not affiliated with any hardware vendor. See `DISCLAIMER.md` before use.
Licensed under MIT.
