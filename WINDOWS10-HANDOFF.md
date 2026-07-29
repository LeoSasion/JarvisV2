# JarvisV2 Windows 10 接力入口

这份文件是迁移包在 Windows 10 上的唯一入口。先阅读，再运行命令。

当前包不是 Windows 10 成品，也不包含可加载的 Win10 原生模块。它保留
Windows 11 已完成的离线基础，同时为 Win10 提供独立目录、命名规则、
只读主机盘点和第一条可见开发路线。任何 Win11 私有符号、XAML 选择器或
DWM 材质都不得通过“放宽版本检查”在 Win10 上复用。

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

第一轮只收集身份，不启动 Windhawk、不连接 Explorer、不加载 DLL：

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
3. 在用户明确批准后，复用 `Jarvis.DesktopStyleSession` 做一次有超时和
   回滚的桌面文字色预览。
4. 只读盘点 Win10 `CabinetWClass`、任务栏窗口和实际渲染技术。
5. 根据盘点结果分别实现 Win10 Explorer 与任务栏后端；不得复制 Win11
   的 `Taskbar.View.dll` 私有 Hook 或 `FileExplorerExtensions.*` 选择器。

第一条 Explorer 内容区写入必须重新满足：精确 PID/TID/HWND、原值收据、
单窗口范围、超时、逆序回滚和当次用户授权。

## 命名规则

- 共享项目：保留 `Jarvis.<Feature>`。
- Win10 专属项目：`Jarvis.Win10.<Feature>`，程序集
  `jarvis-win10-<feature>`。
- Win11 现有项目保持原名，避免破坏构建收据和既有审计身份。
- 新模块 ID 必须包含平台，例如 `jarvis-win10-taskbar-*`；不得复用
  `jarvis-taskbar-icon-size`。
- 运行时安全目录和状态门继续使用 `JARVIS2`，除非另做有迁移收据的版本升级。

## 永久安全边界

- `%LOCALAPPDATA%\JARVIS2\disabled.flag` 默认保持存在。
- 不得通过删除门禁、改写哈希或扩大版本区间让 Win11 模块接受 Win10。
- 不启动、安装、配置或启用 Windhawk。
- 不重启、终止 Explorer，不替换系统 DLL，不修改 Shell 注册表入口。
- 离线测试、编译或截图不等于实机原生验证。

## 回到 Windows 11

Win11 代码完整保存在 `src/platforms/windows11/`、`mods/windows11/` 和
`tests/native/windows11/`。回到已验证的 Win11 主机后，先运行：

```powershell
pwsh -NoLogo -NoProfile -File .\scripts\Test-PlatformLayout.ps1
pwsh -NoLogo -NoProfile -File .\scripts\Test-Windows10HandoffPackage.ps1
pwsh -NoLogo -NoProfile -File .\scripts\Test-Project.ps1
```

只有固定兼容档、canonical receipt 和当次主机证据重新一致，才可以讨论新的
只读或视觉验证。迁移到 Win10 不会自动撤销 Win11 的任何安全阻断。

更完整的目录职责和双后端约束见
[`docs/PLATFORM-ARCHITECTURE.md`](docs/PLATFORM-ARCHITECTURE.md)。
