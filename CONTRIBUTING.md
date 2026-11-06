# 参与贡献

感谢你愿意帮忙。这个项目很小，贡献方式主要有三种。

---

## 1. 报告问题

用 [Issue 模板](https://github.com/Lemon140339/BatteryMonitor/issues/new/choose) 提交，选择 **Bug report**。

**请务必附上日志**，否则很难定位：

```
BatteryMonitor.exe --log
```

会在同目录生成 `battery.log`，记录每次读到的电量。把这个文件拖进 issue 输入框即可。

如果启动时右上角出现「配置有误 N 项」，同目录还有 `config-error.log`，也一并附上。

---

## 2. 适配新设备

**不需要改代码**，只要加一个 JSON。

完整流程见 [docs/ADD_DEVICE.md](docs/ADD_DEVICE.md)，大致三步：

1. 找到设备的 VID / PID / 接口
2. 抓包定位电量字节
3. 在 `devices/` 下新建 JSON

验证方式：

```
BatteryMonitor.exe --log
```

数值和官方软件一致就算通过。

提交时请说明：设备型号、连接方式（2.4G / 蓝牙 / 有线）、
电量是精确百分比还是分档（比如只有 4 格）。

---

## 3. 改进代码

### 构建

```bat
build.bat
```

需要 Windows 自带的 .NET Framework 4.x。

跑逻辑单测（外壳窗口判定、配置模型/型号识别、接口路径匹配、协议编解码、HID++ 帧）：

```bat
test.bat
```

### 代码约束

**必须保持 C# 5 语法。** 编译器是 Windows 自带的 `csc.exe`
（`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`），
它不支持较新的语法糖。以下写法会编译失败：

```csharp
// ✗ 字符串插值（C# 6）
Console.WriteLine($"{name} = {value}");

// ✓ 用拼接或 string.Format
Console.WriteLine(name + " = " + value);

// ✗ 表达式体成员（C# 6）
public int Value => _value;

// ✓ 写完整的属性
public int Value { get { return _value; } }
```

`?.`、`??`、`async/await`、`var`、LINQ 都是可以用的。

### 依赖

只允许 **.NET Framework 自带程序集**：

- `System.Windows.Forms`
- `System.Drawing`
- `System.Web.Extensions`（JSON 解析）
- `System.dll`、`mscorlib`

**不要引入 NuGet 包或第三方 DLL。** 项目靠“零依赖单文件”这一点保持易用。

### 提交信息

参考现有历史，前缀 + 简短描述：

```
feat: add support for Logitech G304
fix: handle device sleep on query mode
docs: clarify match field in ADD_DEVICE
chore: bump build script
```

### 提交前自查

- [ ] `build.bat` 能编过
- [ ] `test.bat` 能过
- [ ] 改动的设备配置用 `--log` 验证过
- [ ] 文档和 schema 同步更新
- [ ] 删/改了 `devices/*.json` 时，同步更新 `devices/manifest.json` 的 `obsolete`

---

## 免责声明

本项目与任何硬件厂商无关，协议来自对公开行为的观察分析。
提交的配置请只包含协议参数，**不要附带厂商的二进制、安装包或反编译产物**。
