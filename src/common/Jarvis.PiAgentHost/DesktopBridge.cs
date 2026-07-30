using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Jarvis.PiAgentHost;

public sealed record PiAgentSidecarOptions(
    string NodeExecutablePath,
    string HostScriptPath,
    int MaximumFrameBytes = 65_536,
    int RequestTimeoutMilliseconds = 10_000,
    int ShutdownTimeoutMilliseconds = 3_000);

public sealed record PiAgentDesktopProbeReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    string Protocol,
    string Package,
    string InstalledVersion,
    string IntegrationMode,
    bool DesktopLaunchImplemented,
    bool ReadyObserved,
    bool HelloPassed,
    bool CapabilitiesPassed,
    bool SessionCreationPassed,
    bool WorkspaceBound,
    bool ShutdownPassed,
    bool PiOffline,
    bool CredentialEnvironmentScrubbed,
    IReadOnlyList<string> InitialTools,
    IReadOnlyList<string> DeniedTools,
    bool SessionCreationEnabled,
    bool PromptingEnabled,
    bool SessionPersisted,
    bool CredentialTransportAllowed,
    bool ShellMutationSupported,
    bool ExplorerMutationSupported,
    bool SystemMutationSupported,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed);

public sealed record PiAgentBridgeFaultScenario(
    string Name,
    bool Passed,
    string ObservedFailure);

public sealed record PiAgentBridgeFaultReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    int ScenarioCount,
    int PassedCount,
    IReadOnlyList<PiAgentBridgeFaultScenario> Scenarios,
    bool SessionCreationEnabled,
    bool ShellMutationSupported,
    bool ExplorerMutationSupported,
    bool SystemMutationSupported,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed);

public sealed class PiAgentSidecarController : IAsyncDisposable
{
    public const string ContractId = "jarvisv2-pi-agent-desktop-host-v1";
    public const string PackageName = "@earendil-works/pi-coding-agent";
    public const string ExpectedVersion = "0.82.1";

    private static readonly string[] RequiredChildEnvironmentVariables =
    [
        "APPDATA",
        "HOMEDRIVE",
        "HOMEPATH",
        "LOCALAPPDATA",
        "SystemRoot",
        "TEMP",
        "TMP",
        "USERPROFILE",
        "WINDIR",
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly PiAgentSidecarOptions options;
    private readonly Process process;
    private readonly Task<string> stderrTask;
    private bool shutdownCompleted;

    public bool CredentialEnvironmentClean { get; private set; }

    private PiAgentSidecarController(
        PiAgentSidecarOptions options,
        Process process)
    {
        this.options = options;
        this.process = process;
        stderrTask = process.StandardError.ReadToEndAsync();
    }

    public static async Task<PiAgentSidecarController> StartAsync(
        PiAgentSidecarOptions options,
        CancellationToken cancellationToken)
    {
        ValidateOptions(options);

        ProcessStartInfo startInfo = new()
        {
            FileName = options.NodeExecutablePath,
            WorkingDirectory =
                Directory.GetParent(options.HostScriptPath)?.Parent?.FullName
                ?? throw new InvalidOperationException(
                    "The sidecar project root could not be resolved."),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        startInfo.ArgumentList.Add(options.HostScriptPath);
        startInfo.ArgumentList.Add("serve");
        startInfo.Environment.Clear();
        foreach (string variable in RequiredChildEnvironmentVariables)
        {
            string? value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                startInfo.Environment[variable] = value;
            }
        }
        startInfo.Environment["PI_OFFLINE"] = "1";

        Process process = new()
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException(
                "The Pi Agent sidecar process did not start.");
        }

        PiAgentSidecarController controller = new(options, process);
        try
        {
            using CancellationTokenSource timeout =
                CreateTimeout(
                    cancellationToken,
                    options.RequestTimeoutMilliseconds);
            using JsonDocument ready = await controller.ReadFrameAsync(
                timeout.Token);
            ValidateReady(ready.RootElement);
            controller.CredentialEnvironmentClean = ready.RootElement
                .GetProperty("credentialEnvironmentClean")
                .GetBoolean();
            return controller;
        }
        catch
        {
            await controller.DisposeAsync();
            throw;
        }
    }

    public async Task<JsonDocument> RequestAsync(
        string type,
        string id,
        CancellationToken cancellationToken)
    {
        return await SendRequestAsync(
            new { type, id },
            type,
            id,
            cancellationToken);
    }

    public async Task<JsonDocument> StartReadOnlySessionAsync(
        string workspaceRoot,
        string id,
        CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(workspaceRoot) ||
            !Directory.Exists(workspaceRoot))
        {
            throw new ArgumentException(
                "workspaceRoot must name an existing absolute directory.",
                nameof(workspaceRoot));
        }
        return await SendRequestAsync(
            new
            {
                type = "start_session",
                id,
                workspaceRoot,
            },
            "start_session",
            id,
            cancellationToken);
    }

    private async Task<JsonDocument> SendRequestAsync<TRequest>(
        TRequest request,
        string type,
        string id,
        CancellationToken cancellationToken)
    {
        if (shutdownCompleted)
        {
            throw new InvalidOperationException(
                "The Pi Agent sidecar has already shut down.");
        }
        if (string.IsNullOrWhiteSpace(type) ||
            string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "Request type and id must be non-empty.");
        }

        string payload = JsonSerializer.Serialize(
            request,
            SerializerOptions);
        if (Encoding.UTF8.GetByteCount(payload) > options.MaximumFrameBytes)
        {
            throw new InvalidOperationException(
                "The outgoing Pi Agent frame exceeds the contract limit.");
        }

        using CancellationTokenSource timeout =
            CreateTimeout(
                cancellationToken,
                options.RequestTimeoutMilliseconds);
        await process.StandardInput.WriteLineAsync(
            payload.AsMemory(),
            timeout.Token);
        await process.StandardInput.FlushAsync(timeout.Token);
        JsonDocument response = await ReadFrameAsync(timeout.Token);
        ValidateResponseEnvelope(response.RootElement, type, id);
        return response;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        if (shutdownCompleted)
        {
            return;
        }

        using JsonDocument response = await RequestAsync(
            "shutdown",
            "desktop-shutdown",
            cancellationToken);
        if (!response.RootElement.GetProperty("success").GetBoolean())
        {
            throw new InvalidOperationException(
                "The Pi Agent sidecar rejected orderly shutdown.");
        }

        process.StandardInput.Close();
        using CancellationTokenSource timeout =
            CreateTimeout(
                cancellationToken,
                options.ShutdownTimeoutMilliseconds);
        await process.WaitForExitAsync(timeout.Token);
        if (process.ExitCode != 0)
        {
            string stderr = await stderrTask;
            throw new InvalidOperationException(
                $"The Pi Agent sidecar exited with {process.ExitCode}: " +
                stderr.Trim());
        }
        shutdownCompleted = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited)
        {
            using CancellationTokenSource timeout = new(
                options.ShutdownTimeoutMilliseconds);
            try
            {
                await ShutdownAsync(timeout.Token);
            }
            catch (Exception exception)
                when (exception is OperationCanceledException or
                    InvalidOperationException or IOException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
        }
        process.Dispose();
    }

    private async Task<JsonDocument> ReadFrameAsync(
        CancellationToken cancellationToken)
    {
        string? line = await process.StandardOutput.ReadLineAsync(
            cancellationToken);
        if (line is null)
        {
            string stderr = process.HasExited
                ? await stderrTask
                : string.Empty;
            throw new InvalidOperationException(
                "The Pi Agent sidecar closed its output before a complete " +
                $"frame was received. {stderr.Trim()}");
        }
        if (Encoding.UTF8.GetByteCount(line) > options.MaximumFrameBytes)
        {
            throw new InvalidOperationException(
                "The incoming Pi Agent frame exceeds the contract limit.");
        }

        try
        {
            return JsonDocument.Parse(line);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The Pi Agent sidecar returned invalid JSON.",
                exception);
        }
    }

    private static void ValidateOptions(PiAgentSidecarOptions options)
    {
        if (!Path.IsPathFullyQualified(options.NodeExecutablePath) ||
            !File.Exists(options.NodeExecutablePath) ||
            !string.Equals(
                Path.GetFileName(options.NodeExecutablePath),
                "node.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "NodeExecutablePath must name an existing absolute node.exe.");
        }
        if (!Path.IsPathFullyQualified(options.HostScriptPath) ||
            !File.Exists(options.HostScriptPath) ||
            !string.Equals(
                Path.GetFileName(options.HostScriptPath),
                "host.mjs",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "HostScriptPath must name an existing absolute host.mjs.");
        }
        if (options.MaximumFrameBytes != 65_536 ||
            options.RequestTimeoutMilliseconds is < 1_000 or > 15_000 ||
            options.ShutdownTimeoutMilliseconds is < 1_000 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The sidecar limits do not match the reviewed desktop policy.");
        }
    }

    private static CancellationTokenSource CreateTimeout(
        CancellationToken cancellationToken,
        int timeoutMilliseconds)
    {
        CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        timeout.CancelAfter(timeoutMilliseconds);
        return timeout;
    }

    private static void ValidateReady(JsonElement ready)
    {
        bool valid =
            ready.GetProperty("type").GetString() == "ready" &&
            ready.GetProperty("protocol").GetString() == ContractId &&
            ready.GetProperty("package").GetString() == PackageName &&
            ready.GetProperty("version").GetString() == ExpectedVersion &&
            ready.GetProperty("credentialEnvironmentClean").GetBoolean() &&
            ready.GetProperty("sessionCreationEnabled").GetBoolean() &&
            !ready.GetProperty("promptingEnabled").GetBoolean();
        if (!valid)
        {
            throw new InvalidOperationException(
                "The Pi Agent sidecar ready frame failed admission.");
        }
    }

    private static void ValidateResponseEnvelope(
        JsonElement response,
        string expectedCommand,
        string expectedId)
    {
        bool valid =
            response.GetProperty("type").GetString() == "response" &&
            response.GetProperty("command").GetString() == expectedCommand &&
            response.GetProperty("id").GetString() == expectedId;
        if (!valid)
        {
            throw new InvalidOperationException(
                "The Pi Agent response envelope did not match its request.");
        }
    }
}

public static class PiAgentDesktopProbe
{
    public static async Task<PiAgentDesktopProbeReceipt> RunAsync(
        PiAgentSidecarOptions options,
        CancellationToken cancellationToken)
    {
        await using PiAgentSidecarController controller =
            await PiAgentSidecarController.StartAsync(
                options,
                cancellationToken);

        using JsonDocument hello = await controller.RequestAsync(
            "hello",
            "desktop-hello",
            cancellationToken);
        bool helloPassed =
            hello.RootElement.GetProperty("success").GetBoolean() &&
            hello.RootElement.GetProperty("protocol").GetString() ==
                PiAgentSidecarController.ContractId &&
            hello.RootElement.GetProperty("runtime").GetString() ==
                PiAgentSidecarController.ExpectedVersion;

        using JsonDocument capabilities = await controller.RequestAsync(
            "capabilities",
            "desktop-capabilities",
            cancellationToken);
        JsonElement capabilityData =
            capabilities.RootElement.GetProperty("data");
        string[] initialTools = capabilityData
            .GetProperty("initialTools")
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToArray();
        string[] deniedTools = capabilityData
            .GetProperty("deniedTools")
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToArray();
        bool capabilitiesPassed =
            capabilities.RootElement.GetProperty("success").GetBoolean() &&
            initialTools.SequenceEqual(["read", "grep", "find", "ls"]) &&
            deniedTools.SequenceEqual(["bash", "edit", "write"]) &&
            capabilityData
                .GetProperty("sessionCreationEnabled")
                .GetBoolean() &&
            !capabilityData
                .GetProperty("promptingEnabled")
                .GetBoolean() &&
            capabilityData
                .GetProperty("sessionPersistence")
                .GetString() == "in-memory" &&
            capabilityData
                .GetProperty("workspaceBinding")
                .GetString() == "single-explicit-root" &&
            !capabilityData
                .GetProperty("resourceDiscoveryEnabled")
                .GetBoolean() &&
            !capabilityData
                .GetProperty("modelNetworkAllowed")
                .GetBoolean() &&
            !capabilityData
                .GetProperty("credentialTransportAllowed")
                .GetBoolean() &&
            !capabilityData
                .GetProperty("shellMutationSupported")
                .GetBoolean() &&
            !capabilityData
                .GetProperty("explorerMutationSupported")
                .GetBoolean() &&
            !capabilityData
                .GetProperty("activationPermitted")
                .GetBoolean();

        string workspaceRoot =
            Directory.GetParent(options.HostScriptPath)?
                .Parent?.FullName
            ?? throw new InvalidOperationException(
                "The desktop probe workspace could not be resolved.");
        using JsonDocument admittedSession =
            await controller.StartReadOnlySessionAsync(
                workspaceRoot,
                "desktop-session-admission",
                cancellationToken);
        JsonElement sessionData =
            admittedSession.RootElement.GetProperty("data");
        string[] activeTools = sessionData
            .GetProperty("activeTools")
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToArray();
        bool sessionCreationPassed =
            admittedSession.RootElement
                .GetProperty("success")
                .GetBoolean() &&
            activeTools.SequenceEqual(["read", "grep", "find", "ls"]) &&
            !sessionData
                .GetProperty("sessionPersisted")
                .GetBoolean() &&
            !sessionData
                .GetProperty("promptingEnabled")
                .GetBoolean() &&
            !sessionData
                .GetProperty("resourceDiscoveryEnabled")
                .GetBoolean() &&
            !sessionData
                .GetProperty("modelNetworkAllowed")
                .GetBoolean();
        bool workspaceBound = string.Equals(
            Path.GetFullPath(workspaceRoot),
            sessionData.GetProperty("workspaceRoot").GetString(),
            StringComparison.OrdinalIgnoreCase);

        await controller.ShutdownAsync(cancellationToken);
        bool passed =
            helloPassed &&
            capabilitiesPassed &&
            sessionCreationPassed &&
            workspaceBound;

        return new PiAgentDesktopProbeReceipt(
            1,
            "jarvisv2-pi-agent-read-only-session-probe",
            passed ? "passed" : "failed",
            PiAgentSidecarController.ContractId,
            PiAgentSidecarController.PackageName,
            PiAgentSidecarController.ExpectedVersion,
            "sdk-sidecar-jsonl",
            true,
            true,
            helloPassed,
            capabilitiesPassed,
            sessionCreationPassed,
            workspaceBound,
            true,
            true,
            controller.CredentialEnvironmentClean,
            initialTools,
            deniedTools,
            true,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            "not-run",
            false);
    }
}

public static class PiAgentBridgeFaultProbe
{
    public static async Task<PiAgentBridgeFaultReceipt> RunAsync(
        string nodeExecutablePath,
        string fixtureRoot,
        CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(fixtureRoot);
        List<PiAgentBridgeFaultScenario> scenarios =
        [
            await ExpectAdmissionFailureAsync(
                "wrong-ready-rejected",
                nodeExecutablePath,
                Path.Combine(root, "wrong-ready", "host.mjs"),
                exception =>
                    exception is InvalidOperationException &&
                    exception.Message.Contains(
                        "ready frame failed admission",
                        StringComparison.Ordinal),
                cancellationToken),
            await ExpectAdmissionFailureAsync(
                "oversized-ready-rejected",
                nodeExecutablePath,
                Path.Combine(root, "oversized-ready", "host.mjs"),
                exception =>
                    exception is InvalidOperationException &&
                    exception.Message.Contains(
                        "frame exceeds the contract limit",
                        StringComparison.Ordinal),
                cancellationToken),
            await ExpectAdmissionFailureAsync(
                "hung-ready-times-out",
                nodeExecutablePath,
                Path.Combine(root, "hung-ready", "host.mjs"),
                exception => exception is OperationCanceledException,
                cancellationToken),
        ];
        int passedCount = scenarios.Count(scenario => scenario.Passed);
        return new PiAgentBridgeFaultReceipt(
            1,
            "jarvisv2-pi-agent-desktop-bridge-fault-probe",
            passedCount == scenarios.Count ? "passed" : "failed",
            scenarios.Count,
            passedCount,
            scenarios,
            true,
            false,
            false,
            false,
            false,
            "not-run",
            false);
    }

    private static async Task<PiAgentBridgeFaultScenario>
        ExpectAdmissionFailureAsync(
            string name,
            string nodeExecutablePath,
            string hostScriptPath,
            Func<Exception, bool> expected,
            CancellationToken cancellationToken)
    {
        try
        {
            PiAgentSidecarOptions options = new(
                Path.GetFullPath(nodeExecutablePath),
                Path.GetFullPath(hostScriptPath),
                RequestTimeoutMilliseconds: 1_000,
                ShutdownTimeoutMilliseconds: 1_000);
            await using PiAgentSidecarController controller =
                await PiAgentSidecarController.StartAsync(
                    options,
                    cancellationToken);
            return new PiAgentBridgeFaultScenario(
                name,
                false,
                "sidecar unexpectedly passed admission");
        }
        catch (Exception exception)
        {
            return new PiAgentBridgeFaultScenario(
                name,
                expected(exception),
                exception.GetType().Name);
        }
    }
}
