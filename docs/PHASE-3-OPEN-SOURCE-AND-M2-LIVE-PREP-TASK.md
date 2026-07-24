# Phase 3 — Open-source baseline and M2 live-validation preparation

Status: **COMPLETE**

Date opened: 2026-07-24
Repository name: **JarvisV2**
Internal runtime namespace: **JARVIS2**
Live activation in this task: **FORBIDDEN**

## Goal

把已完成的离线原生基础整理成可公开审查的 GPL 仓库，并为
`jarvis-taskbar-icon-size` 建立只读、机器可核对的首次实机验证准备包。
本阶段可以整理、测试、构建、提交和准备公开发布，但不能把广义的“自动授权”
解释成清除急停或加载模块的精确授权。

## Safety contract

- [x] `%LOCALAPPDATA%\JARVIS2\disabled.flag` 必须始终保持 armed。
- [x] 不创建、消费或修改 `active-module.txt`。
- [x] 不安装、启动、配置或启用 Windhawk。
- [x] 不加载任何模块，不终止或重启 Explorer。
- [x] M1 继续 `build-only`，不得进入 Supervisor allowlist。
- [x] M2 只生成 read-only readiness evidence，不执行激活。
- [x] 实机加载前仍必须展示新鲜兼容报告，并由用户逐字批准精确
      `clear-kill-switch --module jarvis-taskbar-icon-size --confirm` 命令。
- [x] `JarvisV2` 是仓库与展示名称；状态目录、信号量、模块 ID 和收据中的
      `JARVIS2` 是稳定安全协议，不在本阶段迁移。

## Work items

### P3.0 — Publication boundary

- [x] 建立机器可读 publication manifest。
- [x] 排除 artifacts、便携工具链、构建输出、转储、跟踪和二进制。
- [x] 检查候选文件大小、reparse point、本机绝对路径和常见凭据模式。
- [x] 保留 GPL-3.0 全文、上游 commit/hash、修改记录和 reference-only 边界。
- [x] 明确 public CI 不运行缺少锁定便携工具链的 canonical native build。

### P3.1 — Open-source repository baseline

- [x] README 使用 `JarvisV2` 展示名并解释内部 `JARVIS2` 兼容边界。
- [x] 增加贡献、安全报告、行为准则和公开边界文档。
- [x] 增加 issue / pull-request 模板。
- [x] 增加最小权限、固定 action commit 的 Windows CI。
- [x] CI 运行 publication boundary、PowerShell/JSON 解析和 Supervisor Release build。

### P3.2 — Read-only M2 readiness receipt

- [x] 增加 fail-closed JSON schema；固定
      `activationPermitted=false`、`liveExplorer=not-run`。
- [x] 核对 kill switch、permit、Windhawk 服务、进程和所有可枚举进程的模块映射；
      另行记录不可枚举进程数量，并要求 Explorer 本身可完整枚举。
- [x] 调用 Supervisor `inspect`，核对全部兼容检查。
- [x] 核对 canonical run、当前 M2 源码、0 warning / 0 error 和 allowlist。
- [x] 输出 exact approval command，但脚本不得执行它。
- [x] 默认只写 stdout；指定 artifact path 时拒绝覆盖和目录逃逸。

### P3.3 — Baseline and human-validation package

- [x] 增加只读 Explorer CPU、内存、handle、thread 基线采样器。
- [x] 增加一次一个模块的受控实机 runbook。
- [x] 增加 Win 键、任务按钮、托盘、多屏、DPI、自动隐藏和恢复检查表。
- [x] 明确任何 crash、交互退化、意外窗口或高 idle CPU 都立即 re-arm。
- [x] 禁止 unattended Explorer restart loop 和自动恢复。

### P3.4 — Evidence integration

- [x] 把 Phase 3 文件、安全句和无副作用约束接入 `Test-Project.ps1`。
- [x] 运行 publication boundary。
- [x] 运行 M2 readiness 并生成非覆盖收据。
- [x] 运行短时锁定态性能采样，验证脚本可执行。
- [x] 重新运行 canonical all-module build 和完整项目门禁。
- [x] 最终只读复核仍为 `releaseReady=false`、`activationPermitted=false`。

### P3.5 — Git baseline

- [x] 审查精确提交清单；不使用未审查的宽泛 staging。
- [x] 创建 `main` 初始分支与本地基线提交。
- [x] 记录 tracked/staged/ignored/large-file/secret-scan 收据。
- [x] 只在远程 owner、visibility 和目标仓库均明确后发布。
- [x] 阶段结束时重新只读确认主机 locked。

## Completion rule

只有 P3.0—P3.5 全部完成、公开边界和完整门禁通过、canonical 收据更新、
本地主机仍 locked，并且没有执行 live activation 时，Phase 3 才能标记完成。
即使完成，M2 也只能进入“等待精确命令批准”状态，不能自动加载。

## Execution log

### 2026-07-24 — Task opened

- User selected repository name `JarvisV2` and authorized next-stage work.
- Runtime safety identifiers remain `JARVIS2`; no migration is attempted.
- Initial public candidate set contained 30 files / 2,206,852 bytes, no
  candidate binaries, no reparse points, and no common credential or local
  absolute-path matches.
- GPL-3.0 text, upstream locks and third-party modification notices were present.
- No live or host-mutating action was authorized or executed.

### 2026-07-24 — Publication and readiness implementation complete

- Publication boundary passed for 47 candidate files / 2,282,724 bytes.
  Generated artifacts, tools, binaries, dumps, local paths and common credential
  patterns are excluded; `secretValuesPrinted=false`.
- Public Windows CI pins `actions/checkout` and `actions/setup-dotnet` to exact
  commits, grants only `contents: read`, validates the publication boundary and
  builds the managed Supervisor. It deliberately does not run native compilation.
- Final M2 readiness run `20260724T105321483Z-e869a142` passed schema validation
  with receipt SHA-256
  `7F8AD7E96EFFB4AB14919B1451EE17F40E62505B054B2A4CA7B3FC0D2C40FB33`.
  It recorded Supervisor 23/23, 127 module-enumerable processes, 171
  non-enumerable/protected processes, zero enumeration errors, zero matching
  mappings and complete Explorer module inspection.
- The readiness receipt remains
  `activationPermitted=false`, `liveExplorer=not-run`,
  `exactCommandApproved=false`, `recoveryTerminalAvailable=false` and
  `canExecuteNow=false`.
- A 5.161-second locked baseline collected 10 samples with 0.0% measured average
  and peak Explorer CPU during that short window. The artifact SHA-256 is
  `C4E220024A35C9F11AD603C7EBB37A3189695F6BC61C9E1831823AE11C822427`.
  This is a script smoke test, not the required one-hour live comparison.
- Canonical all-module run `20260724T105833265Z-e47539f8` completed with both
  native modules at 0 warnings / 0 errors. The run-summary SHA-256 is
  `A522270D142B26F30AC434D55B4F3294DF5BD636AB85C9DC2F2E6F66B9F9F6FF`.
- Full `Test-Project.ps1` passed 182/182, including all Phase 3 gates and the
  managed Release build. It still reports `releaseReady=false` and
  `activationPermitted=false`.

### 2026-07-24 — Public Git baseline complete

- The exact reviewed set contained 47 tracked files / 2,284,531 bytes. Git
  whitespace checks and the publication boundary passed with no large files,
  excluded generated roots, local absolute paths or common credential matches.
- Local repository identity is scoped to this checkout. The initial `main`
  baseline commit is `c9de7a60f10c3c44a27c7b1b5d2fc6e6880c5d78`.
- The authenticated owner, public visibility and previously absent target were
  verified before creating <https://github.com/LeoSasion/JarvisV2>.
- Public CI run
  <https://github.com/LeoSasion/JarvisV2/actions/runs/30088098604> completed
  successfully for the baseline commit.
- The final read-only host inspection kept `disabled.flag` armed,
  `active-module.txt` absent, Windhawk stopped/manual and zero JARVIS2 module
  mappings. No module was loaded and Explorer was not restarted.
