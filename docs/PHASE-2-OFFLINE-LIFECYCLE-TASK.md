# Phase 2 — Offline Lifecycle Proof

Status: **COMPLETE**

Date opened: 2026-07-24
Execution authority: current Codex goal
Live activation: **FORBIDDEN**

## Goal

在不加载 Windhawk 模块、不清除急停、不重启 Explorer 的前提下，把 M1
剩余的生命周期 P2 从“依赖永久 pin 的保守残留”推进到“状态可重试、线程可核对、
资源有收据、失败可重复注入”的离线证据闭环。

本文件是 Phase 2 的唯一任务清单。实现顺序、完成状态、验收证据和遗留问题都必须
回写到这里；不能用口头进度替代勾选项，也不能用静态检查冒充实机证明。

## Safety contract

- [x] `%LOCALAPPDATA%\JARVIS2\disabled.flag` 继续视为 armed。
- [x] 不创建或消费 `active-module.txt`。
- [x] M1 保持 `build-only`，Supervisor allowlist 不加入 M1。
- [x] 不启动、安装、启用或配置 Windhawk。
- [x] 不注入模块，不终止或重启 Explorer，不修改注册表或系统文件。
- [x] 所有编译继续使用已验证的便携工具链，不执行 Windhawk 安装器。
- [x] 阶段结束时重新只读确认上述运行态事实。

## Opening baseline (superseded by the final evidence below)

- M1 source SHA-256:
  `C0C29A2CB33C2DDC87E5B23CD93C9FC575D4F86E35434941AA780BD67FB454C3`
- M2 source SHA-256:
  `4A0278E2BC1CC81D616AC885F87BB51CE26DD044E4F44DDB8341E0C6D79087C4`
- Canonical build run:
  `20260723T101911378Z-d8b9459c`
- Baseline full gate: `96/96`
- Baseline audit: `P0=0`, `P1=0`
- Baseline live state: `activationPermitted=false`, `liveExplorer=not-run`

Phase 2 可以改变 M1 源码和测试，因此上述源码回执会自然过期。只有完成本清单后
生成的新 canonical all-module run 才能成为新的离线基线。

## Residual P2 to close

1. GIT revoke 失败时 cookie 已清零，无法可靠重试；COM 初始化失败也可能留下引用。
2. 最终清理只重新枚举当前 HWND，不能核对所有曾初始化的 UI 线程。
3. `SendMessageTimeoutW` 超时、迟到回调或 hook 移除失败时，资源只被保守保留，
   缺少结构化状态和收据。
4. 关键失败分支已有静态门，但缺少可重复、无 Explorer 的故障注入执行证据。

## Design invariants

- 外部 COM、WinRT、窗口消息和 hook API 调用期间不得持有内部 registry/state 锁。
- 状态只能单向推进；失败后不能伪装成已清理，也不能在同一 Explorer 生命周期复活。
- 所有重试都必须有界、幂等；禁止后台无限循环或 Explorer watchdog。
- 任何无法确认的 callback/hook/thread 状态继续要求 permanent module pin。
- 线程 ID、HWND、cookie、HANDLE 都只是能力线索；每次使用前必须重新验证。
- 记录“不可达/保留”与记录“已恢复”必须是两个不同结果。
- 离线模型只能证明状态机和资源所有权，不能证明真实 Explorer/XAML 行为。

## Work items

### P2.0 — Scope and evidence foundation

- [x] 创建 Phase 2 goal。
- [x] 创建本任务文件并写入 baseline、安全合同和验收标准。
- [x] 在 `scripts/Test-Project.ps1` 中加入本任务文件存在性与关键禁止项静态门。
- [x] 给 Phase 2 新增的测试/回执定义稳定、机器可读的 JSON schema。
- [x] 文档和测试明确区分 `offlineEvidenceReady`、`releaseReady`、
      `activationPermitted`。

Acceptance:

- 全量测试会因缺少或篡改本文件的安全关键句而失败。
- Phase 2 的任何测试输出都明确 `liveExplorer=not-run`。

### P2.1 — Retryable COM GIT lifecycle

- [x] 定义显式 GIT 状态：empty、registered、revoking、retained、revoked；
      empty 与成功撤销后的 terminal 状态不得混淆。
- [x] 定义独立 subscription 状态：not-attempted、advising、advised、
      maybe-advised、unadvising、unadvised。
- [x] `GetInterfaceFromGlobal` 与 revoke 之间建立可核对的并发协议。
- [x] revoke 只有在成功后才能永久清除 cookie。
- [x] revoke 失败后保留 cookie、失败码和 retry eligibility。
- [x] 并发 revoke 只能有一个 owner；其他调用返回可重试状态，不能 double revoke。
- [x] COM apartment 初始化失败时记录 retained 原因并保持 fail-safe pin。
- [x] 析构、Advise 失败、Unadvise 成功/失败使用同一个幂等关闭入口。
- [x] Advise 调用已经开始但返回失败时进入 maybe-advised，并先 best-effort
      Unadvise；不得假定外部服务没有保留 callback。
- [x] watcher 仅允许在 revoked/empty terminal 状态析构；retained/revoking
      必须转移到 retired owner，析构不得启动外部 COM。
- [x] 不在内部状态锁内调用 `CoCreateInstance`、GIT 或 VisualTreeService。
- [x] 为成功、失败、并发、重复、COM init 失败建立离线故障注入用例。

Acceptance:

- 任何失败注入后 cookie 都不会无收据消失。
- 成功 revoke 恰好发生一次；重复 close 是稳定 no-op。
- 固定工具链 M1 编译 0 warning / 0 error。

### P2.2 — Complete UI-thread registry

- [x] 为每个成功初始化的 UI 线程创建 generation-aware registry record。
- [x] record 至少包含 record ID、activation generation、thread ID + creation
      time、仅有 `SYNCHRONIZE|THREAD_QUERY_LIMITED_INFORMATION` 权限的真实
      thread HANDLE、agile DispatcherQueue、状态和最后收据；不得持久保存 HWND。
- [x] HWND 只允许作为局部 bootstrap 线索；进入 `RunFromWindowThread` 前重新
      核对 PID/thread/class，且禁止写入 registry、receipt 或异步 capture。
- [x] `InitializeForCurrentThread` 成功后登记，失败时不得登记为 initialized。
- [x] `UninitializeForCurrentThread` 产生 cleaned receipt，并幂等移除/封存记录。
- [x] 窗口创建/销毁或线程不可达时更新 record，而不是静默丢失。
- [x] 最终清理从 registry snapshot 出发，不再只依赖重新枚举当前 HWND。
- [x] registry 锁外执行 `RunFromWindowThread` 和 XAML cleanup。
- [x] 不可达线程记录 retained/unreachable，并继续 permanent pin，不能报告完整恢复。
- [x] 为重复初始化、窗口替换、窗口消失、thread ID 复用、部分清理建立离线用例。

Acceptance:

- 每个已登记 generation 最终只能是 cleaned 或 retained/unreachable。
- 不存在“曾初始化但完全没有终态收据”的记录。
- 静态门验证 registry snapshot、锁外派发和失败关闭顺序。

### P2.3 — Dispatch ownership and receipts

- [x] 为 `RunFromWindowThreadContext` 增加稳定 dispatch ID 和显式状态。
- [x] 收据覆盖 registered、claimed、invoked、send-timeout、hook-removed、
      callback-late、retained。
- [x] sender/callback 引用释放路径可核对，且任何状态最多释放一次。
- [x] timeout 后禁止新的跨线程派发，并记录 retained 原因。
- [x] tracking push 失败 + unhook 失败时仍保留可枚举的 hook receipt。
- [x] final unload 能输出 pending context/hook 数和各自终态，不只输出布尔值。
- [x] 收据不得包含地址、凭据或用户数据；只保留 ID、状态、错误码和计数。
- [x] 为同步成功、目标 hung、目标退出、迟到 callback、重复 callback、
      hook tracking/unhook 失败建立离线用例。

Acceptance:

- 任一注入场景的引用、context 和 hook 终态都能由 JSON 收据解释。
- 不存在既未释放又未标记 retained 的资源。

### P2.4 — Portable offline fault-injection lab

- [x] 增加独立于 Explorer/Windhawk 运行态的测试入口。
- [x] 测试实现使用可替换操作表或纯状态核心，不依赖真实 COM/GIT/HHOOK。
- [x] 默认执行确定性场景；并发场景使用有界 barrier，不使用 sleep 猜时序。
- [x] 至少覆盖 P2.1、P2.2、P2.3 列出的全部失败场景。
- [x] 每次运行生成 schema-versioned JSON receipt。
- [x] receipt 绑定测试源、相关 M1 源码、测试脚本和工具链哈希。
- [x] 测试失败时退出码非零，且不会写“passed”回执。
- [x] `scripts/Test-Project.ps1` 默认执行 fault lab，不提供静默跳过的发布路径。

Acceptance:

- 连续运行三次得到相同场景集合和相同 pass/fail 结果。
- 所有场景通过且没有未解释 retained resource。
- 输出明确声明 `liveExplorer=not-run`。

### P2.5 — Documentation and static contracts

- [x] 更新 `README.md`、`docs/ARCHITECTURE.md`、`docs/RECOVERY.md`、
      `docs/ROADMAP.md`。
- [x] 文档说明 GIT、UI-thread registry、dispatch receipt 的真实边界。
- [x] 静态门禁止重新出现 clear-cookie-before-success、current-HWND-only cleanup、
      unreceipted timeout。
- [x] M1 blocker 与 allowlist 保持不变。
- [x] 更新任务文件中的完成状态、证据和残余风险。

### P2.6 — Final evidence and review

- [x] 运行 Phase 2 fault lab 三次。
- [x] 运行固定工具链 M1 快速构建。
- [x] 冻结源码并执行至少两路独立只读安全审计。
- [x] 修复所有 P0/P1 后重新冻结和审计。
- [x] 运行 canonical all-module build，生成新 schema v3 回执。
- [x] 运行完整 `scripts/Test-Project.ps1`。
- [x] Supervisor `inspect` 仍全部通过。
- [x] 最终只读确认 kill switch armed、permit absent、Windhawk stopped/manual、
      无 JARVIS/Windhawk 进程和 Explorer 映射。
- [x] 记录 git 状态；不擅自 stage、commit 或 push。

Acceptance:

- 独立审计 `P0=0`、`P1=0`。
- native modules 与 managed supervisor 均为 0 warning / 0 error。
- 完整 gate 全过，但 `releaseReady=false`、`activationPermitted=false`。
- 没有执行 live activation。

## Execution log

### 2026-07-24 — Task opened

- Goal created.
- Baseline and safety scope frozen.
- Repository has no `.codegraph/`; normal source inspection is used.
- Worktree content remains untracked as a whole; no staging or cleanup performed.

### 2026-07-24 — P2.0 completed

- Added `config/offline-lifecycle-receipt.schema.json` with schema version 1.
- Added four Phase 2 gates to `scripts/Test-Project.ps1`; all four passed.
- The focused gate reported `100` total checks and only the four expected stale
  canonical-receipt failures caused by changing the test script.
- The receipt contract fixes `releaseReady=false`, `activationPermitted=false`
  and `liveExplorer=not-run`; `offlineEvidenceReady` remains an independent
  computed field.
- Incorporated three independent read-only design audits. No audit modified the
  repository or touched Windhawk/Explorer.

### 2026-07-24 — First implementation freeze rejected

- Frozen M1 source:
  `A9543DD820F3D14AC1B34358F93318900441DFCFCFFB9611409CD4C9DB81ED46`
- Frozen shared protocol:
  `D4F7E0FA758041903A37DEEAB1B30FDF13CAFEB95B37E278807FE36E88795917`
- Portable fault-lab run:
  `20260723T231024109Z-2c0a8fa8`, `37/37`, `doubleRelease=0`,
  `retainedUnexplained=0`, `activationPermitted=false`,
  `liveExplorer=not-run`.
- Three independent read-only reviews rejected this freeze for completion.
  Blocking findings were: an allocating retired-watcher owner transfer, dispatch
  late/duplicate counters that a later publish could overwrite, and an incomplete
  exception firewall around the Windows hook ABI.
- P2.1, P2.2 and P2.3 deliberately remain unchecked. The freeze was reopened for
  fixes and stronger injected-failure tests; no canonical receipt was promoted.
- These reviews and the rejected fault-lab run were offline only. Windhawk and
  Explorer were not touched.

### 2026-07-24 — Second implementation freeze rejected

- Frozen M1/protocol/harness SHA-256:
  `488CCB19755E0D2F29A8CEBC66D0C9E9688299049517B4FAC5B7CBE2F16D82EB`,
  `B31AA9200B97F4D005956ED3E3AC7A26CDB2B8289045414CF4F00F2E36637437`,
  `DEA478B0B44F405CD2EF2ACF1C8F8EDEFED0F684BCA9F8674BEDD80F8B2180AA`.
- Fixed-toolchain M1 quick build
  `20260723T234754609Z-7b565686` compiled with `0` warnings and `0` errors.
- Three non-overwriting receipts under
  `artifacts/lifecycle-fault-lab/runs/phase2-freeze-488ccb19-0{1,2,3}.json`
  independently proved identical `45/45` scenario payloads with no unexplained
  retained resource or double release. They remained offline-only and explicitly
  denied release, activation and live Explorer claims.
- Independent production-path review nevertheless found five P1 blockers not
  exercised by the pure protocol harness: dynamically allocating GIT lease/reason
  state inside `noexcept` adapters, a partially committed dispatch claim, a UI
  cleanup ticket issued before snapshot allocation could fail, two raw HANDLEs
  lacking pre-allocation RAII, and a dynamically growing vector inside a USER32
  enumeration callback.
- The freeze was rejected and reopened. P2.1–P2.6 remain unchecked; neither the
  quick build nor the three passing receipts were promoted to canonical evidence.
- No live module, Windhawk action, Explorer action, registry change or system-file
  change occurred.

### 2026-07-24 — Intermediate hardening freezes rejected

- A `53/53` candidate bound M1/protocol/harness SHA-256
  `5ED380A22DDB710C9379065F963BECDF69930CC674EF15DB8D6CE0F99362950A`,
  `91C01E483078F355C9B1269058ED036E507CE7A8FDF0E55D98C428930DDB4E87`
  and
  `976343DA935F68EFB738B9E8C534B85228C68B64883BF261F1FF5AF65F9D0B02`.
  Its three non-overwriting receipts were retained as rejected intermediate
  evidence after wider production-path review reopened the freeze.
- Subsequent `54/54`, `59/59` and `65/65` focused candidates exercised
  additional UI, ABI and GIT failure branches. They were diagnostic hardening
  passes, not completion receipts, and none was promoted to canonical evidence.
- A later `76/76` candidate bound M1/protocol/harness SHA-256
  `027EB16D84B185071D1115C3A0084940BA91A6DF127F9A69ADA866526649D833`,
  `051030D5707CE660821EB13DA4D44733774AC17C53BC785E5B8250A0B9A0036E`
  and
  `0757E20E9E83F6CAE967A8FE2DB1BB2869C7C9F6D75034495B2242C534C608FB`.
  Three identical scenario payloads were produced, beginning with receipt
  SHA-256
  `6FD4DDE560EDD7C10E43932F613ECB77430C336CC4DAB7D28198CAA541B45791`.
  The resource audit found no P0/P1/P2, but the global ABI audit found five P1
  groups around export and COM firewalls, diagnostic exceptions, loader
  references and fail-closed pin ordering. The freeze was therefore rejected.
- All of these runs remained offline, set `activationPermitted=false` and
  `liveExplorer=not-run`, and made no Windhawk, Explorer, registry or system-file
  change.

### 2026-07-24 — Fifth implementation freeze rejected

- Frozen M1/protocol/harness/Test-Project SHA-256:
  `2D53DAB47D72FC6ECA47F88E03C8CFCF88F997B8C738B852C00E0630CC7986A0`,
  `051030D5707CE660821EB13DA4D44733774AC17C53BC785E5B8250A0B9A0036E`,
  `59EB81FE73C5BF5F701AA98B80A5EEBB5D73BC07D9D73C248B13DE198619DC81`
  and
  `10A84C8E32587D630A6FEFF0293FC7F6D2179D0E197A98F510DB65E3080350AE`.
- Fixed-toolchain M1 quick build
  `20260724T052830926Z-1dc836a7` compiled with `0` warnings and `0` errors;
  it was a single-module run and did not overwrite the committed canonical
  receipt.
- Three non-overwriting `81/81` receipts
  `phase2-final-freeze-2d53dab4-0{1,2,3}.json` had identical scenario payloads,
  `40` explained retained resources, no unexplained retention or double release,
  and explicit `activationPermitted=false` / `liveExplorer=not-run`. Their
  SHA-256 values were
  `4A8C38BE1FF1822DB5BA9BE9E08EE1B6F47BAFF89BCBAE89A7EC8B1886373B39`,
  `2F17F096A9B7999EAC512D503587C41F6D1C892F165B588AE3A8BF21C5614968`
  and
  `1A45CE94155A1E1C579F1E18CEA468033B09FBB0276621DAB37E3D302B572353`.
- The global ABI audit nevertheless reported `P0=0, P1=4, P2=2`: unsafe
  unpaired `LockServer(FALSE)`, incomplete `XamlBlurBrush` destruction and
  connection firewalls, projected delegate/TLS mutation leakage, and two
  lower-severity HANDLE/stats ownership gaps.
- The independent resource/evidence audit reported `P0=0, P1=3`: receipt
  source descriptors were not bound one-to-one to their property names,
  GIT/UI accounting was largely hand-authored rather than event-derived, and
  external window-message/hook APIs were called while an internal dispatch
  serialization lock was held. It also identified retry-terminal,
  capability-receipt, module-pin race and scenario-binding P2 gaps.
- The freeze and all three receipts were rejected. P2.1–P2.6 remain unchecked
  while the production code, harness and evidence gates are repaired and
  re-frozen. No live activation or host mutation occurred.

### 2026-07-24 — Sixth implementation freeze rejected

- Frozen M1/protocol/harness/runner/schema/Test-Project SHA-256:
  `F2B63989E9B208F23E047C51C50246D2F0CE68071A6644FEF11B39685C0E4756`,
  `989AD033ADCD1731DCA83DAFB21976833D7F02E0DE37DE57AFF1B51F9691B48A`,
  `E069917E752E871CBCFA45224DB6A667931BDA470D573DC44799464E93BF26AE`,
  `28F8F4129FEC09392954EC6767DEED2266E8B9F21B2D6E2669B1A4CD8FC9E50C`,
  `9726C9A9207F1BADD8F21027FBC5E6243DAC5B6F9374A3C332C859424605E5C7`
  and
  `70851A85D412A4E1C200EBBED8F27B4DDE3C5D7BB2E0452DDFD3FA25463C840A`.
- Fixed-toolchain M1 quick build
  `20260724T064037360Z-dc007cb1` compiled with `0` warnings and `0` errors.
  It was non-canonical, did not write the committed receipt and made no live
  claim.
- Three non-overwriting `90/90` receipts
  `phase2-final-freeze-f2b63989-0{1,2,3}.json` bound the six exact sources and
  had identical scenario payload SHA-256
  `9442A35852D1916028BEF3E6D5F233C380C628BDAB81E694170E9FF61C591CF5`.
  Each reported `307` resources, `614` events, `251` releases, `56` explained
  retentions, no unexplained retention or double release, and explicit
  `activationPermitted=false` / `liveExplorer=not-run`. Their receipt SHA-256
  values were
  `4BAEA01776DE0218DCE1DE7938574AEA4F445C89CE5CFD887744E2875593EA14`,
  `95EBFBC72170E9D4A99C8E74E411F1BBF979FBF4CB92DCA820765DDB7D70EC5B`
  and
  `C5D01B286A28FBADBF2DAF9596302E1EF90C7B73FCE5DB4F4D1E4C23ACCFB64A`.
- The independent ABI audit reported `P0=0, P1=3, P2=2`. It found that a
  protocol `Cleaned` UI record could retain its global/TLS runtime owner and
  DispatcherQueue, that production capability bits and disposition commits
  diverged from the shared protocol, and that `FreeLibrary` ran while the
  unload-pin decision gate was held. It also retained projected COM/WinRT
  illegal-exception boundaries and several HANDLE receipt paths as P2.
- The independent evidence audit also reported `P0=0, P1=3, P2=2`. It found
  that resource events could still be synthesized from aggregate counts,
  retained events lacked a required reason code, and the 90 scenario-to-
  production-gate map used broad prefix fallbacks rather than narrow explicit
  bindings. Schema semantic depth and the missing `module` area were P2.
- The freeze and all three receipts were rejected. No canonical build followed.
  P2.1–P2.6 remain unchecked while UI owner retirement, exact capability
  disposition, loader-call reentrancy and event provenance are repaired.
  No live activation, Windhawk/Explorer action, registry change or system-file
  change occurred.

### 2026-07-24 — Final implementation freeze accepted

- Final frozen SHA-256 identities:
  - M1: `DDC1455AD9994C775288EE8E0A5B1B8AFFA2B6BE67CAFCF9F13E76EFA01F29D7`
  - shared protocol: `0F26DE7C6EB150EAE4C2153AD51B1868F646BED877EED23D383DE6C0BE9DD676`
  - lifecycle harness: `62EC9A691F7157797863988DAD150260C0E277D4B7994FAE60A159B53F2006CA`
  - fault runner: `3D30294B8096E0D6AB2000929886995FD365C49406850DA7F14869C912B89463`
  - receipt schema: `6621A475900B8F6CF4C2E315ECED258278C374882CC4B8AC56DFCF6CF46567FE`
  - Test-Project: `3E362A4740296D495607A38D93D0E14924C83250FA96758F3874DC1A3D845D65`
- Fixed-toolchain M1 quick build `20260724T095924921Z-c6c0063d` compiled
  the frozen M1 with 0 warnings and 0 errors. It remained non-canonical,
  M1-only, `activationPermitted=false` and `liveExplorer=not-run`.
- Three non-overwriting final receipts:
  - `artifacts/lifecycle-fault-lab/runs/phase2-final-freeze-ddc1455a-62ec9a69-01.json`
    — `4F34543ABF253456380041AE4317A713853FEE67B6951A0ABAA17CB065A934EC`
  - `artifacts/lifecycle-fault-lab/runs/phase2-final-freeze-ddc1455a-62ec9a69-02.json`
    — `541C1E82A57D4D21E6FC4BA858788818A73D8E56DA919E594F8BBE138AAC1352`
  - `artifacts/lifecycle-fault-lab/runs/phase2-final-freeze-ddc1455a-62ec9a69-03.json`
    — `20C3E12DA472CFE884AA165D082570DD6A33BBE3B8A9B13D172501C80922D180`
- Every receipt is schema-valid and reports 90/90 unique scenarios, 332
  resources, 259 releases, 73 reasoned retentions, no unexplained retention
  and no double release. The three scenario payloads are byte-identical
  (`B7C8CD164672577210F82EB69B60680879F24C4A8BED9713C861490E6C244763`);
  the run-independent raw payloads are also byte-identical
  (`12410DD1FF1F428319CBB8A0F841A4E57D8DD54F653E51EF6C4B2E5851970F1D`).
  All three retain `releaseReady=false`, `activationPermitted=false` and
  `liveExplorer=not-run`.
- Independent ABI, evidence and lifecycle audits all ended at
  `P0=0 / P1=0 / P2=0`. The final lifecycle review explicitly verified the
  observer AddRef lifetime, unlocked degradation/publication/logging, the
  `{used=true, barrierParticipants=3}` concurrency receipt and its dedicated
  production gate mapping.

### 2026-07-24 — Canonical, full gate and host closure

- Canonical all-module run `20260724T100933494Z-7248f7df` wrote the schema v3
  committed receipt. Its run-summary SHA-256 is
  `622CBF4643CC2EAEBB8D6D186536ABAD4B9569AF22C88C10C731F5DB8FE9857E`.
  M1 DLL SHA-256 is
  `D2B95D50F47A080A1BB5A12577B5947B1D6F8E1D9683BF6C15D2A9CB66734603`;
  M2 DLL SHA-256 is
  `FC4369820663E2A5E5302260F41E5216E6776113EF24B98A2E45C8FDCD5AD147`.
  Both compiled with 0 warnings and 0 errors; the receipt keeps
  `activationPermitted=false` and `liveExplorer=not-run`.
- Full `scripts/Test-Project.ps1` passed 173/173. Its managed Supervisor
  Release build completed with 0 warnings and 0 errors; the mandatory fault
  lab, exact scenario map, current-source identity and canonical evidence gates
  all passed.
- Read-only Supervisor `inspect` passed 23/23 for profile
  `win11-25h2-26200.8875-x64`.
- The read-only host snapshot at
  `docs/receipts/host-safety-2026-07-24.json` records: kill switch armed with
  SHA-256
  `A6A4DDFFAEA0B963AD00F2E47B4BCC3EA3FF0EEC8E068A8A0A843F4D64A3F7BD`,
  permit absent, Windhawk Stopped/Manual/PID 0, zero Windhawk/JARVIS processes,
  zero matching module mappings across 352 enumerable processes, zero
  enumeration errors, and zero matching mappings in Explorer PID 11640.
- The repository remains wholly untracked; no file was staged, committed or
  pushed. No module was loaded, no kill switch was cleared, and no Windhawk,
  Explorer, registry or system-file mutation occurred.

## Completion rule

只有 P2.0—P2.6 的所有必需项完成、最终审计无 P0/P1、canonical 回执和只读主机
收据齐全时，Phase 2 才能标记为 complete。即使完成，M1 仍保持 build-only；
任何 live activation 必须在另一个任务中重新满足 AGENTS.md 的完整授权门。
