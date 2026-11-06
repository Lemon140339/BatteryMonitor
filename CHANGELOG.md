# Changelog

本项目从 v1.0.0 开始记录变更。

## v1.2.0 — 2026-11-06

### 新增

- **五个新协议族**（全部配置驱动，新增设备不用改代码）：
  - **Compx/Nordic 52840**（17 字节，命令 `08 04`，整帧和恒为 `0x55`）：
    ATK、VXE、VGN、达尔优、G-Wolves、Zaopin、Scyrox 等 8 个品牌的接收器 PID
  - **Compx/Nordic 54L15**（64 字节，`08 7D 72 02 00 <link> 07 01`）：ATK Zero
  - **MCHOSE `FF01`**（Feature 报告 `0x11`、整包 XOR `0xFF`）：A7 Pro
  - **A8A4/A8A5 ODM**（64 字节，命令 `0x30`）：名龙星球 MX300 / HJSDZHI S1-V2 等，
    同一 ODM 的 `55…` / `F0…` 两种方言会自动都试一遍
  - **罗技 HID++ 2.0**：Lightspeed / Bolt / Unifying 接收器，读 `0x1000`
    （Battery Level Status）与 `0x1004`（Unified Battery）两个 feature，
    **一套实现覆盖所有型号**，不需要逐型号配置
- **HID 层能力**：Feature 报告读写（`HidD_GetFeature` / `HidD_SetFeature`）、
  `HidD_SetOutputReport` 兜底、按 HID usage 选接口（读报告描述符）、`HidP_GetCaps`
- **配置能力**：`usage` / `protocol` / `io` / `scale` / `minPercent` / `charging` /
  `payloadXor` / `retries` / `wakeDelayMs` / `response.expect` / `checksum.sumTo` /
  `query.variants`（同一族多种方言）

### 变更

- `HidDevice` 暴露 `HidCaps`（三种报告长度），支持按完整路径打开
- `DeviceWorker` 改为按 `protocol` / `io` 分派；查询逻辑抽到 `ProtocolCodec`
  （组帧、校验和、整包异或解码、应答特征校验），可以直接单测
- 新增 `src/ProtocolCodec.cs`、`src/Hidpp.cs`；`test.bat` 增加协议编解码与 HID++ 帧的断言

### 文档

- `docs/ADD_DEVICE.md`：新增三种协议写法的完整说明（Feature 报告型 / 按 usage 选接口 / 多方言 variants）
- README、USAGE-zh.txt：内置设备表、目录结构、协议摘要同步

### 已知限制

- 上述新协议族**尚未在真机上逐一验证**（作者没有对应设备），
  读数不对请带 `--log` 生成的 `battery.log` 反馈
- 罗技只支持 HID++ 2.0；老 HID++ 1.0 接收器（Nano 那代）不支持；
  一个接收器后挂多台设备时只显示第一台能读到电量的
- 暂不支持：Razer、黑爵 AJAZZ、雷柏 VT9、MCHOSE 耳机

### 致谢

新增协议的事实来源（均为 MIT，详见 README「设备数据来源与致谢」）：
InphicBatteryTray、power-island、vxe-monitor、GearPower、mchose-2.4g-battery-tray。
罗技部分参考 Solaar / logiops / Linux 内核（GPL）的协议事实，未复制其代码或设备表。

## v1.1.2 — 2026-11-06

### 新增

- **升级自动清理旧版配置**：随包发布 `devices/manifest.json`，启动时按 `obsolete` 列表
  删除（没有写权限时改为忽略）旧版本遗留的配置文件。用户升级只需要覆盖整个文件夹，
  不用再读"请删除 xxx"的说明

### 修复

- **从 v1.1.0 升级后 IN10 显示两行**：旧版 `devices/in10.json` 在覆盖升级时不会被删除，
  与新的 1D57 家族配置同时匹配同一个接收器。现在启动时会自动清理/忽略这类旧文件

## v1.1.1 — 2026-11-06

### 新增

- **只显示当前接入的设备**：界面只列接收器当前插着的设备——插上自动出现，拔掉立即消失；
  一台都没检测到时显示「未检测到设备」。判据是 HID 接口在不在，不是"最近有没有数据"，
  所以设备休眠不会让行消失（旧值仍按 5 分钟规则保留）
- **一个接收器支持多种型号**：新的 `modelIndex` / `models` 配置让同一接收器下的不同鼠标
  自动显示对应型号名（型号变了会切换），按 10% 档上报的型号自动换算。
  内置 1D57 无线接收器家族（`devices/beken-1d57.json`，48 个型号 ID：INPHIC IN10/IN9/IN6…
  与 Attack Shark X11/X3/X6/R1 等），IN10 不再单独占一个配置文件

### 变更

- 配置字段：`pid` 可换成 `pids`（一组接收器 PID）；新增可选 `modelIndex` / `models`
  （`devices/device.schema.json` 与 `docs/ADD_DEVICE.md` 已同步）

### 文档

- 设备数据来源与致谢：`attack-shark-x11-driver`、`mouse-battery-tray`（均为 MIT）；
  OpenMouse 仅作交叉核对，未复制其 AGPL 代码或数据

### 修复

- **显示模式不记忆**：托盘菜单里选的「自动 / 总是显示 / 只留托盘」每次启动都会回到「自动」，
  开机后还得重选一次。现在选择会立即保存到 `%APPDATA%\BatteryMonitor\settings.json`，
  下次启动沿用；文件缺失或内容损坏时按「自动」处理，不影响启动。
  设置放用户目录而不是 exe 旁边：程序装在 `Program Files` 时普通权限写不进去
- **点桌面会误弹悬浮层**：全屏判据只看「前台窗口矩形是否铺满屏幕」，而桌面窗口
  （`Progman` / `WorkerW`）的矩形本来就是整块屏幕，于是点一下桌面、按 Win+D
  都会把悬浮层叫出来。现在先把桌面 / 任务栏 / 任务视图这类外壳窗口排除，再判断全屏
  （`src/ShellWindowRules.cs`，`test.bat` 覆盖）

## v1.1.0 — 2026-11-03

### 新增

- **托盘图标**：不再只能盯着右上角。平时待在托盘里，**一台设备画一个电池**
  （竖着排，各自按电量着色：绿 >50% / 黄 20–50% / 红 <20%），
  鼠标悬停看每台设备的百分比。图标是 GDI+ 现画的，不新增任何资源文件
- **右键菜单**：显示模式（自动 / 总是显示 / 只留托盘）、退出
- **全屏自动浮出**：全屏看视频或无边框全屏游戏时，右上角悬浮层自动出现；
  回到普通桌面自动隐藏。判定用「前台窗口是否铺满显示器」，
  不用 `SHQueryUserNotificationState`（实测这台机器会误报 `QUNS_BUSY`）
- **按需提权**：启动时先真的注册一次托盘，能注册就直接跑（不弹 UAC）；
  注册不了才 `runas` 重启自己一次。用户拒绝也能用，自动退化成一直接显示悬浮层

### 变更

- 退出方式从「任务管理器结束」变成「托盘右键 → 退出」
- 主界面改为 `ApplicationContext` 驱动：托盘与悬浮层并存，按策略切换
- `WidgetForm` 不再自带 1 秒计时器，改由 `WidgetApp` 统一驱动

### 修复

- **托盘注册失败被当成成功**：WinForms 的 `NotifyIcon.Visible = true` 在
  `Shell_NotifyIcon` 返回 `FALSE` 时既不抛异常也不报错，导致程序以为托盘可用、
  连悬浮层也隐藏，结果是「托盘和悬浮层都看不到」。改用原生 `Shell_NotifyIcon`，
  以返回值为准；注册失败时自动退化成一直显示悬浮层
- 托盘探测提前建窗口导致 `SetCompatibleTextRenderingDefault` 抛
  `InvalidOperationException`（启动即崩）——WinForms 初始化提到入口最前面
- **全屏视频时悬浮层被压在下面**：播放器的全屏窗口自己也是 topmost，
  激活后排到我们前面。现在显示期间每秒 `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)`
  把 Z 序顶回去（不能激活自己，否则会抢焦点）

### 已知限制

- 独占全屏（D3D exclusive）下分层窗口不参与合成，悬浮层看不到；
  托盘也被游戏盖住，要切出来才有
- Windows 11 把新托盘图标默认收进「^」溢出区，需要手动拖出来
- **Windows 11「媒体播放器」全屏时完全看不到悬浮层**：它走直通呈现，
  桌面合成层被绕过。实测连不透明的置顶窗口也不会显示，说明与画法无关，
  是系统限制；要覆盖只能做注入，本项目不做。浏览器全屏、VLC/MPV 不受影响

## v1.0.1 — 2026-11-03

### 修复

- **满电量时设备名错位**：数字列原来按实际数值排版，`100%` 比 `77%` 宽，
  会把整行的设备名一起往左推。现在数字列按最宽的 `100%` 定宽，
  设备名与百分比各自对齐到固定列
- **宽度按内容自适应**：设备名长时不再被裁掉（原先固定 170px）
- **电量下标可以是 0**：`battery` 改为可空字段，`battery: 0` 现在合法，
  不会再被当成「没填」
- **被动模式读到 0 字节时忙循环**：改为按失败处理（关闭重开 + 等待），
  不再空转 CPU
- **读取超时后的 overlapped IO 收尾**：超时先 `CancelIoEx` 再等它结束，
  避免内核继续往已失效的 `OVERLAPPED` 或缓冲区写结果
- **32 位系统下的 HID 枚举**：接口详情结构大小按位数取 8 / 6，不再写死 64 位

### 计划

- 托盘版：现在的悬浮层是权限妥协而不是设计约束（见 `docs/DESIGN.md`），
  后续尝试托盘形态，悬浮层保留为降级方案

## v1.0.0 — 2026-11-02

首个公开版本。

### 功能

- **透明文字悬浮层**：屏幕右上角显示设备电量，背景完全透明，鼠标穿透，
  固定位置、置顶、不占任务栏
- **配置驱动**：设备协议写在 `devices/*.json`，新增设备不需要改代码
- **两种读取模式**
  - `passive`：监听设备主动上报的报文（如英菲克 IN10）
  - `query`：主动发送查询帧并解析回复（如 NDIY KY87）
- **校验算法**：支持 `sum8`，可配置区间与写入位置
- **多设备**：屏幕行数随配置数量自动调整，每行独立刷新
- **`--log`**：输出 `battery.log`，记录每次电量变化，便于排查新设备
- **配置校验**：启动时检查每个 JSON，出错时屏幕显示红字提示，
  并写出 `config-error.log` 逐条说明问题
- **JSON Schema**：`devices/device.schema.json`，编辑器自动补全与实时校验

### 内置设备

| 设备 | VID:PID | 模式 |
|---|---|---|
| 英菲克 IN10 | `1D57:FA65` | 被动读 5 字节 |
| NDIY KY87 | `0C45:FEFE` | 查询 33 字节 |

### 文档

- `README.md`：项目说明与快速开始
- `USAGE-zh.txt`：面向使用者的中文说明
- `docs/ADD_DEVICE.md`：适配新设备的完整流程（抓包 → 定位字节 → 写配置）
- `DISCLAIMER.md`：免责声明（非官方、只读、无担保、EULA 提示）

### 已知限制

- 键盘休眠时读不到电量，保留上次数值，按键后自动恢复
- 查询类设备默认 30 秒轮询一次
- 仅支持 Windows，需要 .NET Framework 4.x
- 不提供托盘图标：在桌面外壳以高权限运行的机器上，
  非提权进程无法注册托盘图标（`Shell_NotifyIcon` 返回 `ACCESS_DENIED`）
