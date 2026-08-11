# JarvisV2 Windows 10 接力入口

这份文件是迁移包在 Windows 10 上的唯一入口。先阅读，再运行命令。

当前包不是 Windows 10 成品，也不包含可加载的 Win10 原生模块。它保留
Windows 11 已完成的离线基础，同时为 Win10 提供独立目录、命名规则、
主机盘点和第一条可见开发路线。Win11 私有符号、XAML 选择器或 DWM 材质
可以用于离线兼容性研究，但不得通过放宽版本检查直接载入 Win10 宿主。

## 先确认包

解压后，仓库根目录应同时存在：

- `HANDOFF-MANIFEST.json`：打包时生成的提交、文件大小和 SHA-256 清单；
- `config/platform-matrix.json`：Win10 / Win11 平台边界；
- `src/common/`：经过审查的跨版本候选；
- `src/platforms/windows10/`：Win10 新实现的唯一落点；
- `src/platforms/windows11/`：保留的 Win11 实现；
- `mods/windows10/` 与 `mods/windows11/`：互不复用模块 ID 的原生后端；
- `tests/native/windows10/` 与 `tests/native/windows11/`：平台专属测试。

运行不修改系统的仓库检查：

```powershell
pwsh -NoLogo -NoProfile -File .\scripts\Test-PlatformLayout.ps1
pwsh -NoLogo -NoProfile -File .\scripts\Test-Windows10HandoffPackage.ps1
```

第二条命令会逐个核对迁移包内版本化文件的大小和 SHA-256，并拒绝缺失、
篡改或未列入清单的文件。`Test-PublicationBoundary.ps1` 依赖 Git 索引，
只在完整 Git 工作副本中运行，不是 ZIP 解压目录的门禁。

## 第一次进入 Win10

第一轮先收集身份，不加载 DLL；实时验证必须随后通过 `AGENTS.md` 的自动预检：

```powershell
pwsh -NoLogo -NoProfile -File .\scripts\Inspect-Windows10Host.ps1 |
  Tee-Object .\windows10-host-inventory.json
```

把输出作为人工审查输入，不要直接复制到现有
`config/compatibility.json`。Win10 必须拥有新的、精确到 build / UBR /
Explorer 哈希的兼容档；现有 Win11 档保持原样。

## Win10 第一条开发路线

严格按以下顺序推进，每一步都必须产生可见结果或解除下一步的直接阻塞：

1. 在 `src/platforms/windows10/` 新建只作用于自身进程的原生窗口样式探针。
2. 复用 `src/common/Jarvis.DesktopStyleProbe` 只读确认桌面
   `SysListView32` 身份。
3. 复用 `Jarvis.DesktopStyleSession` 做一次有超时、可回滚并留下收据的
   桌面文字色预览。
4. 只读盘点 Win10 `CabinetWClass`、任务栏窗口和实际渲染技术。
5. 根据盘点结果分别实现 Win10 Explorer 与任务栏后端；不得未经独立兼容档
   证明就复用 Win11 的 `Taskbar.View.dll` 私有 Hook 或
   `FileExplorerExtensions.*` 选择器。

Explorer 内容区写入必须绑定当前源码和产物哈希、一个精确 PID/非零
TID/HWND、原值收据、单模块许可、范围、超时、逆序回滚和恢复助手。

## 命名规则

- 共享项目：保留 `Jarvis.<Feature>`。
- Win10 专属项目：`Jarvis.Win10.<Feature>`，程序集
  `jarvis-win10-<feature>`。
- Win11 现有项目保持原名，避免破坏构建收据和既有审计身份。
- 新模块 ID 必须包含平台，例如 `jarvis-win10-taskbar-*`；不得复用
  `jarvis-taskbar-icon-size`。
- 运行时安全目录和状态门继续使用 `JARVIS2`，除非另做有迁移收据的版本升级。

## 永久安全边界

- 每次实机验证开始时 `%LOCALAPPDATA%\JARVIS2\disabled.flag` 必须存在，
  且不得有陈旧的一次性许可。
- 不得绕过源码/产物哈希、主机身份、版本或 exact-target 门禁。
- 不使用 Windhawk 全局服务注入器；仅允许 JARVIS2 私有 collector 把一个
  已审查模块装入一个精确 Explorer PID/TID。
- 不替换系统文件、不削弱 Windows 安全、不建立无人值守 Explorer 重启循环。
- 只有模块已静默且急停已重新武装时才允许一次恢复重启。
- 离线测试、编译或截图必须与真实注入结果分开标注。

## 回到 Windows 11

Win11 代码完整保存在 `src/platforms/windows11/`、`mods/windows11/` 和
`tests/native/windows11/`。回到已验证的 Win11 主机后，先运行：

```powershell
pwsh -NoLogo -NoProfile -File .\scripts\Test-PlatformLayout.ps1
pwsh -NoLogo -NoProfile -File .\scripts\Test-Windows10HandoffPackage.ps1
pwsh -NoLogo -NoProfile -File .\scripts\Test-Project.ps1
```

固定兼容档、canonical receipt 和主机证据必须由自动预检重新核对。预检通过
后不需要逐次重复人工确认；失败时必须保持急停和失败关闭状态。

更完整的目录职责和双后端约束见
[`docs/PLATFORM-ARCHITECTURE.md`](docs/PLATFORM-ARCHITECTURE.md)。
