# 如何适配一台新设备

本文说明怎样把一个新的无线键鼠加进 BatteryMonitor，
全程大约 30–60 分钟，不需要写代码，最后只产出一个 JSON。

---

## 0. 先确认：它是不是真的有电量

只有**无线**设备才有电量概念。有线设备不存在这个值。

判断方法：把设备拔掉，看它还能不能工作。

---

## 1. 先排除"标准 HID 电量"

部分设备实现了标准 HID 电量，这种最省事，一行配置就够。

用 PowerShell 检查：

```powershell
Get-PnpDevice -Class HIDClass -PresentOnly |
  Select-Object FriendlyName, InstanceId, Status
```

如果设备在 **设置 → 蓝牙和其他设备** 里能直接显示电量，
大概率支持标准电量，那你甚至不需要这个项目。

大多数国产键鼠用的是私有协议，继续往下走。

---

## 2. 找到接收器的 VID / PID / 接口

```powershell
Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Enum\HID' |
  ForEach-Object {
    $id = $_.PSChildName
    Get-ChildItem $_.PSPath | ForEach-Object {
      $p = Get-ItemProperty $_.PSPath
      [pscustomobject]@{
        ID   = $id
        Name = $p.FriendlyName
        Desc = $p.DeviceDesc
      }
    }
  } | Format-Table -AutoSize
```

你会看到一批 `VID_xxxx&PID_yyyy&ColNN` 或 `&MI_NN`。
其中**带 vendor-defined（厂商自定义）集合**的那个，通常就是配置通道。

记下三样东西：

- `vid` —— 四位的十六进制，例如 `1D57`
- `pid` —— 例如 `FA65`
- `iface` —— 接口路径里能唯一定位的那一段，例如 `col03` 或 `mi_03`

> 注意：很多接收器会同时暴露"键盘+鼠标+厂商自定义"多个集合，
> 别拿错。厂商自定义通常 `UsagePage` 是 `0xFF00` 以上。

---

## 3. 抓一次包

工具：[USBPcap](https://desowin.org/usbpcap/) + Wireshark（或 USBPcapCMD）。

步骤：

1. 安装 USBPcap，重启
2. 开始抓包，抓你接收器所在的根集线器
3. **打开官方驱动软件，让它显示一次电量**
4. 停止抓包

关键：**必须让官方软件真的去读一次电量**，否则抓包里不会有那条报文。

---

## 4. 在抓包里找电量帧

先过滤出你的设备：`usb.device_address == N` 或按 VID/PID。

然后找两类特征：

**特征 A：被动推送**

设备自己周期性发来的短报文，长度固定（常见 4–8 字节），
其中一个字节的数值在 0–100 之间，并且随时间缓慢下降。

**特征 B：一问一答**

先看到一条主机发出的写请求，紧跟一条设备回复。
回复里同样有一个 0–100 的字节。

判断依据：**把设备电量放到 20% 左右再抓一次，对比那个字节是否跟着变。**

---

## 5. 确定电量在第几字节

这是最容易出错的一步。方法：

1. 让设备电量在明显不同的两档各抓一次（例如 80% 和 30%）
2. 对比两次的报文，找出**只有那一个字节在变**
3. 用 `--log` 验证：数值应该和官方软件显示的一致

> 如果整条报文都在变（加密/滚动码），这条路走不通，放弃。

---

## 6. 写 JSON

配置放在 `devices/` 下，文件名随意，按名字排序显示。

### 被动推送型

```json
{
  "name": "IN10",
  "vid": "1D57",
  "pid": "FA65",
  "iface": "col03",
  "mode": "passive",
  "readLength": 5,
  "match": { "1": "0x93", "2": "0x40" },
  "battery": 4,
  "retryDelayMs": 2000
}
```

`match` 用来过滤掉同一条通道上的其它报文（比如 DPI 变化、配置回读），
只保留电量帧。键是字节下标，值是期望值，支持 `0x` 前缀。

### 一问一答型

```json
{
  "name": "KY87",
  "vid": "0C45",
  "pid": "FEFE",
  "iface": "mi_03",
  "mode": "query",
  "battery": 4,
  "query": {
    "length": 33,
    "set": { "0": "0x00", "1": "0x20", "2": "0x01", "3": "0x00" },
    "checksum": { "type": "sum8", "range": [0, 30], "at": 31 }
  },
  "response": {
    "length": 33,
    "checksum": { "type": "sum8", "range": [0, 31], "at": 32 }
  },
  "pollIntervalMs": 30000,
  "retryDelayMs": 5000
}
```

---

## 7. 字段说明

| 字段 | 适用 | 说明 |
|---|---|---|
| `name` | 全部 | 显示在屏幕上的名字 |
| `vid` / `pid` | 全部 | 十六进制，四位 |
| `iface` | 全部 | 接口路径片段，用来区分同一设备的多个集合 |
| `mode` | 全部 | `passive`（被动读）或 `query`（主动问） |
| `battery` | 全部 | 电量字节的**下标**（从 0 开始） |
| `readLength` | passive | 每次读取的字节数，必须等于报告长度 |
| `match` | passive | 帧特征，`{"下标": "期望值"}`，全部命中才算电量帧 |
| `query.length` | query | 发送帧总长度（含报告 ID） |
| `query.set` | query | 固定字节，`{"下标": "值"}`，未列出的补 0 |
| `query.checksum` | query | 发送帧的校验 |
| `response.length` | query | 期望回复长度 |
| `response.checksum` | query | 回复的校验（不填则不校验） |
| `pollIntervalMs` | query | 轮询间隔，默认 30000 |
| `retryDelayMs` | 全部 | 失败后的重试间隔，默认 5000 |

### 校验算法

目前只支持 `sum8`：把 `range` 区间内的字节相加，取低 8 位，写到 `at` 位置。

```json
"checksum": { "type": "sum8", "range": [0, 30], "at": 31 }
```

含义：把第 0 到第 30 字节相加，结果写入第 31 字节。

如果设备没有校验，整个 `checksum` 字段省略即可。

---

## 8. 验证

```
BatteryMonitor.exe --log
```

同目录会生成 `battery.log`：

```
17:04:23  KY87 = 74
17:04:24  IN10 = 38
```

对照官方软件显示的数字。对不上说明 `battery` 下标错了，
或者抓错了通道。

**常见问题**

| 现象 | 原因 |
|---|---|
| 日志里一直没出现你的设备 | `vid`/`pid`/`iface` 不对，或设备在休眠 |
| 数字明显不对但一直在变 | `battery` 下标偏了 |
| 数字偶尔才更新 | 被动型设备推送间隔长，正常 |
| 一直 `--` | 校验没通过，检查 `checksum` 的 `range`/`at` |
| 只出现一次就没了 | 设备休眠，按几下键盘看是否恢复 |

---

## 9. 提交

把新的 JSON 放到 `devices/` 下，在 README 的设备表里加一行，
提 PR 时说明：

- 设备型号
- 抓包时的连接方式（2.4G / 蓝牙 / 有线）
- 电量是精确百分比还是分级（如只有 4 档）

这样别人用同款设备就能直接受益。
