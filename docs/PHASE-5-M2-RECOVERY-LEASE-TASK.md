# Phase 5 — M2 recovery-terminal lease and fail-closed activation task

Repository name: **JarvisV2**

Internal runtime namespace: **JARVIS2**

Module in scope: **`jarvis-taskbar-icon-size` only**

Live activation in this task: **FORBIDDEN**

## Goal

修复 Phase 4 实机观察中暴露的安全缺口：恢复终端曾在模块加载后消失，
但旧入口只证明它在启动后 750 ms 仍存在，无法证明它在签发一次性许可时
仍可用。

Phase 5 把恢复能力从“曾经打开一个窗口”提升为短租约安全能力：

- 恢复终端每秒原子发布一次心跳；
- `clear-kill-switch` 在写许可前和删除 `disabled.flag` 前各验证一次租约；
- 验证必须绑定 PID、进程启动时间、session plan、计划 SHA-256、计划过期
  时间、当前 Release Supervisor 程序以及全部计划源码身份；
- 关闭终端、心跳超过 4 秒、计划/源码漂移、进程复用或计划过期时全部
  fail closed；
- 心跳线程只写租约，不运行恢复命令，不启动服务，不重启 Explorer。

## Non-negotiable safety boundary

- `disabled.flag` 在全部开发和故障注入中保持 armed。
- `active-module.txt` 在全部开发和故障注入中保持 absent。
- 不执行 `clear-kill-switch` 或 `restart-explorer`。
- 不启动、配置、启用或停止 Windhawk。
- 不加载任何 Windhawk 模块，不终止任何 Windows Shell 进程。
- M1 继续 build-only。
- 离线实验室只能把 `--lease-path` 指向仓库 `artifacts/` 中的临时
  fixture；它不得写 `%LOCALAPPDATA%\JARVIS2`。
- `inspect-recovery-terminal` 是只读诊断命令；只有默认租约路径能被真实
  激活流程消费。

## Work items

- [x] 新增 `m2-recovery-terminal.json` schema，限制字段、状态、模块 ID、
  恢复命令和非激活边界。
- [x] 恢复终端改为一秒心跳循环，并以临时文件加原子替换发布。
- [x] 父入口等待真实 `ready` 心跳，不再使用固定 750 ms PID 快照。
- [x] 终端正常退出写 `closing`；强制关闭时最后心跳在 4 秒内失效。
- [x] 计划过期写 `expired`，不自动续期或重新打开窗口。
- [x] session plan 绑定租约 schema 与当前 Release Supervisor DLL。
- [x] Supervisor 验证计划路径、计划哈希、全部固定源码身份及正在执行的
  DLL 身份。
- [x] Supervisor 验证心跳新鲜度、PID、`pwsh` 进程名和进程启动时间，
  阻断 PID 复用。
- [x] `ActivateModuleUnderLease` 在许可写入前及删除急停前双重验证。
- [x] 新增只读 `inspect-recovery-terminal --module <id>` 诊断入口。
- [x] 新增六场景离线故障注入：正常、新鲜度过期、closing、计划哈希
  漂移、进程启动时间漂移和源码身份漂移。
- [x] 安全审查发现并修复 lease 心跳与根目录 watcher 的 P0 冲突；lease
  移到 `JARVIS2\Recovery` 子目录。
- [x] M2 增加一秒轮询、六秒失效的原生 heartbeat watchdog；终端强制
  退出后自动锁进 pass-through，但不重启或终止任何进程。
- [x] managed lease 与离线 fixture 路径增加 reparse-point 拒绝。
- [x] 离线实验增加 non-recursive child-path isolation，矩阵扩展为 7/7。
- [x] 在当前源码上重新生成 canonical native build receipt。
- [x] 运行完整 `Test-Project.ps1` 与 publication boundary。
- [x] 把默认惰性的受控实机控制器纳入 session plan 源码身份；禁用态
  安装切换、启动、单次启用、观察、恢复必须是分离动作，控制器自身
  不能清除急停或重启 Explorer。
- [ ] 在主机自然清除残余基础映射后，生成新的真实 session plan。
- [ ] 另一个任务中重新展示 fresh compatibility/readiness、恢复终端租约
  和精确恢复命令，等待新的明确实机授权。

## Acceptance criteria

1. Release managed build 为 0 warning / 0 error。
2. 六个离线租约场景全部通过，且收据固定：
   `activationPermitted=false`、`liveExplorer=not-run`、
   `mutationPerformed=false`、`stateDirectoryTouched=false`。
3. 离线实验结束后临时 plan 和 lease fixture 均被删除。
4. 默认状态目录没有被实验室写入；真实租约缺失时只读检查返回
   `SafetyInterlock`。
5. `disabled.flag` 仍存在，`active-module.txt` 仍不存在。
6. 没有 Windhawk 启动、模块加载、Explorer 重启或系统进程终止。
7. 任何后续激活都必须重新生成未过期 session plan、打开可见恢复终端，
   让 Supervisor 读到新鲜租约，并获得用户对精确命令的新授权。

## Evidence boundary

Phase 5 只能证明租约解析、身份绑定和故障拒绝在离线 fixture 中按预期
工作。它不能证明可见窗口在未来实机加载期间一定不会被外部因素关闭，
也不能证明 M2 的原生 Hook 稳定。真实观察若发现窗口消失，最后心跳最多
4 秒后失效；已开始的实机观察仍必须立即执行人工 `arm-kill-switch` 恢复
流程。此任务不把离线通过冒充实机稳定性。
