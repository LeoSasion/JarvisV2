namespace Jarvis.Win10.NeuralVoidPreview;

internal sealed record PreviewRenderReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    string Scope,
    string OutputPath,
    int Width,
    int Height,
    string AccentHex,
    string EffectId,
    double Phase,
    bool DesktopContainsDeviceUi,
    bool OwnProcessOnly,
    bool ShellMutationSupported,
    bool DeviceIntegrationSupported,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed,
    IReadOnlyList<string> Failures);
