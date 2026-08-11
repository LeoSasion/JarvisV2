# Roadmap

> Current runtime policy is defined by `AGENTS.md`. Historical `build-only` and
> `liveExplorer=not-run` labels remain evidence facts; later live validation
> must independently satisfy the current automated preflight.

## M1 — Native Taskbar

源码、精确兼容门禁、一次性许可、固定工具链基线和静态约束已经完成。Phase 2 又完成四组严格受限的离线安全切片：GIT cookie 的显式可重试生命周期与 provisional quarantine；不依赖 HWND 的 generation-scoped UI 线程注册表及事务式初始化/清理；以固定 slot、稳定 ID/epoch/generation、独立 observer 和真实能力引用实现的跨线程 claim-or-cancel 派发收据；以及不加载 Explorer 的便携故障注入实验室。single-generation init CAS、创建前 reserve 的 typed kernel ledger、成组保留 registry wait bundle、TAP lifecycle drain、active/retired watcher、callback scope 和 safety pin 约束继续保留。

便携实验室已经用 `git / ui-thread / dispatch / module` 四域的 90 个确定性场景注入 GIT revoke/registration/retired-owner、UI 初始化/线程复用/多窗口/销毁失败/清理、dispatch timeout/target-exit/unhook/emergency-slot/protocol-publication/late callback、ABI/loader/kernel capability 等失败。三次最终运行均为 90/90、场景负载一致，332 个逐项资源全部以 release 或 reasoned retain 收口，`retainedUnexplained=0`、`doubleRelease=0`；收据仍固定 `releaseReady=false`、`activationPermitted=false`、`liveExplorer=not-run`。这把共享状态机的离线证据从静态匹配推进到可重复执行，但没有调用真实 COM apartment、DispatcherQueue、Windhawk hook、XAML 或 Explorer 关闭路径。任何 Windhawk detour callback 或其他外部入口一经发布，独立 HMODULE safety pin 仍永久保留到 Explorer 自然退出；不可确认的能力在最终 seal 中标为 retained/unreachable，而不冒充已恢复。因此 M1 仍为 **build-only**；`compatibility.json` 的 blocker、Supervisor allowlist 和激活资格均不变，本轮没有实机加载 `jarvis-native-taskbar`。

进入 allowlist 前还必须完成：

- 在正确的真实 UI 线程验证同步撤销已应用属性和资源，不依赖当前 HWND 枚举，也不把 retained/unreachable 当成 restored；
- 在单模块、一次性授权的真实宿主实验中验证 GIT apartment 代理、DispatcherQueue、`WH_CALLWNDPROC`、目标线程退出和 unhook 失败；每个注入点只执行一次，异常立即 re-arm；
- 为所有实际排队的 Dispatcher、CoreDispatcher、线程池和属性 callback 接入生产收据，并证明最终 seal 与模块 pin 决策一致；
- 证明撤销线程、Windhawk Hook 生命周期与 Explorer 关闭过程不会互锁，并为重复 Arm、停用和新的 Explorer 生命周期生成专门收据；
- 完成前不取消“发布外部回调后永久保留 pin”的保守策略，不把离线 90/90 作为 allowlist 或激活依据。

之后的受控实机验收包括：

- 开始按钮、Win 键、任务按钮、拖动排序、缩略图、跳转列表和托盘交互；
- 100%、125%、150%、200% DPI，双屏和不同缩放组合；
- 自动隐藏、全屏应用、睡眠唤醒和 Explorer 重启；
- 连续 25 次 Explorer 重启；
- 一小时空闲 CPU、私有工作集和事件率基线；
- 禁用、卸载和 `disabled.flag` 恢复后零视觉残留。

## M2 — Taskbar behavior

第一个离线切片已经完成：`jarvis-taskbar-icon-size` 只保留 GPL-3.0 `taskbar-icon-size` 的现代单符号图标尺寸路径，默认关闭，stock 值 24，边界 20-32。完整上游实现涉及三十多个私有 Hook、内存常量改写、opcode scanner、托盘/搜索和多屏几何；这些没有进入当前模块。当前它是 Supervisor 唯一 allowlisted 候选，但仍未获得本轮实机授权，也没有被加载。

M2 已具备：真实 Shell PID 绑定、精确磁盘和映射映像身份、5 分钟逐模块一次性许可、后台急停监视、热路径纯原子 pass-through，以及 Explorer 重启后无许可自动复载阻断。离线门禁通过不等于实机稳定性通过。

Phase 4 在锁定态增加了短时 session plan、默认 inert 的恢复终端入口和只读观测演练。计划把 readiness、canonical build、M2 源码、恢复入口和观测器的 SHA-256 绑定到唯一 run ID；任何源码漂移或过期都会拒绝继续。观测演练分别保留真实宿主快照和内存评估副本，可以无副作用地注入 kill switch、permit、Windhawk service、Explorer PID、module mapping 和 CPU 六类漂移，并要求每类都触发明确 stop reason。它没有打开恢复终端、创建许可、启动 Windhawk、清除急停或加载模块，因此仍不是实机稳定性证据。

Phase 5 把恢复终端从启动瞬间的 PID 快照升级为一秒心跳、四秒 managed 失效的短租约。安全审查修复了心跳写入 state root 会触发原生 watcher 的 P0 冲突：lease 现在位于非递归 watcher 下的 `JARVIS2\Recovery` 子目录；M2 自身每秒轮询，超过六秒即永久 pass-through。Supervisor 在写许可前和删除急停前两次验证终端 PID/启动时间、计划哈希/过期时间、固定源码身份、reparse boundary 和当前 Release 程序。七场景离线故障/路径隔离实验室全部通过，但不会写真实状态目录，也不执行恢复或激活。

2026-07-27 的受控会话随后证明：即使 M2 保持禁用，Windhawk 服务仍会把基础运行库映射进 Explorer 和大量非目标进程。会话在清除急停前终止，正常恢复保持 Explorer PID 稳定且 M2 映射为 0。Phase 6 因此隔离整个 Windhawk 服务宿主：readiness 固定失败，控制器不再具备启动服务或把 M2 设为启用的可达路径，Supervisor 也在状态锁之前拒绝 `clear-kill-switch`。

[ADR-0001](ADR-0001-EXPLORER-ONLY-HOST.md) 已从 Windhawk 官方源码确认 service → engine → all-process injector 链路，并把未来候选收窄为 Shell 窗口绑定的单一非零线程 ID；它不是实机授权。Phase 18 已交付独立 bridge ABI 与 callback ownership/quiesce 核心；Phase 19 把一个预先审查的 HWND/PID/非零 TID 绑定到 `WH_CALLWNDPROC` 传输状态机；Phase 20 进一步交付磁盘态空 body callback DLL，并用共享、非执行 PE 数据段让 controller 与目标回调看到同一 bridge 状态。当前仍无 collector、loader、消息/视觉处理或 Explorer 连接。下一步必须把 exact-target collector admission、私有模块路径/DACL、模块哈希、恢复终端与一次性许可组合成新的精确审批包；在此之前继续固定 `activationPermitted=false`、`liveExplorer=not-run`、`mutationPerformed=false`。

在单符号切片完成独立实机稳定性验证之前，不增加任务栏高度、按钮宽度、badge、overflow、托盘或搜索联动。验收必须一次只加载一个 Explorer 模块，并覆盖 100%-200% DPI、双屏、自动隐藏、应用启动/关闭、睡眠唤醒、卸载与急停恢复。任何一项异常都回到 pass-through，不把第二项功能叠上去。

## M3 — Start and system surfaces

为 `StartMenuExperienceHost.exe` 和 `ShellExperienceHost.exe` 分别建立模块。先做 Start、通知中心、音量/亮度 Flyout 的原生样式，再做行为变更。每个宿主单独急停，禁止全局注入。

## M4 — Explorer chrome

改造文件资源管理器标题栏、导航区和上下文表面。ExplorerBlurMica 可作为 LGPL/GPL 研究对象，但不直接把其配置或二进制混入 M1。

Phase 11–15 已把精确单窗口传输、只读 TAP 外壳、一次性 admission/fingerprint、属性投影和严格逆序恢复建成离线模型。Phase 16 进一步增加了一个针对真实 `IXamlDiagnostics` / `IVisualTreeService2` 接口的独立只读 review object：它只编译、不链接、不执行，便携策略核以 56/56 合成外部调用场景验证本地值来源、精确 `SolidColorBrush` 类型、数组释放和 COM 引用收口。Phase 17 又把视觉树事件收敛为固定容量的三表面唯一发现：512 个句柄、2,048 个事件、64 层祖先上限，58/58 合成拓扑场景通过；真实 `IVisualTreeServiceCallback2` 仍只编译为独立对象，不订阅、不链接、不执行。新鲜主机 review package 已能只读确认 23/23 兼容、急停、许可、Windhawk 服务和 Explorer 映射基线，但会因精确 `C:\` 窗口、visual-tree generation、既有 consumer、恢复终端、链接和控制器六项缺口固定阻断，也不生成命令。现有 TAP 仍在 `SetSite` 返回 `E_ACCESSDENIED`；下一步是审查 connectable 的单窗口只读控制器，获得当次明确批准前不连接 Explorer。

## M5 — DWM laboratory

独立、默认关闭的 DWMBlurGlass 研究分支。只有在已有 dump 捕获、符号缓存、Safe Mode 恢复和多版本 CI 后才允许加载到 `dwm.exe`。DWM 故障不能拖入任务栏稳定分支。

## 长期边界

eDEX-UI 继续只是视觉参考。若未来增加命令中心，优先使用原生窗口、Windows App SDK 或独立普通窗口；它可以成为应用，但不能成为遮盖桌面的壳。
