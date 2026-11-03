# 发版流程

版本号规则：[语义化版本](https://semver.org/lang/zh-CN/)

| 变化 | 版本 |
|---|---|
| 新增设备配置、修 bug | `v1.0.1` |
| 新功能（向下兼容） | `v1.1.0` |
| 破坏性变更（配置格式不兼容） | `v2.0.0` |

---

## 清单

### 1. 更新 CHANGELOG

在 `CHANGELOG.md` 顶部加一段，把日期和版本号写实：

```markdown
## v1.1.0 — 2026-11-20

### 新增
- 支持罗技 G304
```

### 2. 本地编译验证

```bat
build.bat
```

编译通过后，**双击跑一次**，确认屏幕上还能正常显示电量。

### 3. 打包（带版本号）

```bat
package.bat v1.1.0
```

产物：`dist\BatteryMonitor-v1.1.0.zip`

> 版本号必须显式传入，漏了 `package.bat` 会直接报错退出（以前默认 `v1.0.0`，容易把新版本打成旧号）。

### 4. 提交并打标签

```powershell
git add -A
git commit -m "release: v1.1.0"
git tag -a v1.1.0 -m "BatteryMonitor v1.1.0"
git push origin main --tags
```

### 5. 在 GitHub 上发布

1. 打开 [Releases → Draft a new release](https://github.com/Lemon140339/BatteryMonitor/releases/new)
2. **Choose a tag** → 选刚推的 `v1.1.0`
3. **Release title**：`BatteryMonitor v1.1.0`
4. **描述**：粘贴 CHANGELOG 里对应那段（可以适当精简）
5. **Attach binaries**：拖入 `dist\BatteryMonitor-v1.1.0.zip`
6. 勾选 **Set as the latest release** → Publish

### 6. 发布后自检

- [ ] Actions 页面最新一次构建是绿的
- [ ] Release 页面的附件大小和本地 zip 一致
- [ ] 把 zip 下载下来解压，双击 exe 能正常显示
- [ ] README 顶部的 release 徽章变成新版本号（shields.io 有缓存，可能延迟几分钟）

---

## 出问题怎么回滚

**Release 发错了**：在 Release 页面点 Delete，然后重新按第 5 步发一次。

**标签打错了**：

```powershell
git tag -d v1.1.0              # 删本地
git push origin :refs/tags/v1.1.0   # 删远端
```

**代码有问题想撤回**：先 revert 提交，再发一个补丁版本（不要删已发布的 tag，别人的下载链接会失效）。

---

## 版本号出现在哪些地方

| 位置 | 是否需手动改 |
|---|---|
| `package.bat` | 否（参数必填，脚本里没有版本号） |
| `CHANGELOG.md` | **是** |
| Git tag | 是（第 4 步） |
| Release 标题 | 是（第 5 步） |
| `README.md` | 否（徽章自动读 tag，打包产物名用 `<版本>` 占位） |

只有 `CHANGELOG.md` 是硬编码的，其余都从 tag 派生或走参数。
