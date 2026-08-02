using System.Globalization;
using System.Windows;

namespace Jarvis.ControlCenter;

public sealed record WindowsUiLanguageReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    string RequestedCulture,
    string ResourceLanguage,
    string Authority,
    bool SimplifiedChineseSelected,
    bool EnglishFallbackSelected,
    bool InternalOverrideSupported,
    bool SettingsPersisted,
    string ApplyAfter,
    IReadOnlyList<string> Failures);

public static class UiText
{
    public const string LanguageAuthority = "windows-current-ui-culture";

    private static readonly IReadOnlyDictionary<string, string> English =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Loc.App.Title"] = "JarvisV2 Control Center",
            ["Loc.Common.Cancel"] = "CANCEL",
            ["Loc.Common.Close"] = "Close",
            ["Loc.Header.Product"] = "JARVIS V2",
            ["Loc.Header.Subtitle"] = "PI DESKTOP / CONTROL CENTER",
            ["Loc.Header.RuntimePath"] = "CONTROL CENTER / LOCAL RUNTIME",
            ["Loc.Header.ReviewGated"] = "REVIEW-GATED PI TOOLS",
            ["Loc.Window.MinimizeAutomation"] = "Minimize JarvisV2",
            ["Loc.Window.CloseAutomation"] = "Close JarvisV2",
            ["Loc.Immersive.EnterAutomation"] =
                "Enter immersive conversation mode",
            ["Loc.Immersive.EnterTooltip"] = "Immersive mode (F11)",
            ["Loc.Immersive.ExitAutomation"] =
                "Exit immersive conversation mode",
            ["Loc.Immersive.Exit"] = "F11 / ESC · EXIT IMMERSIVE",
            ["Loc.Nav.Workspace"] = "Workspace",
            ["Loc.Nav.Conversation"] = "Conversation",
            ["Loc.Nav.Runtime"] = "Runtime",
            ["Loc.Nav.ReadTools"] = "Read tools",
            ["Loc.Nav.SafetyGates"] = "Safety gates",
            ["Loc.Nav.Evidence"] = "Evidence",
            ["Loc.Nav.LocalTime"] = "Local time",
            ["Loc.Nav.SafetySummary"] =
                "PI AGENT // 0.82.1\nTOOLS // READ + PROPOSE\nSHELL // LOCKED",
            ["Loc.Conversation.Title"] = "Conversation",
            ["Loc.Conversation.Shortcuts"] =
                "CTRL+ENTER / SEND ONCE    ESC / CANCEL",
            ["Loc.Stage.User"] = "USER",
            ["Loc.Stage.UserDetail"] = "REQUEST ORIGIN",
            ["Loc.Stage.Pi"] = "PI RUNTIME",
            ["Loc.Stage.PiDetail"] = "SESSION / BROKER",
            ["Loc.Stage.Tool"] = "BOUNDED TOOL",
            ["Loc.Stage.ToolDetail"] = "READ / FIND / PROPOSE",
            ["Loc.Stage.Jarvis"] = "JARVIS",
            ["Loc.Stage.JarvisDetail"] = "STREAMED RESPONSE",
            ["Loc.Stage.Ownership"] = "OWNERSHIP",
            ["Loc.Stage.ProgressAutomation"] = "Active turn ownership progress",
            ["Loc.Turn.You"] = "YOU",
            ["Loc.Turn.SessionTool"] = "SESSION TOOL",
            ["Loc.Turn.OrderedWholeSet"] = "ORDERED / WHOLE SET",
            ["Loc.Empty.StartAutomation"] = "Start Pi workspace session",
            ["Loc.Empty.AdmissionSteps"] =
                "1 WORKSPACE  /  2 PROVIDER  /  3 ADMISSION",
            ["Loc.Composer.Mode"] =
                "ONE-TURN COMPOSER / SEND ONCE HERE    REVIEWED MISSION / START LOOP IN OWNER POLICY",
            ["Loc.Composer.Automation"] = "Message Jarvis",
            ["Loc.Composer.Help"] =
                "Choose Send Once for one turn, or Start Reviewed Loop in Owner Policy for a four-edit six-hour reviewed mission.",
            ["Loc.Composer.SendAutomation"] = "Send one conversation message",
            ["Loc.Composer.Send"] = "SEND ONCE",
            ["Loc.Composer.CancelAutomation"] = "Cancel active turn",
            ["Loc.Inspector.Section"] = "Runtime inspector",
            ["Loc.Inspector.Title"] = "Pi conversation host",
            ["Loc.Inspector.Description"] =
                "One desktop-owned broker, sidecar, admitted workspace, encrypted checkpoint and durable review receipts.",
            ["Loc.Inspector.Runtime"] = "RUNTIME",
            ["Loc.Inspector.SessionBoundary"] = "Session boundary",
            ["Loc.Inspector.Provider"] = "PROVIDER",
            ["Loc.Inspector.Access"] = "ACCESS",
            ["Loc.Inspector.Workspace"] = "WORKSPACE",
            ["Loc.Review.Section"] = "Reviewed iteration",
            ["Loc.Review.OwnerPolicy"] = "OWNER POLICY",
            ["Loc.Review.ProgressAutomation"] =
                "Reviewed iteration progress and durable receipt",
            ["Loc.Review.ValidationAutomation"] =
                "Exact pinned trusted validation command",
            ["Loc.Review.PolicySummary"] =
                "Clean HEAD / four writes maximum / six hours / fixed repository gate / separate owner approval for pinned tests / reasoning only after a pass.",
            ["Loc.Review.StartAutomation"] =
                "Start reviewed loop from composer mission",
            ["Loc.Review.Start"] = "START REVIEWED LOOP",
            ["Loc.Review.RunTestsAutomation"] =
                "Run the exact pinned trusted tests once",
            ["Loc.Review.RunTests"] = "RUN PINNED TESTS ONCE",
            ["Loc.Review.RearmAutomation"] =
                "Re-arm interrupted reviewed iteration",
            ["Loc.Review.Rearm"] = "RE-ARM",
            ["Loc.Review.StopAutomation"] = "Stop reviewed iteration",
            ["Loc.Review.Stop"] = "STOP LOOP",
            ["Loc.Tools.Section"] = "Active tools",
            ["Loc.Tools.Permitted"] =
                "Permitted: read, grep, find, ls, propose_edit, propose_patch, propose_create_file",
            ["Loc.Tools.Writes"] = "Writes: desktop-owner approval only",
            ["Loc.Tools.Locked"] =
                "Shell / direct edit / unattended approval: locked",
            ["Loc.Tools.Continuation"] =
                "Loop continuation: repository gate + owner-approved pinned tests",
            ["Loc.Transport.Section"] = "Persistence and transport",
            ["Loc.Transport.Broker"] = "BROKER",
            ["Loc.Transport.Checkpoint"] = "CHECKPOINT",
            ["Loc.Transport.Credential"] = "CREDENTIAL POSTURE",
            ["Loc.Model.ConfigureAutomation"] = "Configure OpenAI model connection",
            ["Loc.Model.Configure"] = "CONFIGURE OPENAI",
            ["Loc.Shutdown.Label"] = "SAFE SHUTDOWN",
            ["Loc.Shutdown.Description"] =
                "Closing this window suspends any reviewed policy, quiesces submissions, cancels an active turn, flushes DPAPI state, then releases the owned sidecar and broker.",
            ["Loc.Footer.Runtime"] = "Pi desktop runtime",
            ["Loc.Footer.Safety"] =
                "NO SHELL INJECTION / NO UNREVIEWED WRITES",
            ["Loc.Runtime.Phase.NotStarted"] = "NOT STARTED",
            ["Loc.Runtime.Phase.Preview"] = "DESIGN PREVIEW",
            ["Loc.Runtime.Phase.Starting"] = "STARTING",
            ["Loc.Runtime.Phase.Ready"] = "READY",
            ["Loc.Runtime.Phase.Stopping"] = "STOPPING",
            ["Loc.Runtime.Phase.Stopped"] = "STOPPED",
            ["Loc.Runtime.Phase.Faulted"] = "FAULTED",
            ["Loc.Runtime.Phase.Unknown"] = "UNKNOWN",
            ["Loc.Runtime.Status.Preview"] =
                "Illustrative conversation data; no runtime was started.",
            ["Loc.Runtime.Status.Idle"] =
                "Launch with an admitted workspace and the packaged Pi runtime.",
            ["Loc.Runtime.Provider.Preview"] = "ILLUSTRATIVE // NO RUNTIME",
            ["Loc.Runtime.Provider.None"] = "NO PROVIDER ADMITTED",
            ["Loc.Runtime.Access"] = "READ + OWNER-REVIEWED WRITES",
            ["Loc.Runtime.Workspace.Preview"] = "ILLUSTRATIVE // NOT ADMITTED",
            ["Loc.Runtime.Workspace.None"] = "NO WORKSPACE ADMITTED",
            ["Loc.Runtime.Checkpoint.Preview"] = "ILLUSTRATIVE // NOT SAVED",
            ["Loc.Runtime.Checkpoint.NotLoaded"] = "NOT LOADED",
            ["Loc.Runtime.Checkpoint.Faulted"] = "FAULTED / SUBMISSIONS CLOSED",
            ["Loc.Runtime.Checkpoint.Counts"] = "{0} SAVED / {1} RESTORED",
            ["Loc.Runtime.Credential.Preview"] = "NOT CONFIGURED / NOT EVALUATED",
            ["Loc.Runtime.Credential.NotReady"] = "NOT READY",
            ["Loc.Runtime.Broker.Preview"] = "ILLUSTRATIVE // NOT STARTED",
            ["Loc.Runtime.Broker.None"] = "NO BROKER",
            ["Loc.Runtime.Broker.Counts"] = "{0} REQUESTS / {1} FAULTS",
            ["Loc.Runtime.Shutdown.Stopping"] = "QUIESCING ACTIVE TURN",
            ["Loc.Runtime.Shutdown.Stopped"] = "OWNED RUNTIME RELEASED",
            ["Loc.Runtime.Shutdown.Ready"] = "ORDERLY SHUTDOWN ARMED",
            ["Loc.Runtime.Shutdown.Preview"] = "ILLUSTRATIVE // NO OWNED RUNTIME",
            ["Loc.Runtime.Shutdown.None"] = "NO OWNED RUNTIME",
            ["Loc.Runtime.Handoff.Owner"] =
                "OWNER HOLDS A ONE-SHOT WORKSPACE WRITE DECISION",
            ["Loc.Runtime.Handoff.User"] = "USER HOLDS THE NEXT TURN",
            ["Loc.Runtime.Handoff.Pi"] = "PI RUNTIME OWNS THE ACTIVE TURN",
            ["Loc.Runtime.Handoff.Tool"] = "BOUNDED TOOL OWNS THE ACTIVE TURN",
            ["Loc.Runtime.Handoff.Complete"] = "TURN COMPLETE / CONTROL RETURNED",
            ["Loc.Runtime.Handoff.Streaming"] = "JARVIS IS STREAMING A RESPONSE",
            ["Loc.Runtime.Preview.User"] =
                "[ILLUSTRATIVE] Inspect the workspace boundary.",
            ["Loc.Runtime.Preview.Assistant"] =
                "Illustrative handoff complete. No workspace, broker, sidecar, or Pi tool was started in preview mode.",
            ["Loc.Runtime.Review.NotArmed"] = "NOT ARMED",
            ["Loc.Runtime.Review.Detail"] =
                "Type a mission in the composer, then arm a bounded reviewed loop.",
            ["Loc.Runtime.Review.Progress"] = "0 / 4 APPROVED EDITS",
            ["Loc.Runtime.Review.Receipt"] = "NO DURABLE RECEIPT",
            ["Loc.Runtime.Review.Head"] = "CLEAN GIT HEAD REQUIRED",
            ["Loc.Runtime.Review.Expiry"] = "6 HOUR OWNER POLICY",
            ["Loc.Runtime.Review.Profile"] = "PINNED TEST PROFILE REQUIRED",
            ["Loc.Runtime.Review.ProfileValue"] = "PINNED TEST PROFILE / {0}",
            ["Loc.Runtime.Review.Command"] =
                "No trusted validation command admitted.",
            ["Loc.Language.Section"] = "Display language",
            ["Loc.Language.Authority"] = "FOLLOWS WINDOWS",
            ["Loc.Language.Current"] = "CURRENT WINDOWS LANGUAGE",
            ["Loc.Language.Description"] =
                "Jarvis uses the Windows display language. Change it in Windows Settings or Control Panel under Time & language > Language, then restart Jarvis.",
            ["Loc.Launch.Title"] = "Start Pi Session",
            ["Loc.Launch.Header"] = "Pi workspace admission",
            ["Loc.Launch.CloseAutomation"] = "Close session launcher",
            ["Loc.Launch.Heading"] = "Start a workspace session",
            ["Loc.Launch.Intro"] =
                "Choose one local workspace and one model path. Jarvis verifies the boundary, starts the owned Pi runtime, then returns you to the conversation.",
            ["Loc.Launch.Recent"] = "Resume recent work",
            ["Loc.Launch.RecentDescription"] =
                "One action revalidates the workspace and runtime, then restores encrypted conversation context when present.",
            ["Loc.Launch.Dpapi"] = "CURRENTUSER DPAPI",
            ["Loc.Launch.NoRecent"] =
                "No recent work yet. Your first successful session will appear here.",
            ["Loc.Launch.WorkspaceStep"] = "1  Workspace",
            ["Loc.Launch.WorkspaceBoundary"] = "ONE CANONICAL ROOT",
            ["Loc.Launch.WorkspaceAutomation"] = "Workspace directory",
            ["Loc.Launch.BrowseAutomation"] = "Browse for workspace directory",
            ["Loc.Launch.Browse"] = "BROWSE",
            ["Loc.Launch.ModelStep"] = "2  Model path",
            ["Loc.Launch.ModelBoundary"] = "LOCAL DEFAULT / OPENAI OPT-IN",
            ["Loc.Launch.LocalAutomation"] = "Use local diagnostic provider",
            ["Loc.Launch.Local"] = "Local diagnostic",
            ["Loc.Launch.LocalDescription"] =
                "Ready immediately. Deterministic first turn; no model network.",
            ["Loc.Launch.OpenAiAutomation"] = "Use OpenAI Responses provider",
            ["Loc.Launch.OpenAiDescription"] =
                "Uses the desktop-only DPAPI key. Pi remains credential-free.",
            ["Loc.Launch.Awaiting"] = "AWAITING WORKSPACE",
            ["Loc.Launch.AwaitingDetail"] =
                "Browse to an existing project directory outside protected Windows locations.",
            ["Loc.Launch.Safety"] =
                "TOOLS // READ + PROPOSE\nWRITES // OWNER REVIEW\nSHELL // LOCKED",
            ["Loc.Launch.NoProcess"] = "No process starts until admission passes.",
            ["Loc.Launch.CancelAutomation"] = "Cancel session launch",
            ["Loc.Launch.StartAutomation"] = "Admit workspace and start Pi session",
            ["Loc.Launch.Start"] = "ADMIT & START",
            ["Loc.Launch.BrowseDialog"] =
                "Choose the single workspace Jarvis may read",
            ["Loc.Launch.Ready"] = "READY TO VERIFY RUNTIME",
            ["Loc.Launch.NotAdmitted"] = "WORKSPACE NOT ADMITTED",
            ["Loc.Launch.ReadyDetail"] =
                "The local path boundary passed. Start will verify the packaged Pi runtime.",
            ["Loc.Launch.ChooseAnother"] = "Choose another workspace.",
            ["Loc.Launch.VerifyingRecent"] = "VERIFYING RECENT WORK",
            ["Loc.Launch.VerifyingRuntime"] = "VERIFYING RUNTIME",
            ["Loc.Launch.VerifyingRecentDetail"] =
                "Rechecking the workspace and packaged runtime before restoring its encrypted checkpoint.",
            ["Loc.Launch.VerifyingRuntimeDetail"] =
                "Checking workspace, packaged hashes and the desktop-owned Pi sidecar.",
            ["Loc.Launch.SessionNotAdmitted"] = "SESSION NOT ADMITTED",
            ["Loc.Launch.Repair"] =
                "Choose another workspace or repair the portable runtime.",
            ["Loc.Launch.VerifyResume"] = "VERIFY & RESUME",
            ["Loc.Launch.Unavailable"] = "UNAVAILABLE",
            ["Loc.Launch.LocalProvider"] = "LOCAL DIAGNOSTIC",
            ["Loc.Launch.ResumeAutomation"] =
                "Verify and resume recent workspace {0} at {1} with {2}",
            ["Loc.Launch.UnavailableAutomation"] =
                "Recent workspace {0} at {1} is unavailable",
            ["Loc.Setup.Title"] = "Configure OpenAI",
            ["Loc.Setup.Header"] = "OpenAI model connection",
            ["Loc.Setup.CloseAutomation"] = "Close model setup",
            ["Loc.Setup.Heading"] = "Connect Jarvis to the model",
            ["Loc.Setup.Intro"] =
                "Your key is protected for this Windows user with DPAPI. Only the desktop provider can open it; the offline Pi sidecar never receives the credential.",
            ["Loc.Setup.Model"] = "MODEL",
            ["Loc.Setup.Tools"] = "TOOLS",
            ["Loc.Setup.WriteBoundary"] = "WRITE // DESKTOP OWNER APPROVAL ONLY",
            ["Loc.Setup.ApiKey"] = "OpenAI API key",
            ["Loc.Setup.NotConfigured"] = "NOT CONFIGURED",
            ["Loc.Setup.KeyAutomation"] = "OpenAI API key",
            ["Loc.Setup.Transport"] =
                "NETWORK // DESKTOP ONLY    RETENTION // STORE FALSE    SIDECAR // OFFLINE",
            ["Loc.Setup.ReplaceNote"] =
                "Replacing a key never exposes the previous value.",
            ["Loc.Setup.CancelAutomation"] = "Cancel model setup",
            ["Loc.Setup.SaveAutomation"] = "Protect and save OpenAI API key",
            ["Loc.Setup.Save"] = "PROTECT & SAVE",
            ["Loc.Setup.Unreadable"] = "UNREADABLE / REPLACE REQUIRED",
            ["Loc.Setup.Protected"] = "PROTECTED / REPLACE OPTIONAL",
            ["Loc.Setup.ValidationError"] =
                "Enter the complete API key; spaces and partial values are rejected.",
            ["Loc.Setup.SaveError"] = "The key was not saved: {0}",
        };

    private static readonly IReadOnlyDictionary<string, string> SimplifiedChinese =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Loc.App.Title"] = "JarvisV2 控制中心",
            ["Loc.Common.Cancel"] = "取消",
            ["Loc.Common.Close"] = "关闭",
            ["Loc.Header.Product"] = "JARVIS V2",
            ["Loc.Header.Subtitle"] = "PI 桌面端 / 控制中心",
            ["Loc.Header.RuntimePath"] = "控制中心 / 本地运行时",
            ["Loc.Header.ReviewGated"] = "受审查保护的 PI 工具",
            ["Loc.Window.MinimizeAutomation"] = "最小化 JarvisV2",
            ["Loc.Window.CloseAutomation"] = "关闭 JarvisV2",
            ["Loc.Immersive.EnterAutomation"] = "进入沉浸式对话模式",
            ["Loc.Immersive.EnterTooltip"] = "沉浸模式（F11）",
            ["Loc.Immersive.ExitAutomation"] = "退出沉浸式对话模式",
            ["Loc.Immersive.Exit"] = "F11 / ESC · 退出沉浸模式",
            ["Loc.Nav.Workspace"] = "工作区",
            ["Loc.Nav.Conversation"] = "对话",
            ["Loc.Nav.Runtime"] = "运行时",
            ["Loc.Nav.ReadTools"] = "读取工具",
            ["Loc.Nav.SafetyGates"] = "安全门禁",
            ["Loc.Nav.Evidence"] = "证据",
            ["Loc.Nav.LocalTime"] = "本地时间",
            ["Loc.Nav.SafetySummary"] =
                "PI AGENT // 0.82.1\n工具 // 读取 + 提案\nSHELL // 锁定",
            ["Loc.Conversation.Title"] = "对话",
            ["Loc.Conversation.Shortcuts"] =
                "CTRL+ENTER / 发送一次    ESC / 取消",
            ["Loc.Stage.User"] = "用户",
            ["Loc.Stage.UserDetail"] = "请求起点",
            ["Loc.Stage.Pi"] = "PI 运行时",
            ["Loc.Stage.PiDetail"] = "会话 / 代理",
            ["Loc.Stage.Tool"] = "受限工具",
            ["Loc.Stage.ToolDetail"] = "读取 / 查找 / 提案",
            ["Loc.Stage.Jarvis"] = "JARVIS",
            ["Loc.Stage.JarvisDetail"] = "流式响应",
            ["Loc.Stage.Ownership"] = "控制权",
            ["Loc.Stage.ProgressAutomation"] = "当前回合控制权进度",
            ["Loc.Turn.You"] = "你",
            ["Loc.Turn.SessionTool"] = "会话工具",
            ["Loc.Turn.OrderedWholeSet"] = "有序 / 完整集合",
            ["Loc.Empty.StartAutomation"] = "启动 Pi 工作区会话",
            ["Loc.Empty.AdmissionSteps"] =
                "1 工作区  /  2 提供方  /  3 准入",
            ["Loc.Composer.Mode"] =
                "单回合编辑器 / 在此发送一次    审查任务 / 在所有者策略中启动循环",
            ["Loc.Composer.Automation"] = "向 Jarvis 发送消息",
            ["Loc.Composer.Help"] =
                "选择“发送一次”执行单回合，或在所有者策略中启动最多四次编辑、六小时的受审查任务。",
            ["Loc.Composer.SendAutomation"] = "发送一条对话消息",
            ["Loc.Composer.Send"] = "发送一次",
            ["Loc.Composer.CancelAutomation"] = "取消当前回合",
            ["Loc.Inspector.Section"] = "运行时检查器",
            ["Loc.Inspector.Title"] = "Pi 对话宿主",
            ["Loc.Inspector.Description"] =
                "由桌面拥有的代理、sidecar、已准入工作区、加密检查点和持久审查收据。",
            ["Loc.Inspector.Runtime"] = "运行时",
            ["Loc.Inspector.SessionBoundary"] = "会话边界",
            ["Loc.Inspector.Provider"] = "提供方",
            ["Loc.Inspector.Access"] = "访问权限",
            ["Loc.Inspector.Workspace"] = "工作区",
            ["Loc.Review.Section"] = "受审查迭代",
            ["Loc.Review.OwnerPolicy"] = "所有者策略",
            ["Loc.Review.ProgressAutomation"] = "受审查迭代进度和持久收据",
            ["Loc.Review.ValidationAutomation"] = "固定的可信验证命令",
            ["Loc.Review.PolicySummary"] =
                "要求干净 HEAD / 最多四次写入 / 六小时 / 固定仓库门禁 / 固定测试需单独获得所有者批准 / 仅在通过后继续推理。",
            ["Loc.Review.StartAutomation"] = "从编辑器任务启动受审查循环",
            ["Loc.Review.Start"] = "启动受审查循环",
            ["Loc.Review.RunTestsAutomation"] = "运行一次固定的可信测试",
            ["Loc.Review.RunTests"] = "运行一次固定测试",
            ["Loc.Review.RearmAutomation"] = "重新武装中断的受审查迭代",
            ["Loc.Review.Rearm"] = "重新武装",
            ["Loc.Review.StopAutomation"] = "停止受审查迭代",
            ["Loc.Review.Stop"] = "停止循环",
            ["Loc.Tools.Section"] = "活动工具",
            ["Loc.Tools.Permitted"] =
                "允许：read、grep、find、ls、propose_edit、propose_patch、propose_create_file",
            ["Loc.Tools.Writes"] = "写入：仅限桌面所有者批准",
            ["Loc.Tools.Locked"] = "Shell / 直接编辑 / 无人批准：锁定",
            ["Loc.Tools.Continuation"] = "循环继续：仓库门禁 + 所有者批准的固定测试",
            ["Loc.Transport.Section"] = "持久化与传输",
            ["Loc.Transport.Broker"] = "代理",
            ["Loc.Transport.Checkpoint"] = "检查点",
            ["Loc.Transport.Credential"] = "凭据状态",
            ["Loc.Model.ConfigureAutomation"] = "配置 OpenAI 模型连接",
            ["Loc.Model.Configure"] = "配置 OPENAI",
            ["Loc.Shutdown.Label"] = "安全关闭",
            ["Loc.Shutdown.Description"] =
                "关闭窗口会暂停受审查策略、停止新提交、取消活动回合、刷新 DPAPI 状态，然后释放自有 sidecar 和代理。",
            ["Loc.Footer.Runtime"] = "Pi 桌面运行时",
            ["Loc.Footer.Safety"] = "不注入 SHELL / 不进行未经审查的写入",
            ["Loc.Runtime.Phase.NotStarted"] = "尚未启动",
            ["Loc.Runtime.Phase.Preview"] = "设计预览",
            ["Loc.Runtime.Phase.Starting"] = "正在启动",
            ["Loc.Runtime.Phase.Ready"] = "就绪",
            ["Loc.Runtime.Phase.Stopping"] = "正在停止",
            ["Loc.Runtime.Phase.Stopped"] = "已停止",
            ["Loc.Runtime.Phase.Faulted"] = "故障",
            ["Loc.Runtime.Phase.Unknown"] = "未知",
            ["Loc.Runtime.Status.Preview"] = "示意对话数据；未启动任何运行时。",
            ["Loc.Runtime.Status.Idle"] = "请使用已准入的工作区和打包 Pi 运行时启动。",
            ["Loc.Runtime.Provider.Preview"] = "示意 // 无运行时",
            ["Loc.Runtime.Provider.None"] = "未准入提供方",
            ["Loc.Runtime.Access"] = "读取 + 所有者审查写入",
            ["Loc.Runtime.Workspace.Preview"] = "示意 // 未准入",
            ["Loc.Runtime.Workspace.None"] = "未准入工作区",
            ["Loc.Runtime.Checkpoint.Preview"] = "示意 // 未保存",
            ["Loc.Runtime.Checkpoint.NotLoaded"] = "未加载",
            ["Loc.Runtime.Checkpoint.Faulted"] = "故障 / 已关闭提交",
            ["Loc.Runtime.Checkpoint.Counts"] = "已保存 {0} / 已恢复 {1}",
            ["Loc.Runtime.Credential.Preview"] = "未配置 / 未评估",
            ["Loc.Runtime.Credential.NotReady"] = "未就绪",
            ["Loc.Runtime.Broker.Preview"] = "示意 // 未启动",
            ["Loc.Runtime.Broker.None"] = "无代理",
            ["Loc.Runtime.Broker.Counts"] = "请求 {0} / 故障 {1}",
            ["Loc.Runtime.Shutdown.Stopping"] = "正在静默活动回合",
            ["Loc.Runtime.Shutdown.Stopped"] = "自有运行时已释放",
            ["Loc.Runtime.Shutdown.Ready"] = "有序关闭已准备",
            ["Loc.Runtime.Shutdown.Preview"] = "示意 // 无自有运行时",
            ["Loc.Runtime.Shutdown.None"] = "无自有运行时",
            ["Loc.Runtime.Handoff.Owner"] = "所有者持有一次性工作区写入决定",
            ["Loc.Runtime.Handoff.User"] = "用户持有下一回合",
            ["Loc.Runtime.Handoff.Pi"] = "PI 运行时持有当前回合",
            ["Loc.Runtime.Handoff.Tool"] = "受限工具持有当前回合",
            ["Loc.Runtime.Handoff.Complete"] = "回合完成 / 控制权已返回",
            ["Loc.Runtime.Handoff.Streaming"] = "JARVIS 正在流式响应",
            ["Loc.Runtime.Preview.User"] = "[示意] 检查工作区边界。",
            ["Loc.Runtime.Preview.Assistant"] =
                "示意交接已完成。预览模式未启动工作区、代理、sidecar 或 Pi 工具。",
            ["Loc.Runtime.Review.NotArmed"] = "尚未启用",
            ["Loc.Runtime.Review.Detail"] = "在编辑器中输入任务，然后启用受限审查循环。",
            ["Loc.Runtime.Review.Progress"] = "0 / 4 次已批准编辑",
            ["Loc.Runtime.Review.Receipt"] = "无持久收据",
            ["Loc.Runtime.Review.Head"] = "要求干净的 GIT HEAD",
            ["Loc.Runtime.Review.Expiry"] = "6 小时所有者策略",
            ["Loc.Runtime.Review.Profile"] = "需要固定测试配置",
            ["Loc.Runtime.Review.ProfileValue"] = "固定测试配置 / {0}",
            ["Loc.Runtime.Review.Command"] = "未准入可信验证命令。",
            ["Loc.Language.Section"] = "显示语言",
            ["Loc.Language.Authority"] = "跟随 WINDOWS",
            ["Loc.Language.Current"] = "当前 WINDOWS 语言",
            ["Loc.Language.Description"] =
                "Jarvis 使用 Windows 显示语言。如需更改，请在 Windows 设置或控制面板的“时间和语言 > 语言”中操作，然后重启 Jarvis。",
            ["Loc.Launch.Title"] = "启动 Pi 会话",
            ["Loc.Launch.Header"] = "Pi 工作区准入",
            ["Loc.Launch.CloseAutomation"] = "关闭会话启动器",
            ["Loc.Launch.Heading"] = "启动工作区会话",
            ["Loc.Launch.Intro"] =
                "选择一个本地工作区和模型路径。Jarvis 会验证边界、启动自有 Pi 运行时，然后返回对话。",
            ["Loc.Launch.Recent"] = "继续最近工作",
            ["Loc.Launch.RecentDescription"] =
                "一次操作即可重新验证工作区与运行时，并在存在时恢复加密对话上下文。",
            ["Loc.Launch.Dpapi"] = "当前用户 DPAPI",
            ["Loc.Launch.NoRecent"] = "暂无最近工作。首次成功会话后将显示在这里。",
            ["Loc.Launch.WorkspaceStep"] = "1  工作区",
            ["Loc.Launch.WorkspaceBoundary"] = "唯一规范根目录",
            ["Loc.Launch.WorkspaceAutomation"] = "工作区目录",
            ["Loc.Launch.BrowseAutomation"] = "浏览工作区目录",
            ["Loc.Launch.Browse"] = "浏览",
            ["Loc.Launch.ModelStep"] = "2  模型路径",
            ["Loc.Launch.ModelBoundary"] = "默认本地 / OPENAI 可选",
            ["Loc.Launch.LocalAutomation"] = "使用本地诊断提供方",
            ["Loc.Launch.Local"] = "本地诊断",
            ["Loc.Launch.LocalDescription"] = "立即可用。首回合确定性运行；不访问模型网络。",
            ["Loc.Launch.OpenAiAutomation"] = "使用 OpenAI Responses 提供方",
            ["Loc.Launch.OpenAiDescription"] =
                "使用仅限桌面的 DPAPI 密钥；Pi 不接触凭据。",
            ["Loc.Launch.Awaiting"] = "等待工作区",
            ["Loc.Launch.AwaitingDetail"] = "请选择受保护 Windows 目录之外的现有项目目录。",
            ["Loc.Launch.Safety"] =
                "工具 // 读取 + 提案\n写入 // 所有者审查\nSHELL // 锁定",
            ["Loc.Launch.NoProcess"] = "准入通过前不会启动任何进程。",
            ["Loc.Launch.CancelAutomation"] = "取消会话启动",
            ["Loc.Launch.StartAutomation"] = "准入工作区并启动 Pi 会话",
            ["Loc.Launch.Start"] = "准入并启动",
            ["Loc.Launch.BrowseDialog"] = "选择 Jarvis 可以读取的唯一工作区",
            ["Loc.Launch.Ready"] = "可以验证运行时",
            ["Loc.Launch.NotAdmitted"] = "工作区未准入",
            ["Loc.Launch.ReadyDetail"] = "本地路径边界已通过。启动时将验证打包的 Pi 运行时。",
            ["Loc.Launch.ChooseAnother"] = "请选择其他工作区。",
            ["Loc.Launch.VerifyingRecent"] = "正在验证最近工作",
            ["Loc.Launch.VerifyingRuntime"] = "正在验证运行时",
            ["Loc.Launch.VerifyingRecentDetail"] =
                "正在重新检查工作区和打包运行时，随后恢复其加密检查点。",
            ["Loc.Launch.VerifyingRuntimeDetail"] =
                "正在检查工作区、打包哈希和桌面拥有的 Pi sidecar。",
            ["Loc.Launch.SessionNotAdmitted"] = "会话未准入",
            ["Loc.Launch.Repair"] = "请选择其他工作区或修复便携运行时。",
            ["Loc.Launch.VerifyResume"] = "验证并继续",
            ["Loc.Launch.Unavailable"] = "不可用",
            ["Loc.Launch.LocalProvider"] = "本地诊断",
            ["Loc.Launch.ResumeAutomation"] =
                "验证并继续最近工作区 {0}，路径 {1}，提供方 {2}",
            ["Loc.Launch.UnavailableAutomation"] =
                "最近工作区 {0}（路径 {1}）不可用",
            ["Loc.Setup.Title"] = "配置 OpenAI",
            ["Loc.Setup.Header"] = "OpenAI 模型连接",
            ["Loc.Setup.CloseAutomation"] = "关闭模型设置",
            ["Loc.Setup.Heading"] = "将 Jarvis 连接到模型",
            ["Loc.Setup.Intro"] =
                "密钥使用 DPAPI 为当前 Windows 用户保护。只有桌面提供方能够读取；离线 Pi sidecar 永远不会收到凭据。",
            ["Loc.Setup.Model"] = "模型",
            ["Loc.Setup.Tools"] = "工具",
            ["Loc.Setup.WriteBoundary"] = "写入 // 仅限桌面所有者批准",
            ["Loc.Setup.ApiKey"] = "OpenAI API 密钥",
            ["Loc.Setup.NotConfigured"] = "未配置",
            ["Loc.Setup.KeyAutomation"] = "OpenAI API 密钥",
            ["Loc.Setup.Transport"] =
                "网络 // 仅桌面    保留 // STORE FALSE    SIDECAR // 离线",
            ["Loc.Setup.ReplaceNote"] = "替换密钥时绝不会显示旧值。",
            ["Loc.Setup.CancelAutomation"] = "取消模型设置",
            ["Loc.Setup.SaveAutomation"] = "保护并保存 OpenAI API 密钥",
            ["Loc.Setup.Save"] = "保护并保存",
            ["Loc.Setup.Unreadable"] = "无法读取 / 必须替换",
            ["Loc.Setup.Protected"] = "已保护 / 可选替换",
            ["Loc.Setup.ValidationError"] = "请输入完整 API 密钥；不接受空格或不完整值。",
            ["Loc.Setup.SaveError"] = "未保存密钥：{0}",
        };

    public static WindowsUiLanguageReceipt Resolve(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        bool simplifiedChinese =
            culture.Name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ||
            culture.Name.Equals("zh-SG", StringComparison.OrdinalIgnoreCase) ||
            culture.Name.Equals(
                "zh-Hans",
                StringComparison.OrdinalIgnoreCase) ||
            culture.Parent.Name.Equals(
                "zh-Hans",
                StringComparison.OrdinalIgnoreCase);
        bool english = culture.TwoLetterISOLanguageName.Equals(
            "en",
            StringComparison.OrdinalIgnoreCase);
        string resourceLanguage = simplifiedChinese ? "zh-CN" : "en-US";
        return new(
            1,
            "jarvisv2-windows-ui-language-resolution",
            "resolved-from-windows",
            culture.Name,
            resourceLanguage,
            LanguageAuthority,
            simplifiedChinese,
            !simplifiedChinese && !english,
            false,
            false,
            "application-restart-after-windows-language-change",
            []);
    }

    public static WindowsUiLanguageReceipt ApplyWindowsLanguage(
        Application application,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(application);
        WindowsUiLanguageReceipt receipt = Resolve(culture);
        IReadOnlyDictionary<string, string> selected =
            receipt.SimplifiedChineseSelected
                ? SimplifiedChinese
                : English;
        ResourceDictionary resources = new();
        foreach ((string key, string english) in English)
        {
            resources[key] = selected.TryGetValue(key, out string? translated)
                ? translated
                : english;
        }
        resources["Loc.Language.Current"] = receipt.SimplifiedChineseSelected
            ? $"简体中文 · WINDOWS ({culture.Name})"
            : $"{culture.NativeName.ToUpperInvariant()} · WINDOWS ({culture.Name})";
        application.Resources.MergedDictionaries.Insert(0, resources);
        return receipt;
    }

    public static string Get(string key)
    {
        if (Application.Current?.TryFindResource(key) is string localized)
        {
            return localized;
        }
        return English.TryGetValue(key, out string? fallback)
            ? fallback
            : key;
    }

    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    internal static bool HasCompleteSimplifiedChineseCatalog() =>
        English.Keys.All(SimplifiedChinese.ContainsKey) &&
        SimplifiedChinese.Keys.All(English.ContainsKey);
}

public static class UiLanguageProbe
{
    public static object Run()
    {
        WindowsUiLanguageReceipt zh = UiText.Resolve(
            CultureInfo.GetCultureInfo("zh-CN"));
        WindowsUiLanguageReceipt en = UiText.Resolve(
            CultureInfo.GetCultureInfo("en-US"));
        WindowsUiLanguageReceipt zhHans = UiText.Resolve(
            CultureInfo.GetCultureInfo("zh-Hans"));
        WindowsUiLanguageReceipt zhSg = UiText.Resolve(
            CultureInfo.GetCultureInfo("zh-SG"));
        WindowsUiLanguageReceipt unsupported = UiText.Resolve(
            CultureInfo.GetCultureInfo("fr-FR"));
        List<string> failures = [];
        if (!zh.SimplifiedChineseSelected || zh.ResourceLanguage != "zh-CN")
        {
            failures.Add("simplified-chinese-windows-culture-not-selected");
        }
        if (
            zhHans.ResourceLanguage != "zh-CN" ||
            zhSg.ResourceLanguage != "zh-CN")
        {
            failures.Add("simplified-chinese-windows-family-not-selected");
        }
        if (en.ResourceLanguage != "en-US" || en.EnglishFallbackSelected)
        {
            failures.Add("english-windows-culture-not-selected");
        }
        if (
            unsupported.ResourceLanguage != "en-US" ||
            !unsupported.EnglishFallbackSelected)
        {
            failures.Add("unsupported-culture-did-not-fallback-to-english");
        }
        if (!UiText.HasCompleteSimplifiedChineseCatalog())
        {
            failures.Add("simplified-chinese-resource-catalog-incomplete");
        }
        if (
            zh.InternalOverrideSupported ||
            zh.SettingsPersisted ||
            zh.Authority != UiText.LanguageAuthority)
        {
            failures.Add("jarvis-language-override-was-enabled");
        }
        return new
        {
            SchemaVersion = 1,
            ReceiptType = "jarvisv2-control-center-ui-language-probe",
            Result = failures.Count == 0 ? "passed" : "failed",
            WindowsCulture = CultureInfo.CurrentUICulture.Name,
            WindowsAuthority = UiText.LanguageAuthority,
            SimplifiedChineseResource = zh.ResourceLanguage,
            SimplifiedChineseNeutralResource = zhHans.ResourceLanguage,
            SimplifiedChineseSingaporeResource = zhSg.ResourceLanguage,
            EnglishResource = en.ResourceLanguage,
            UnsupportedFallbackResource = unsupported.ResourceLanguage,
            ResourceCatalogComplete = UiText.HasCompleteSimplifiedChineseCatalog(),
            InternalOverrideSupported = false,
            SettingsPersisted = false,
            ReadyForShellMutation = false,
            ActivationPermitted = false,
            LiveExplorer = "not-run",
            MutationPerformed = false,
            Failures = failures,
        };
    }
}
