# Architecture

> Current runtime policy is defined by `AGENTS.md`: bounded live validation has
> standing authorization only after exact-target, identity, kill-switch,
> one-module and recovery preflight checks succeed.

## Independent desktop agent boundary

`Jarvis.ControlCenter` is an ordinary own-process WPF application. Its
portable bootstrap resolves a bundled Node executable and exact Pi sidecar,
then creates one current-user model broker, one root-confined in-memory Pi
session and one immutable desktop conversation. Local diagnostic mode is the
default; production mode is an explicit `OpenAiResponsesModelProvider`
selection. The production key is stored with CurrentUser DPAPI and used only
by desktop HTTPS. The broker pipe and offline sidecar never receive it.

Both modes expose only `read`, `grep`, `find`, `ls` and the non-writing
`propose_edit` / `propose_patch` / `propose_create_file` /
`propose_change_set` tools. Only the desktop
owner can apply one exact replacement, one 2–8-hunk exact non-overlapping patch
to a single existing UTF-8 file, exclusively create one reviewed UTF-8 file
beneath an existing parent, or accept one ordered two-to-four-file durable
change set. The optional reviewed loop requires a fresh
one-shot write approval plus fixed repository validation for every write. It
then pauses for a separate desktop-owner approval of the exact Node test profile
pinned in the clean Git HEAD. The desktop executes that profile directly without
a shell and reruns the repository gate afterward; only both passes may continue
reasoning. Pi receives neither process nor validation-approval authority. This desktop agent boundary
has no shell injection, Windhawk activation, Explorer lifecycle, registry or
generic mutation capability. See
`PI-AGENT-OPENAI-RESPONSES-PROVIDER.md` and
`JARVIS-CONTROL-CENTER-PORTABLE-RUNTIME.md`.

The no-argument desktop entry point contains an in-process session launcher.
It admits one canonical local workspace, one explicit provider and the fixed
portable/developer runtime before constructing the broker. Local diagnostic is
the default and starts no network request. The same Control Center window then
transitions from the idle command surface into the Pi conversation; no child
shell, helper console or second desktop process is used.

Successful in-app launches update a separate CurrentUser-DPAPI recent-session
catalog containing at most eight workspace/provider/time hints and no model or
conversation data. The launcher displays the three newest hints, re-admits the
current path and complete portable runtime, and only then starts the same owned
runtime. Conversation checkpoints and reviewed-iteration receipts remain
separate workspace-bound stores; the launch catalog holds no approval
capability.

## 原生优先边界

JARVIS2 的“桌面”不是一个新的顶层窗口。下列 M1 链路描述的是模块进入目标进程后的内部安全契约，不再代表获准使用 Windhawk 服务作为传输宿主。2026-07-27 的受控会话证明该服务会把基础运行库注入 Explorer 和非目标进程，因此整个服务宿主已被 Phase 6 隔离。

M1 的模块内链路是：

1. 模块元数据只接受 `%SystemRoot%\explorer.exe`；这不能限制 Windhawk 基础运行库自身的全局注入范围。
2. `Wh_ModInit` 的第一项操作以 CAS 认领当前 DLL 映射唯一的初始化 generation；重复进入会不可逆转为 `Quiesced`，不能建立第二代。随后模块从 Windows LocalAppData Known Folder 解析固定状态目录，并以 0 ms 等待同一个命名状态门；状态门繁忙就立即失败关闭，绝不阻塞 Explorer 启动。
3. 模块先检查 `disabled.flag`，再独占读取与自身 ID 精确匹配的 `active-module.txt` 一次性许可。
4. 模块核对 Windows build/UBR、精确产品版本，以及 Explorer 和 `Taskbar.View.dll` 当前映射映像的 PE timestamp、`SizeOfImage`、CodeView GUID/age。`Taskbar.View.dll` 的模块引用在整个生命周期内固定。
5. 全部门禁通过后，模块原子消费许可；仍持有 StateGate 时先把 activation state 置为 `Authorized` 并启动状态目录 watcher。许可已经在 watcher 启动前消费，因此此后任何目录文件名变化或 watcher 失败都被保守视为急停，并把 `Authorized` / `Active` 不可逆锁进 `Quiesced`。
6. 初始化 Hook 全部排队成功后，只有一次 `Authorized` → `Active` 的 CAS 可以提交激活。CAS 失败时保持静默，不能用普通 store 覆盖 watcher 已写入的 `Quiesced`。
7. `IVisualTreeServiceCallback2` 事件驱动地发现原生 XAML 元素，并修改 Dependency Properties；callback 对象使用 C++/WinRT 默认 agile 实现，公寓绑定的 XAML Diagnostics 服务只保存在 COM Global Interface Table 中，Advise、Unadvise 与句柄解析均在当前公寓取得代理。初始化、Visual Tree 回调、属性传播以及排队异步 setter 都只读原子 activation state，非 `Active` 时不产生新工作。
8. 模块在启动后台 watcher 前取得自身 HMODULE pin。`BeforeUninit` 先关闭 TAP/GIT admission 并锁进静默，有界等待已经接纳的注入、SetSite、Advise worker、Visual Tree 回调、GIT lease 和 UI-thread dispatch。`Uninit` 对 GIT revoke、全部 active/retired watcher、UI 线程注册表和临时 hook 做有界清理；GIT cookie、线程能力和 hook 能力都只有在对应外部操作确认成功后才释放或清空。
9. UI 清理不再依赖卸载时重新枚举当前 HWND。成功初始化的线程以 activation generation、线程身份、最小权限线程句柄、agile DispatcherQueue、shutdown token 和角色位登记；清理要么在当前登记线程直接执行，要么通过登记的 DispatcherQueue 派发，并生成 restored、retained、unreachable 或迟到完成收据。跨线程 window-hook 派发以固定 slot 持有真实资源，使用稳定 dispatch ID、operation epoch 与 protocol generation 防 ABA；observer 只在槽内身份成立时取得独立 AddRef，锁内仅做 claim/协议转移，降级、回执、pin、日志与 USER32 调用都在锁外完成。
10. 任何 Windhawk detour callback 或其他外部入口一经发布，当前离线版本就不可逆地保留独立 HMODULE pin 到 Explorer 自然退出。任一 drain、GIT revoke、hook 移除、迟到 callback 或 UI 清理未确认时也保留 pin；这保证未知回调不会落到已卸载代码，但意味着停用后 DLL 可能继续映射，且同一 Explorer 生命周期不能重新激活 M1。许可已经被消费，因此 Explorer 意外重启不会自动再次加载实验 Hook。

这条链路不创建 JARVIS2 可见顶层窗口，不接管鼠标命中，不复制任务栏按钮状态，也不轮询窗口列表。开始菜单、托盘、缩略图、跳转列表、拖动排序和辅助功能仍由 Windows 原生组件实现。

## 组件

### Native mod

`mods/windows11/jarvis-native-taskbar.wh.cpp` 是固定上游版本的 GPL-3.0 分支。M1 保留成熟的 XAML Visual Tree 引擎，但增加：

- 精确宿主路径和 x64 架构限制；
- 编译期写死、不可在设置中绕过的兼容基线；
- 独立于 Windhawk 配置的急停文件和逐模块一次性许可；
- 不信任 Explorer 继承的 `LOCALAPPDATA` 环境变量；
- 验证磁盘指纹之外的当前映射 PE/CodeView 身份；
- 不启动继承的主题统计网络请求；
- 只使用纯色本地资源的 JARVIS2 主题；
- 在 StateGate 内启动一次性状态目录 watcher，并让任何后续目录文件名变化或 watcher 失败不可逆停止新工作；
- 用只读原子 guard 覆盖初始化、回调、属性传播和异步 setter，以 CAS 防止 `Quiesced` 被激活提交覆盖；
- 对 active/retired VisualTreeWatcher 使用互斥保护、worker 自持引用、有界 join 和可检查的 Unadvise；
- 用显式 `empty → registered → revoking → retained/revoked` 协议管理 GIT cookie；revoke 失败保留 cookie 供有界重试，provisional 注册回滚失败进入固定 quarantine，并确保代理先于 lease 释放；
- 用 activation generation 和线程创建时间核对每个 UI 线程能力，以 reserve/commit/rollback 事务覆盖初始化，以单一总期限、一次有界重试和最终 seal 覆盖当前线程、DispatcherQueue、线程退出及迟到完成；
- 用固定 dispatch slot、稳定 dispatch ID/generation、真实 callback 引用与 emergency hook slot，把 claim-or-cancel、timeout、target-exit、unhook 重试、late/duplicate callback 和容量溢出写入机器可读收据；
- 用固定容量 typed kernel ledger 在外部创建前预留 slot，以 `Empty / Reserved / LiveLocal / Retained`、精确 owner ticket 和单次 disposition 管理线程句柄、事件、注册表 key、wait、hook、semaphore 与 mutex；registry wait 的 key/event/wait/context 作为一个依赖 bundle 提交或整体保留；
- 对 element customization 与 per-VSG 状态使用共享所有权，并把 Dependency Property、VSG、layout、颜色、图像和系统事件等长生命周期回调纳入 callback scope；XamlBlurBrush 重新连接必须再次通过 Active gate，注册表 wait 用进程期 weak-context 消除注销失败后的 raw-this 与 pending-handle 风险，Win32 wait callback 和 CreateWindow 后处理以 `noexcept` 异常防火墙失败关闭；
- 用 accepting + operation/callback in-flight 计数 + condition variable 组成 TAP 生命周期闸门，卸载前后有界 drain；
- 从临时 `HHOOK` 创建时开始跟踪，成功移除才从集合删除，失败在 `BeforeUninit` / `Uninit` 重试；
- 用独立 HMODULE safety pin 阻止未知 COM、线程或 hook 回调进入已卸载代码；pin 保留表示失败关闭，不表示卸载成功。

XAML Diagnostics 同一时刻只能有一个 consumer。M1 不支持与另一个 Taskbar Styler、UWPSpy 的 TAP 或相同机制的调试器并行运行。

M1 当前只允许离线编译。共享协议与便携 harness 已在无 Explorer 的确定性故障注入中覆盖 `git / ui-thread / dispatch / module` 四域的 90 个场景，并检查逐资源 create→release/reasoned-retain、double release、最终 `releaseReady`、`activationPermitted` 及 `liveExplorer=not-run`。三次最终运行均为 90/90、场景负载一致、0 未解释保留和 0 双重释放；独立证据与生命周期审计均为 `P0=0 / P1=0 / P2=0`。这些测试证明的是状态推进、引用归属、typed kernel ledger 和收据模型，不执行也不能模拟真实 XAML Diagnostics COM apartment、DispatcherQueue、`WH_CALLWNDPROC`、Explorer 关闭或 WinRT module lock。它们因此不能证明已应用属性会同步完整恢复、所有宿主回调都能排空，或真实 hook/COM 能力一定可撤销。当前版本在发布任何 Windhawk detour callback 或其他外部入口后仍故意永久保留 HMODULE pin；不可确认的资源在最终 seal 中被标为 retained/unreachable，而不会冒充 restored。为避免短暂 Arm 后旧视觉或悬空回调残留，`compatibility.json` 的 blocker 与 Supervisor allowlist 均保持不变，当前 allowlist 仍只有 M2；本轮没有实机加载 M1。

### Native icon-size experiment

`mods/windows11/jarvis-taskbar-icon-size.wh.cpp` 是独立的 M2 模块，不依赖 M1，也不复用 XAML Diagnostics。它从 GPL-3.0 的 `taskbar-icon-size` 1.3.7 中只保留现代 `TaskbarConfiguration::GetIconHeightInViewPixels()` 思路，实际只有一个私有符号 Hook：

- 默认 `Enabled=false`，即使误导入也不会解析符号；
- 只接受 AMD64、精确 Explorer 宿主路径和精确加载的 Core 包 `Taskbar.View.dll`；
- 在 Hook 前核对 build、UBR、安装类型、产品版本、文件大小、SHA-256 和当前映射 PE/CodeView 身份；
- 拒绝 legacy `ExplorerExtensions.dll`，也不接受相同版本但哈希不同的 SxS Taskbar.View；
- 只有 `Enabled=true` 且即将注册 Hook 时才消费与本模块匹配的一次性许可；设置变更不能从静默态恢复或注册新 Hook；
- 只改变正常图标计算值，保留小图标、任务栏高度、按钮宽度、托盘、搜索和多屏几何；
- 不使用 opcode scanner、对象偏移裸写、`VirtualProtect`、布局刷新消息、计时器或窗口；
- 后台目录变更监视器只负责更新原子状态；Hook 热路径不读文件、不读取环境变量，也不等待锁；
- 运行中发现急停后永久锁进 pass-through，设置变化不能重新激活。

这个模块的 Hook 调用原函数后只做原子状态读取和范围判断。创建急停文件只能让已加载模块停止改写返回值，不能把 DLL 从 Explorer 物理卸载；完整撤销仍由用户在 Windhawk 禁用模块，必要时再显式确认恢复 Explorer。

### Safety supervisor

`src/platforms/windows11/Jarvis.Supervisor` 不注入代码，也不写 Windhawk 配置。它负责：

- 对 OS、Explorer、Taskbar.View、SystemTray、SearchUx 的版本、大小和 SHA-256 做精确检查；
- 通过 `GetShellWindow` 与 `Shell_TrayWnd` 的共同 PID 识别真实桌面 Shell，不按进程名聚合或终止文件夹窗口；
- 逐该 Shell PID 核对实际加载的 Taskbar.View、SystemTray、SearchUx 路径，并确认 legacy `ExplorerExtensions.dll` 未加载；
- 在命名 Semaphore 下原子创建急停文件、撤销许可；任何无法确认的文件状态都按 Unknown 失败关闭；
- 历史设计只在全部指纹匹配时为一个 allowlisted 模块写入严格 ASCII 一次性许可；Phase 6 现于状态门之前固定拒绝 `clear-kill-switch`，因此该写许可/删急停路径不可达；
- 只在急停已开启、调用者显式传入 `--confirm` 时恢复真实 Shell PID；终止或等待异常后仍执行一次有界恢复。

Supervisor 不是常驻 watchdog。M1 的状态 watcher 是等待目录文件名变化的一次性事件线程，不轮询、不恢复激活，也不重启 Explorer；项目没有后台自愈重启，以避免故障循环。

### Explorer host offline model

`src/platforms/windows11/Jarvis.ExplorerHostModel` 是 portable `net8.0` 离线准入模型，不是 loader。它只接受显式 `offline-fixture`，并且源码中没有 P/Invoke、进程枚举、服务、注册表、远程内存或 Hook 安装 API。候选身份必须来自 Shell desktop window 的单一 PID 和非零 TID，并继续匹配会话、Explorer 路径、版本、哈希、架构、启动时间与未来 standalone bridge 哈希。

当前 Windhawk Mod 契约会被模型拒绝。即使 fixture 全部匹配，收据也只产生 `thread-specific-window-hook-review-candidate`，固定 `executionSupported=false`、`activationPermitted=false`、`liveExplorer=not-run` 和 `mutationPerformed=false`。完整机制取舍和未来 ABI 边界见 [ADR-0001](ADR-0001-EXPLORER-ONLY-HOST.md)。

## 急停状态机

| 状态 | `disabled.flag` | `active-module.txt` | 结果 |
|---|---:|---:|---|
| Locked | 有 | 无 | 所有模块在 Hook 前退出；这是默认和恢复状态。 |
| Prepared | 有 | 指定模块 | Supervisor 事务中的短暂内部状态；急停仍阻止加载。 |
| One-shot released | 无 | 指定模块 | 历史设计状态；Phase 6 隔离下 Supervisor 不再允许进入。 |
| Consumed | 无 | 无 | 当前模块可能已运行，但 Explorer 崩溃/重启后没有许可，不能形成自动注入循环。 |
| Re-armed | 有 | 无 | 新初始化全部阻止；M2 已加载 Hook 转为 pass-through，M1 锁进 latched no-new-work。M1 若曾发布外部回调会保留 HMODULE pin 到 Explorer 自然退出；现有 XAML 属性可能仍保留，完整 UI 恢复与安全卸载尚未证明。 |

Supervisor 的 Arm/Clear/Restart 与原生模块消费许可使用同一个 `Local\JARVIS2.StateGate.v1`。恢复命令会长时间持有状态门，所以原生模块只能 0 ms 尝试获取：繁忙即退出，不能让 Supervisor 等 Shell、Shell 又等状态门。

## 模块隔离原则

后续每个高风险宿主必须单独成模块、单独门禁、默认关闭：

- `explorer.exe`：任务栏、托盘和文件资源管理器表面；
- `StartMenuExperienceHost.exe`：开始菜单；
- `ShellExperienceHost.exe`：通知中心和系统 Flyout；
- `dwm.exe`：窗口框架与合成，风险最高。

任何模块都不得使用全局注入作为省事方案。DWM 模块不得与 Explorer 模块共享崩溃域。

## 兼容性状态

兼容状态分为三个不同事实：

- **static passed**：源码结构、许可证、门禁和无覆盖层约束通过。
- **compiled passed**：固定 Windhawk 工具链生成了目标 PE DLL。
- **live passed**：在特定指纹的真实 Windows 会话完成交互、恢复、压力和性能验收。

前两项不能替代第三项。当前 M1 和 M2 都仍是 `liveExplorer: not-run`；当前急停保持 armed，且没有发现任何 JARVIS/JARVIS2 模组配置或加载。2026-07-22 的构建事故确实在主机上留下了系统级 Windhawk 安装，但 Windhawk 引擎存在不等于 JARVIS2 模组完成实机验证，详情见 [安全事件记录](SECURITY-INCIDENT-2026-07-22.md)。
