# BatteryMonitor

在 Windows 桌面右上角显示无线键鼠电量的轻量工具。

**纯 HID 直读，不安装任何厂商驱动或后台服务。** 加一台新设备只需要改一个 JSON 文件。

> 中文文档见下方，English summary at the bottom.

---

## 特性

- **透明文字层** —— 屏幕右上角，背景完全透明，鼠标穿透，不影响任何点击
- **配置驱动** —— 设备协议写在 `devices/*.json`，新增设备不用改代码
- **单文件 exe** —— 约 22 KB，无第三方依赖（只需 .NET Framework 4.x）
- **只读** —— 不写设备、不改设置，仅做查询与监听

## 显示效果

```
        IN10  38%
        KY87  74%
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
└── 安装开机自启.bat
```

1. 双击 `BatteryMonitor.exe`
2. 需要开机自启：右键 `安装开机自启.bat` → 以管理员身份运行

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

## 内置设备描述

| 名称 | 接收器 VID:PID | 接口 | 模式 |
|---|---|---|---|
| IN10 | `1D57:FA65` | `col03` | 被动读 5 字节 |
| KY87 | `0C45:FEFE` | `mi_03` | 查询 33 字节 |

## 构建

需要 Windows 自带的 .NET Framework 编译器：

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe ^
  /nologo /target:winexe /out:BatteryMonitor.exe ^
  src\Profile.cs src\HidDevice.cs src\DeviceWorker.cs ^
  src\WidgetForm.cs src\DebugLog.cs src\Program.cs ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll
```

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
docs/
  ADD_DEVICE.md     如何适配新设备
```

## 已知限制

- 键盘休眠时会读不到，保留上次数值；按键后自动恢复
- 查询类设备默认 30 秒轮询一次
- 仅 Windows（依赖 setupapi / user32）
- x64 与 x86 都可用，但需对应架构的 csc

## 免责声明

本项目的设备协议通过对公开软件的行为观察获得，**与任何厂商无关，不包含厂商代码**。
仅供个人在自己设备上使用。部分厂商的用户协议可能禁止逆向工程，请自行判断合规性。
使用本工具造成的任何问题由使用者自行承担。

## License

MIT，见 [LICENSE](LICENSE)。

---

## English summary

BatteryMonitor is a tiny Windows tray-less overlay that shows battery levels of
wireless mice and keyboards in the top-right corner of the screen.

- Reads HID devices directly — no vendor drivers, no background services
- Device protocols are declared in `devices/*.json`, so supporting a new device
  is a config change, not a code change
- Single ~22 KB exe, requires only .NET Framework 4.x
- Read-only: it never writes to the device

Build with the bundled `csc.exe`. See `docs/ADD_DEVICE.md` for adapting new
devices. Licensed under MIT.
