# JarvisV2

[![JarvisV2 CI](https://github.com/LeoSasion/JarvisV2/actions/workflows/ci.yml/badge.svg)](https://github.com/LeoSasion/JarvisV2/actions/workflows/ci.yml)

**JarvisV2** 是 Windows 原生桌面体验实验。当前开发主线是 Win10 x64 上的 Horizon Membrane，自有 WPF 桌面已支持自由布局、可拖动分界线的分区布局、独立 Explorer 会话和只读任务管理器。桌面预览在 Jarvis 自有进程内运行。

[查看当前桌面基线截图](docs/screenshots/jarvis-win10-current-ui-baseline.png)

## 在本机打开桌面预览

在仓库根目录使用 PowerShell 7 和 [global.json](global.json) 指定的 .NET SDK。

```powershell
dotnet run --project .\src\platforms\windows10\Jarvis.Win10.NeuralVoidPreview\Jarvis.Win10.NeuralVoidPreview.csproj --configuration Release -- show
```

## 创作与实现入口

| 工作内容 | 入口 |
| --- | --- |
| 产品定位与视觉语言 | [产品说明](PRODUCT.md)、[设计系统](DESIGN.md)、[Horizon Membrane 交互与视觉规范](docs/WINDOWS10-NEURAL-VOID-OWNED-PREVIEW.md) |
| Win10 桌面与窗口交互 | [WPF 源码](src/platforms/windows10/Jarvis.Win10.NeuralVoidPreview/)、[窗口布局](docs/WINDOWS10-OWNED-WINDOW-LAYOUT.md)、[任务管理器](docs/WINDOWS10-OWNED-TASK-MANAGER.md) |
| 控制中心与 Pi | [会话界面](docs/PI-AGENT-DESKTOP-CONVERSATION-SURFACE.md)、[编辑审阅](docs/PI-AGENT-WORKSPACE-EDIT-APPROVAL.md)、[便携运行时](docs/JARVIS-CONTROL-CENTER-PORTABLE-RUNTIME.md) |
| 平台与视觉契约 | [平台架构](docs/PLATFORM-ARCHITECTURE.md)、[视觉后端能力与证据](docs/ADR-0002-VISUAL-BACKEND-CONFORMANCE.md) |

控制中心使用自有 WPF 界面与 Pi sidecar。默认可用本地诊断模式，连接真实模型时按 [provider 说明](docs/PI-AGENT-OPENAI-RESPONSES-PROVIDER.md) 配置。Pi 开发所需的 Node 与 pnpm 安装步骤见 [桌面宿主说明](docs/PI-AGENT-DESKTOP-HOST.md)。

## 验证

```powershell
pwsh -NoProfile -File .\scripts\Test-PlatformLayout.ps1
pwsh -NoProfile -File .\scripts\Test-PublicationBoundary.ps1
pwsh -NoProfile -File .\scripts\Test-Windows10NeuralVoidPreview.ps1
```

预览门禁覆盖托管 Release 构建、布局与焦点回归、门禁失败报告和渲染。像素验收使用锁定的 Windows 10 运行环境与已批准的 PNG 基线，细节和历史记录见 [设计 QA](design-qa.md)。独立验证进程采样模型时使用 [任务管理器测试](scripts/Test-Windows10OwnedTaskManager.ps1)。

## 平台维护与历史

- Win10 和 Win11 实现分别位于 [Win10 源码](src/platforms/windows10/) 与 [Win11 源码](src/platforms/windows11/)。共享组件位于 [src/common](src/common/)。
- 原生模块、恢复和验收记录见 [架构](docs/ARCHITECTURE.md)、[恢复流程](docs/RECOVERY.md) 和 [路线图](docs/ROADMAP.md)。实时宿主验证遵循 [AGENTS.md](AGENTS.md) 的自动预检，并保留独立收据。
- 普通 Git 克隆可直接使用上面的预览与验证入口。生成的迁移包按 [Windows 10 接力说明](WINDOWS10-HANDOFF.md) 校验。
- 项目采用 GPL-3.0。参阅 [许可](LICENSE)、[第三方声明](third_party/NOTICE.md)、[贡献说明](CONTRIBUTING.md)、[安全报告](SECURITY.md) 和 [公开发布边界](docs/OPEN-SOURCE-BOUNDARY.md)。内部运行时标识为 `JARVIS2`。
