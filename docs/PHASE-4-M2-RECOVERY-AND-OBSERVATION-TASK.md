# Phase 4 — M2 recovery rehearsal and observation preflight

Status: **COMPLETE**

Date opened: 2026-07-24
Target module: **jarvis-taskbar-icon-size**
Live activation in this task: **FORBIDDEN UNTIL A SEPARATE EXACT APPROVAL**

## Goal

把 Phase 3 的静态 readiness 推进为可交接的单次会话计划、恢复终端入口和
只读观测演练。Phase 4 只到达实机加载的最后一道人工门：它可以证明命令、
源码、构建、宿主和停止逻辑相互绑定，但不能替用户批准或执行加载。

## Safety contract

- [x] `disabled.flag` 在全部开发与演练中保持 armed。
- [x] `active-module.txt` 在全部开发与演练中保持 absent。
- [x] 不启动、配置或启用 Windhawk。
- [x] 不执行 `clear-kill-switch`、不加载模块、不重启 Explorer。
- [x] 所有新脚本默认只读；输出只能进入受限且不可覆盖的 artifact 目录。
- [x] 恢复终端入口没有 `-ConfirmOpen` 时必须保持 inert。
- [x] 观测故障注入只修改内存中的评估副本，不修改真实宿主。
- [x] M1 继续 build-only，Phase 4 只处理 M2。

## Work items

### P4.0 — Session contract

- [x] 建立 fail-closed 会话计划 schema。
- [x] 会话计划绑定 readiness、canonical build、M2 源码和全部控制脚本。
- [x] 会话计划使用短时有效期、唯一 run ID 和固定单模块 ID。
- [x] 计划固定 `activationPermitted=false`、`liveExplorer=not-run`。

### P4.1 — Recovery terminal handoff

- [x] 实现默认 inert 的恢复终端入口。
- [x] 打开前重新验证会话计划、有效期和锁定宿主。
- [x] 终端只显示经过审核的 `arm-kill-switch` 命令，不自动执行。
- [x] 启动可见终端必须要求精确 `-ConfirmOpen`。

### P4.2 — Observation rehearsal

- [x] 建立观测演练 schema 和只读采样器。
- [x] 采样器绑定会话计划并验证 Explorer PID、CPU、内存、handle 和 thread。
- [x] 覆盖 kill switch、permit、service、PID、module mapping 和 CPU 停止条件。
- [x] 故障注入必须可重复、无宿主副作用，并生成 reasoned stop receipt。

### P4.3 — Evidence integration

- [x] 把 Phase 4 文件和无激活约束接入 `Test-Project.ps1`。
- [x] 生成一份非覆盖的 locked session plan。
- [x] 运行默认 inert 的 recovery-terminal dry run。
- [x] 运行正常观测演练和全部模拟停止条件。
- [x] 重新生成 canonical all-module build receipt。
- [x] 完整项目门禁通过并重新确认宿主 locked。

### P4.4 — Publication

- [x] 更新 README、roadmap、runbook 和 publication manifest。
- [x] 审查精确 Git diff，不发布 artifacts。
- [x] 提交、推送并等待 public CI。

## Completion rule

Phase 4 只有在正常演练通过、每个模拟故障都触发 reasoned stop、canonical
收据与完整门禁更新、公开 CI 通过并且宿主仍 locked 时才能完成。

Phase 4 完成后仍必须保持：

- `exactCommandApproved=false`;
- `recoveryTerminalAvailable=false`，除非未来任务真实打开并核验终端；
- `canExecuteNow=false`;
- `activationPermitted=false`;
- `liveExplorer=not-run`.

真实 M2 加载属于后续独立任务。届时必须重新生成新鲜 evidence、真实打开
第二恢复终端，并由用户逐字批准 runbook 中的精确命令。

## Execution log

### 2026-07-24 — Task opened

- Phase 3 public baseline and CI were green at commit
  `b387ce2a0709abd0da3be92e0e6cb73b2be3b48e`.
- The opening read-only inspection found a clean `main`, armed kill switch,
  absent permit and no activation authority carried into this task.
- No live or host-mutating action was executed.

### 2026-07-24 — Locked rehearsal complete

- Final session plan `20260724T113034262Z-e43cd249` passed with SHA-256
  `483EEC05A30EC4B578C6BACAA7AD55860F58589F14E2189C06349ED1C1C09A77`.
  It bound compatibility 23/23, canonical run
  `20260724T112901367Z-c246e535`, current M2 source, every control script,
  `disabled.flag=armed`, permit absent and zero module mappings.
- Recovery-terminal dry run passed with `launchPerformed=false`,
  `terminalAvailable=false`, `mutationPerformed=false` and
  `canExecuteNow=false`; no visible terminal was opened.
- Normal observation run `20260724T113059494Z-1ec89991` collected eight
  locked Explorer samples with zero stop conditions. Its receipt SHA-256 is
  `479A4CF1166A02E43B6958E7125E0D956AA471316941B93B6E0B4621D5997E17`.
- All six injected evaluations produced `stop-required` with their exact
  expected reason. Every receipt kept the real host at armed/absent,
  Windhawk Stopped/Manual, zero module mappings and
  `mutationPerformed=false`.
- Canonical run `20260724T112901367Z-c246e535` completed both native modules
  at 0 warnings / 0 errors. Run-summary SHA-256:
  `B852EB4B49D7C65C876C54DD381EAA0E823959256ED0EBE4A75D8FCF71A1888C`.
- Full `Test-Project.ps1` passed 188/188 including the managed Release build.
  It still reports `releaseReady=false` and `activationPermitted=false`.

### 2026-07-24 — Publication complete

- The reviewed change set contained 12 repository files; no artifact, portable
  toolchain, binary or generated build directory was staged.
- Publication boundary passed for 53 public candidates. Commit `6071715`
  was pushed to `agent/m2-recovery-observation`.
- Draft PR <https://github.com/LeoSasion/JarvisV2/pull/1> is mergeable and
  public CI run <https://github.com/LeoSasion/JarvisV2/actions/runs/30090014385>
  passed.
- Final read-only readiness run `20260724T113519084Z-b28096b0` retained
  compatibility 23/23, `disabled.flag=armed`, permit absent, Windhawk
  Stopped/Manual/PID 0, zero module mappings and complete Explorer inspection.
- The phase ended with `recoveryTerminalAvailable=false`,
  `exactCommandApproved=false`, `canExecuteNow=false`,
  `activationPermitted=false`, `liveExplorer=not-run` and
  `mutationPerformed=false`.
