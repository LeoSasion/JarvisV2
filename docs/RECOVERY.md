# Recovery

JARVIS2 不替换系统文件、不修改 `Shell` 注册表项，也不把自己的窗口设成桌面。最坏情况下的恢复目标是：让模块在 Explorer 启动时、注册 Hook 之前退出。当前 Supervisor 只允许 M2 单 Hook 模块进入未来实机门；M1 视觉模块仍为 build-only。

## 当前宿主状态

2026-07-22 的只读审计发现，早期 portable 工具链引导意外在 `C:\Program Files\Windhawk` 产生了 `Portable=0` 的系统安装，并由 Windows 记录了 Windhawk 服务创建。没有发现任何 JARVIS/JARVIS2 模组配置或加载。经用户明确授权，阶段 A 已正常停止 Windhawk 服务、把启动类型改为 Manual，并确认 Explorer 不再映射基础引擎；当时一个完全挂起的 `ShellExperienceHost.exe` 仍保留惰性 DLL 映射，所以流程没有恢复或终止该进程，也没有运行卸载器。

用户随后自行重启。2026-07-24 的[历史只读主机收据](receipts/host-safety-2026-07-24.json)曾确认全系统 Windhawk/JARVIS 映射为 0；同日后续受控实验留下的 `ShellExperienceHost.exe` 基础惰性映射也已自然消失。2026-07-27 02:18（Asia/Shanghai）的[最新只读主机收据](receipts/host-safety-2026-07-27.json)确认 `%LOCALAPPDATA%\JARVIS2\disabled.flag` 存在、`active-module.txt` 和恢复租约不存在、Windhawk 服务 Stopped / Manual / PID 0、Explorer PID 11640 与全系统匹配映射均为 0、Supervisor 23/23 compatible。readiness 只抵达 `readyForExactApproval=true`，不构成激活授权。事故时间线见[安全事件记录](SECURITY-INCIDENT-2026-07-22.md)。

## 正常恢复

```powershell
dotnet run --project .\src\Jarvis.Supervisor -- arm-kill-switch
dotnet run --project .\src\Jarvis.Supervisor -- restart-explorer --confirm
```

第一条命令在跨进程状态门内原子创建 LocalAppData Known Folder 下的 `JARVIS2\disabled.flag`，然后撤销残留的一次性模块许可。第二条命令只处理 `GetShellWindow` 与 `Shell_TrayWnd` 共同指向、会话和映像路径都确认为 `%WINDIR%\explorer.exe` 的真实 Shell PID；它不会按名称终止其他文件夹窗口进程。没有急停文件或没有精确的 `--confirm` 都会拒绝执行。

急停是**加载互锁和运行时静默请求**，不是结束进程的按钮。它保证模块下一次初始化时在注册 Hook 前退出；`jarvis-taskbar-icon-size` 的后台目录监视器在运行中发现急停后，会把唯一 Hook 永久切到 pass-through，Hook 热路径本身不访问文件。

M1 本轮新增的离线路径先以 single-generation CAS 拒绝同一 DLL 映射中的第二次初始化；许可消费后、仍持有 StateGate 时启动状态目录 watcher，任何目录文件名变化或 watcher 失败都会把 `Authorized` / `Active` 不可逆锁进 `Quiesced`。设置变化只会关闭 lifecycle 并静默，不能重读配置或复活。卸载路径关闭 TAP/GIT admission 后，有界等待已接纳的 worker、Visual Tree callback、GIT lease 和 UI-thread dispatch；GIT revoke 失败会保留 cookie 进入可重试 retained，provisional 注册回滚失败则进入固定 quarantine。每个成功初始化的 UI 线程都以 generation、线程身份、最小权限线程句柄、DispatcherQueue 和 shutdown token 登记，不保存 HWND；当前登记线程直接清理，其他线程经登记的 DispatcherQueue 派发。跨线程 window-hook 派发使用固定 slot、稳定 ID/epoch/generation、真实 callback 引用和 emergency hook slot；observer 只在固定槽证明 context 存活后 AddRef，降级、收据、pin、日志及 USER32 调用都在资源锁外。typed kernel ledger 在外部创建前预留能力，registry wait 的 key/event/wait/context 只能成组提交或成组保留。若 drain 或能力释放未确认，破坏性 UI 清理会跳过或标为 retained/unreachable，并保留 safety pin。这些仍只是离线实现，本轮没有实机加载 M1。

上述切片不是完整恢复或安全卸载证明。无 Explorer 的便携故障实验室以 90 个确定性场景覆盖 `git / ui-thread / dispatch / module` 四域的状态机及资源归属；三次最终运行均为 90/90、0 未解释保留、0 double release，但它没有调用真实 XAML Diagnostics、DispatcherQueue、Windhawk hook 或 Explorer 关闭路径。任何 Windhawk detour callback 或其他外部入口一经发布，模块仍不可逆地保留独立 HMODULE pin 到 Explorer 自然退出；非零 WinRT module lock、残留能力、drain 超时或任一清理失败也会保留 pin。不能把“离线 90 场景通过”“已创建急停”“进入 `Quiesced`”“最终收据为 retained”或“pin 已保留”误报成“视觉已经完全还原”“DLL 已卸载”或“真实宿主已验证”；急停不允许被解释为物理卸载。M1 继续 build-only，`compatibility.json` 与 Supervisor allowlist 保持不变。急停文件本身从不终止 Explorer，也不会自动重启或反复拉起 Explorer。

每次受控激活还需要 `%LOCALAPPDATA%\JARVIS2\active-module.txt` 中与唯一模块精确匹配的一次性许可。原生模块在注册 Hook 前消费它；即使 Explorer 随后崩溃，下一次 Explorer 启动也因没有许可而拒绝实验模块，从而截断崩溃/注入循环。取消测试时再次运行 `arm-kill-switch` 即可在保持急停的同时撤销尚未消费的许可。

Phase 5 还要求 `%LOCALAPPDATA%\JARVIS2\Recovery\m2-recovery-terminal.json` 提供可验证的短租约。可见恢复终端每秒更新心跳；Supervisor 在签发许可前与删除急停前各验证一次 4 秒新鲜度、`pwsh` PID 和启动时间、session plan 哈希/过期时间、全部固定源码身份及正在执行的 Release DLL。终端正常关闭写 `closing`，计划到期写 `expired`；强制关闭时 managed lease 最多 4 秒后失效，M2 自身最多 6 秒后永久锁进 pass-through。`Recovery` 子目录使心跳不触发非递归 state-root watcher。心跳不执行 `arm-kill-switch`，不启动服务，也不重启 Explorer；急停仍是 pass-through 请求，不是物理 DLL 卸载。

Explorer 恢复后，在 Windhawk 中禁用 `jarvis-native-taskbar` 和 `jarvis-taskbar-icon-size` 中本次正在验证的那个模块。一次只验证一个 Explorer 宿主模块。不要先清除急停。确认原生任务栏、托盘、Win 键和文件管理器正常后再调查日志。

## Explorer 循环崩溃

从任务管理器“运行新任务”、Windows 恢复命令行或同一用户的其他终端创建急停文件：

```powershell
$stateRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'JARVIS2'
New-Item -ItemType Directory -Path $stateRoot -Force
New-Item -ItemType File -Path (Join-Path $stateRoot 'disabled.flag') -Force
Remove-Item -LiteralPath (Join-Path $stateRoot 'active-module.txt') -Force -ErrorAction SilentlyContinue
```

随后重启 Explorer 或重新登录。模块从 Windows Known Folder API 解析路径，不信任 Explorer 继承的 `LOCALAPPDATA` 环境变量；它会在任何 Hook 注册前看到该文件并返回失败，若路径或状态探测本身出错也同样失败关闭。

## Windows 更新后的行为

累积更新改变 build、UBR 或产品版本后，原生模块自动拒绝加载；Supervisor 也会因版本或 SHA-256 不匹配而拒绝清除急停。这不是故障，而是兼容性合同。

恢复 JARVIS2 需要新建兼容性分支、审计新二进制、更新源代码常量与 manifest、重新编译，并重新执行完整实机验收。项目不提供 `--force` 绕过。

## 禁止的恢复方式

- 不要删除或替换 Windows 系统 DLL。
- 不要用 `taskkill /f /im explorer.exe` 写入无人值守循环。
- 不要让 watchdog 在反复崩溃时自动重启 Explorer。
- 不要清空整个 Windhawk 配置或其他用户的模块。
