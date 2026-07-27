# JarvisV2

[![JarvisV2 CI](https://github.com/LeoSasion/JarvisV2/actions/workflows/ci.yml/badge.svg)](https://github.com/LeoSasion/JarvisV2/actions/workflows/ci.yml)

JarvisV2 是一个独立于旧版 JARVIS 的 Windows 原生桌面改造实验。它不铺设全屏画布，不用 Electron/WebView 假装桌面，也不替换任务栏的交互模型；模块进入明确的 Windows 宿主进程，修改现有原生组件，并且必须可以失败关闭和撤销。仓库与公开展示名称为 **JarvisV2**，内部运行时安全标识仍为 `JARVIS2`；状态目录、命名状态门、模块 ID 和既有收据不做破坏性改名。

当前完成了 **M1 / Native Taskbar** 的 latched no-new-work、线程生命周期闸门、unload-safety pin，以及 Phase 2 的可重试 GIT、UI 线程注册表、跨线程派发收据和便携故障注入四组离线安全切片；同时保留第一个受控的 **M2 / Native Icon Size** 离线里程碑。M1 基于 GPL-3.0 的 Windhawk Taskbar Styler 引擎，直接修改 `explorer.exe` 中的原生 XAML Visual Tree；这些改动仍只允许 build-only，离线状态机通过不构成真实 Explorer 生命周期中的安全卸载或视觉恢复证明。M2 只 Hook `Taskbar.View.dll` 的一个现代图标尺寸计算，是目前唯一进入 Supervisor allowlist 的候选。eDEX-UI 只提供深色控制台、青色状态线和琥珀告警色的视觉语言，不进入运行时。

## 当前状态

- `jarvis-native-taskbar.wh.cpp`：原生任务栏视觉模块，目标仅为真实桌面 Shell 的 `%SystemRoot%\explorer.exe` / AMD64；许可消费后会在仍持有 StateGate 时启动状态目录 watcher，任何目录文件名变化或 watcher 失败都会把 `Authorized` / `Active` 不可逆锁进 `Quiesced`。
- M1 no-new-work 边界：`Wh_ModInit` 的第一项操作以 CAS 认领本次 DLL 映射唯一的初始化 generation；重复进入会不可逆转为 `Quiesced`，不能建立第二代。初始化入口、Visual Tree 回调、属性传播和排队的异步 setter 都只读原子 activation state；提交激活必须由 `Authorized` CAS 到 `Active`。设置热更新已禁用，任何设置变化只会不可逆进入 `Quiesced`，必须在新的 Explorer 生命周期中重新走一次性许可，不能原地复活；`BeforeUninit` / `Uninit` 对 watcher 使用有界、可重复的 stop。该门不是同步屏障，已经越过原子检查的在途回调仍可能完成当前收尾。
- M1 TAP / GIT 生命周期：关闭 admission 后拒绝新的注入、factory、TAP 建立和代理 lease，并有界等待已接纳工作。GIT cookie 只有在 `RevokeInterfaceFromGlobal` 确认成功后才清除；失败进入 retained 并可有界重试。注册尚未提交时使用 provisional guard，回滚失败则进入固定容量 quarantine、静默并保留 pin，而不是丢失 COM 能力。`VisualTreeServiceLease` 总是先释放代理，再归还协议 lease。
- M1 UI 线程注册表：每次成功初始化都以 activation generation、线程 ID、线程创建时间、最小权限线程句柄、agile DispatcherQueue、shutdown token 和角色位登记；不把 HWND 写入注册表或收据。初始化采用 reserve/commit/rollback 事务，清理在登记线程直接执行或经 DispatcherQueue 派发，并以单一总期限、一次有界重试、迟到完成和最终 seal 区分 restored、retained 与 unreachable。
- M1 跨线程派发：固定容量 slot 持有真实 callback 引用和 hook 能力；稳定 dispatch ID、operation epoch 与 protocol generation 联合校验，避免线程 ID、指针或复用 slot 造成 ABA。observer 只在固定槽证明 context 存活后取得独立 AddRef，锁内只做精确 claim/状态转移；降级、收据、pin、日志和 USER32 调用全部在解锁后执行。超时、目标退出、取消、hook 移除失败、迟到/重复回调和容量溢出都有紧凑收据；只有 callback 或 cancel 一方能取得释放权，无法确认的真实 `HHOOK` 会进入固定 emergency slot 并继续要求 pin。
- M1 内核能力账本：固定容量 typed ledger 在外部 Create/Duplicate/FindFirst 之前先预留 slot，以 `Empty / Reserved / LiveLocal / Retained` 和精确 owner ticket 记录线程句柄、事件、注册表 key、wait、hook、semaphore 与 mutex；每项 close/transfer disposition 只能提交一次。registry wait 的 key/event/wait/context 作为一个依赖 bundle 绑定，无法确认注销时整组保留，避免先关依赖再留下回调。
- M1 fail-safe 映射策略：模块在启动任何后台线程前取得独立 HMODULE pin；任何 Windhawk detour callback 一经发布，或 XAML Diagnostics COM 入口/跨线程 `WH_CALLWNDPROC` hook 已对外可达，就永久保留该 pin，直到 Explorer 自然退出。临时 hook 从创建起被跟踪并在卸载阶段有界重试移除；任何非零 WinRT module lock、残留 hook、在途回调、不可解释的 retained 资源或清理失败同样禁止释放。这里的“保留”是防止 UAF，不代表卸载成功，也不授权重启 Explorer。
- `jarvis-taskbar-icon-size.wh.cpp`：独立的原生图标尺寸实验；默认 `Enabled=false`、默认尺寸 24，只允许 20-32。
- 原生任务栏主题：13 个受限目标，使用纯色画刷；无 Acrylic、远程图片、轮询和覆盖窗口。
- M2 攻击面：1 个私有符号 Hook；不含任务栏高度、托盘、搜索、偏移扫描、常量改写或强制刷新逻辑。
- 兼容门禁：只接受已审计的 Windows `26200.8875`，真实 Shell PID，以及 Explorer、`Taskbar.View.dll`、`SystemTray.dll`、`SearchUx.UI.dll` 的精确加载路径、版本、大小和 SHA-256；原生侧还核对映射 PE 与 CodeView 身份。
- 急停：默认 `disabled.flag` 存在、`active-module.txt` 不存在；M1 和 M2 都使用后台状态目录监视器将已进入运行路径的模块永久锁进静默状态，热路径不做文件 I/O。M2 还每秒检查 `JARVIS2\Recovery` 子目录中的恢复租约，心跳超过六秒即永久 pass-through；该子目录位于非递归 state-root file-name watcher 之下，正常心跳不会触发自己的急停。M1 本轮只证明 latched no-new-work，不等于已恢复现有 XAML 属性。
- 一次性许可：只允许严格 ASCII 模块 ID、无 BOM/换行、5 分钟内有效；在 Hook 前原子消费，Explorer 崩溃重启后不会自动再次注入。
- Supervisor：核对 23 项宿主和逐真实 Shell PID 的加载事实；Arm/Clear/Restart 与原生许可消费共享跨进程状态门，恢复只处理真实 Shell PID。Phase 6 已在取得状态门之前固定拒绝 `clear-kill-switch`，错误为 `live_activation_quarantined`；旧的恢复租约即使新鲜也不能越过该隔离。
- 构建：固定 Windhawk 1.7.3 / Clang 20.1.3 / Python 3.14.3，锁定 8,397 个真实编译输入；当前 canonical run 与逐文件哈希见 [schema v3 构建回执](docs/receipts/native-build-2026-07-22.json)。两个 AMD64 DLL 均为 0 warning / 0 error，但回执始终明确 `activationPermitted=false`、`liveExplorer=not-run`，不能授权加载。
- M1 证据边界：无 Explorer 的便携生命周期实验室覆盖 `git / ui-thread / dispatch / module` 四域的 90 个确定性场景。最终三次运行均为 90/90；逐项账本共有 332 个资源，其中 259 个确认释放、73 个带原因保留，`retainedUnexplained=0`、`doubleRelease=0`，两路独立审计均为 `P0=0 / P1=0 / P2=0`。机器可读收据仍分别报告 `offlineEvidenceReady=true`、`releaseReady=false`、`activationPermitted=false` 与 `liveExplorer=not-run`。它能证明冻结状态机、引用归属、typed kernel ledger 和收据模型在这些注入点的结果可重复，不能证明真实 COM apartment、DispatcherQueue、Windhawk hook、Explorer 关闭顺序或 XAML 属性一定恢复。代码仍保留长期 XAML callback scope、异常防火墙和 remote ImageBrush 禁止重追踪等既有保护；未获准或 drain 未确认时不会执行破坏性 UI 清理。因此此版本继续选择“停用后 DLL 可能安全映射到 Explorer 自然退出，且同一 Explorer 生命周期内不能重新激活”，而不是冒险卸载。`compatibility.json` 和 Supervisor allowlist 保持不变，M1 继续 build-only，本轮没有把它加载进 Explorer。
- 主机安全状态：受控禁用宿主演练已正常收口；`disabled.flag` 保持 armed、`active-module.txt` absent、M2 disabled、Windhawk Stopped / Manual / PID 0，Explorer PID 保持稳定且 M2 映射始终为 0。恢复终端已关闭。停止服务后仍可能有非 JARVIS 的 Windhawk 基础运行库残留到各宿主自然退出，因此仓库不再把“服务停止”表述为“全系统映射已经归零”。
- Phase 3：GPL 开源发布边界和 M2 只读实机准备包已经实现。最终 readiness 为 `readyForExactApproval=true`、Supervisor 23/23、所有可枚举进程中 0 匹配映射，但仍固定 `activationPermitted=false`、`liveExplorer=not-run`、`canExecuteNow=false`；它绝不执行清除急停、启动 Windhawk 或加载模块。完整边界见 [开源发布说明](docs/OPEN-SOURCE-BOUNDARY.md) 与 [M2 受控实机 runbook](docs/M2-CONTROLLED-LIVE-VALIDATION-RUNBOOK.md)。
- Phase 4：增加短时、单模块、源码绑定的 M2 session plan，默认 inert 的恢复终端入口，以及把真实宿主状态和内存故障评估副本分开的只读观测演练。正常路径不得出现停止条件；kill switch、permit、Windhawk、Explorer PID、module mapping 和 CPU 六类模拟漂移必须各自生成 reasoned stop。该阶段只抵达精确人工批准门，仍不打开恢复终端、不清除急停、不加载 M2。
- Phase 5：修复实机观察中“恢复终端曾打开但随后消失”的缺口。可见终端每秒在 `JARVIS2\Recovery` 子目录原子发布短租约；关闭、4 秒无心跳、计划过期、PID 复用、reparse point、计划/源码漂移或运行 DLL 不一致都会阻断许可。M2 原生 watchdog 再以六秒窗口约束解锁后的终端丢失。离线故障与路径隔离实验室 7/7 通过，固定 `stateDirectoryTouched=false`、`activationPermitted=false`、`liveExplorer=not-run`；详见 [Phase 5 长任务](docs/PHASE-5-M2-RECOVERY-LEASE-TASK.md)与[安全审查](docs/PHASE-5-SAFETY-REVIEW.md)。
- Phase 6：禁用宿主演练证明 Windhawk 服务会在 Mod 禁用时仍把基础运行库映射到 Explorer 和非目标进程。readiness、受控控制器与 Supervisor 现已三层固定拒绝旧激活路径。[ADR-0001](docs/ADR-0001-EXPLORER-ONLY-HOST.md)记录上游链路和 Explorer-only 边界；`Jarvis.ExplorerHostModel` 只评估离线 fixture，不含进程、服务、注册表、远程内存、P/Invoke 或 Hook 安装 API。20 项模型回归覆盖精确 Shell PID/TID、零 TID、`dwm.exe`、多候选、会话与签名漂移、Windhawk 残留和当前 Windhawk Mod 契约；所有输出都固定禁止执行和激活。
- Phase 9：新增独立的 `Jarvis.ExplorerFrameModel`，为原生资源管理器标签栏、命令栏和导航栏建立离线 XAML 样式事务。候选选择器只使用明确标注为未实机验证的 fixture，三类属性必须先完整保存原值，部分应用会立即按相反顺序恢复；29/29 故障场景通过。该阶段没有 XAML Diagnostics 连接、进程访问、Hook、P/Invoke 或实机写入，下一步仍须先经过单个新开 `C:\` 窗口的只读发现门。详见 [Phase 9 长任务](docs/PHASE-9-EXPLORER-FRAME-STYLER-TASK.md)。

## 验证与构建

```powershell
pwsh -File .\scripts\Test-Project.ps1
pwsh -File .\scripts\Test-PublicationBoundary.ps1
pwsh -File .\scripts\Test-M2LiveReadiness.ps1
pwsh -File .\scripts\Test-M2RecoveryLeaseLab.ps1
pwsh -File .\scripts\Test-ExplorerHostModel.ps1
pwsh -File .\scripts\Test-ExplorerFrameModel.ps1
pwsh -File .\scripts\New-M2ValidationSessionPlan.ps1 -OutputPath `
  .\artifacts\m2-validation-session-plans\runs\<unique-name>.json
pwsh -File .\scripts\Test-M2ObservationRehearsal.ps1 -SessionPlanPath `
  .\artifacts\m2-validation-session-plans\runs\<unique-name>.json
dotnet run --project .\src\Jarvis.Supervisor -- inspect
pwsh -File .\scripts\Build-NativeMod.ps1
pwsh -File .\scripts\Build-NativeMod.ps1 -Module jarvis-taskbar-icon-size
```

同轮安全修复后的 `Build-NativeMod.ps1` 只接受两个固定模块 ID，并只接受已经预置、验明为 `Portable=1` 的 Windhawk 1.7.3 工具链；构建入口绝不执行 Windhawk 安装器，也不把安装器当作解包器。它还校验 Python 和完整编译输入树。两个源码在同一个空的 run 级暂存目录中编译，验证 AMD64 PE32+、节区/导出边界和具体 Windhawk 导出后，才一次目录重命名发布到不可覆盖的 `artifacts/native/runs/<run-id>/`。schema v3 收据绑定源码快照、DLL、日志、脚本和工具链锁。若预置 portable 工具链缺失或不完整，构建直接失败，不得回退为系统安装。

公开 CI 只验证可发布文件边界、PowerShell/JSON 语法和 Supervisor Release build。它不会下载或安装 Windhawk，也不会用未锁定工具链编译 native module；因此绿色 CI 不能替代本机 canonical 收据，更不能授权 Explorer 实机加载。

2026-07-22 的安全审计发现，旧版 portable 引导曾因 `Start-Process` 的 `/D` 参数额外带引号而把安装器导向默认系统安装位置。安装器退出码为 0，但预期的 portable 编译器并未出现；约 40 秒后 Windows 记录了 Windhawk 服务创建。完整时间线、影响边界和处置状态见 [安全事件记录](docs/SECURITY-INCIDENT-2026-07-22.md)。经用户明确授权，阶段 A 已用上游正常停机路径把服务改为 Stopped / Manual，并让 Explorer 和其他活动宿主卸载基础引擎；当时一个完全挂起的 `ShellExperienceHost.exe` 仍保留惰性 DLL 映射，因此流程没有运行卸载器、恢复或终止该系统进程，也没有重启 Explorer。用户随后自行重启；2026-07-23 15:39 的只读复查确认所有 Windhawk DLL 映射已归零，但没有据此执行卸载或激活。

## 实机边界

当前没有可执行的首次实机验证顺序。Windhawk 服务宿主已被隔离，`StartDisabledHost`、`EnableOnce` 和 `clear-kill-switch` 都必须失败。任何旧文档、旧命令或旧 session plan 都不再构成授权。

未来只有在独立 bridge ABI、便携 native fault lab、只读 collector、单 PID/非零 TID transport 和新恢复设计分别完成审查后，才可以起草新的实机 runbook。届时仍需在当前任务中展示并批准精确二进制哈希、PID、TID 和一次性命令；本 ADR 与离线模型本身不授予任何加载权限。

完整的边界、恢复流程和验收矩阵见 [架构](docs/ARCHITECTURE.md)、[恢复](docs/RECOVERY.md)、[安全事件记录](docs/SECURITY-INCIDENT-2026-07-22.md) 与 [路线图](docs/ROADMAP.md)。

## 许可

项目使用 GPL-3.0。上游版本、哈希、署名和本项目修改见 [第三方声明](third_party/NOTICE.md) 与 `config/upstream-lock.json`。贡献、安全报告和公开文件边界分别见 [CONTRIBUTING.md](CONTRIBUTING.md)、[SECURITY.md](SECURITY.md) 与 [docs/OPEN-SOURCE-BOUNDARY.md](docs/OPEN-SOURCE-BOUNDARY.md)。
