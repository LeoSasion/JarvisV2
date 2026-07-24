# 2026-07-22 Windhawk 安装边界事件

## 摘要

2026-07-22，JARVIS2 早期版本的 portable 工具链引导意外触发了 Windhawk 的系统安装。事故改变了主机状态，违反了“工具链必须 portable、不得安装或启动 Windhawk”的项目安全边界。

当前事件已受控：没有发现任何 JARVIS/JARVIS2 模组配置或加载。经用户明确授权，阶段 A 已正常停止 Windhawk 服务并禁用自动启动；当时一个完全挂起的 `ShellExperienceHost.exe` 仍保留惰性 DLL 映射。用户随后自行重启，2026-07-23 15:39（Asia/Shanghai）的只读验收确认 Windhawk 服务仍为 Stopped / Manual，Windhawk 进程和全系统 Windhawk DLL 映射均为 0，JARVIS2 急停保持 armed，`active-module.txt` 不存在。该验收不授权卸载或激活，且没有执行这两类操作。

## 已确认时间线

以下时间均为主机本地时间（Asia/Shanghai）。

- `20:00:11`：旧版构建流程触发 portable 工具链引导。该流程通过 `Start-Process` 运行 Windhawk 安装器，传给 `/D` 的目标路径额外带了一层引号。
- 安装器返回退出码 `0`，但预期 portable 目录中的编译器没有出现。退出码因此只证明安装器进程自行报告成功，不能证明 portable 工具链已在目标目录生成。
- `20:00:51`：Windows 记录 Windhawk 服务创建；`C:\Program Files\Windhawk\windhawk.ini` 表明该实例为 `Portable=0`。
- 随后修正参数后，portable 工具链才在项目工具缓存中成功生成。这没有抵消此前已经发生的系统安装。
- `22:07:40`：在用户明确授权阶段 A 后，Windows 记录 Windhawk 服务启动类型从 Auto 改为 Demand。官方正常停机命令均返回 `0`；服务进入 Stopped / Manual，服务与托盘进程退出，Explorer PID 未变且不再映射 Windhawk 引擎。
- 最终映射核验从 166 个宿主降至 1 个：微软签名的 `ShellExperienceHost.exe` PID 18816 仍保留基础 DLL，但其 52 个线程全部处于 Windows Suspended 状态。流程按失败关闭边界停止，没有恢复、终止或重启该系统进程，也没有运行卸载器。
- `2026-07-23 15:37:39`：用户自行重启后，新的 Explorer PID 11640 启动。
- `2026-07-23 15:39`：只读验收确认 `disabled.flag` 存在且 SHA-256 为 `A6A4DDFFAEA0B963AD00F2E47B4BCC3EA3FF0EEC8E068A8A0A843F4D64A3F7BD`，`active-module.txt` 不存在；Windhawk 服务为 Stopped / Manual / PID 0，Windhawk 进程数为 0，全系统 Windhawk DLL 映射数为 0；Explorer PID 11640 没有 Windhawk 映射；Supervisor `inspect` 为 23/23 compatible。没有执行卸载、激活或人工交互验收。

## 影响边界

已确认的主机变更是 Windhawk 的系统级安装和服务创建。该事实必须与 JARVIS2 模组状态分开陈述：

- 没有发现 `jarvis-native-taskbar`、`jarvis-taskbar-icon-size` 或其他 JARVIS/JARVIS2 模组的配置记录。
- 没有发现任何 JARVIS/JARVIS2 模组加载到 Explorer。
- `%LOCALAPPDATA%\JARVIS2\disabled.flag` 仍存在，急停状态为 armed。
- `%LOCALAPPDATA%\JARVIS2\active-module.txt` 不存在，没有签发或残留一次性模块许可。
- 阶段 A 没有终止或重启 Explorer；2026-07-23 的新 Explorer 进程来自用户自行重启，而不是项目恢复命令。
- M1 与 M2 的实机状态仍为 `liveExplorer: not-run`；系统中存在 Windhawk 引擎不能算作项目实机验证。

## 原因

旧版构建把具有安装能力的 Windhawk 安装器兼作 portable 解包器，并通过 `Start-Process` 拼接命令行。额外引号使 `/D` 目标未按预期生效，安装器转而执行默认系统安装。构建在 portable 编译器缺失时虽然随后失败，但这个检查发生在安装器运行之后，无法撤销已经产生的系统变更。

这次事件同时暴露了两个不成立的安全假设：

1. 安装器退出码为 `0` 不等于 portable 输出位于预期目录。
2. “运行安装器但传入 portable 参数”仍然拥有系统安装副作用，不属于安全的离线构建步骤。

## 已采取的项目侧措施

- 急停保持 armed，且没有创建 `active-module.txt`。
- 没有导入、配置、启用或加载任何 JARVIS/JARVIS2 模组。
- 阶段 A 没有终止或重启 Explorer；后续重启由用户自行完成，项目没有运行 `restart-explorer`。
- 初始处置没有擅自停止 Windhawk 服务；随后仅在用户明确授权下执行阶段 A 的正常停机和禁用自动启动。没有运行卸载程序、清理注册表或删除 `C:\Program Files\Windhawk`。
- 文档不再声称“本机没有安装 Windhawk”，并明确区分 Windhawk 引擎状态、JARVIS2 模组状态与实机验收状态。

## 永久构建边界

JARVIS2 后续构建入口只接受预先准备好的 portable 工具链，并必须验证 `Portable=1`、固定版本、编译器路径、输入树与哈希。构建入口绝不执行 Windhawk 安装器，也不把安装器当作解包器；预置工具链缺失、不完整或身份不匹配时直接失败关闭，不回退到安装或在线修复。

预置 portable 工具链的准备必须与日常构建分离，并作为独立、可审查的人工步骤处理。任何可能安装服务、写入 `Program Files`、启动 Windhawk 或修改注册表的动作，都不属于 locked 状态下允许的构建行为。

## 当前处置状态与下一授权边界

阶段 A 与用户重启后的只读验收均已完成：服务为 Stopped / Manual / PID 0，Windhawk 自身进程为 0，全系统 Windhawk DLL 映射为 0，Explorer PID 11640 无 Windhawk 映射；急停文件保持 armed 且 SHA-256 为 `A6A4DDFFAEA0B963AD00F2E47B4BCC3EA3FF0EEC8E068A8A0A843F4D64A3F7BD`，一次性许可不存在，Supervisor `inspect` 为 23/23 compatible。先前 `ShellExperienceHost.exe` 的惰性映射已在重启后消失，但这只解除映射层面的阻塞，不构成卸载或激活授权。本轮没有运行卸载器、清理 ProgramData/注册表、启用模块或清除急停，也没有完成人工交互验收。后续交互式卸载、残留清理、任何模块激活，以及未来的注销或重启动作都需要分别取得新的明确授权。
