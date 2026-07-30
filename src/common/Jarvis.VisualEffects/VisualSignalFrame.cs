namespace Jarvis.VisualEffects;

public static class VisualSignalContract
{
    public const int ContractVersion = 1;
    public const string ContractId = "jarvis-visual-signal-v1";
    public const string ColorSpace = "linear-srgb";
    public const string SharedRgbSource = "shared-rgb-frame";
    public const string SafetySource = "safety-fixed";

    public static readonly IReadOnlyList<string> SemanticChannelOrder =
    [
        "accent",
        "active",
        "pulse",
        "warning",
        "fault",
    ];
}

public sealed record LinearRgbColor(
    double Red,
    double Green,
    double Blue)
{
    public static LinearRgbColor FromSrgb(
        byte red,
        byte green,
        byte blue) =>
        new(
            Decode(red / 255.0),
            Decode(green / 255.0),
            Decode(blue / 255.0));

    private static double Decode(double value) =>
        value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
}

public sealed record SemanticVisualColor(
    string Id,
    LinearRgbColor Color,
    double Intensity,
    string Source);

public sealed record VisualSignalFrame(
    int ContractVersion,
    string ContractId,
    string ColorSpace,
    long Sequence,
    double MonotonicSeconds,
    double Phase,
    double TempoBpm,
    double Transition,
    RgbFrame Accent,
    IReadOnlyList<SemanticVisualColor> SemanticChannels,
    bool DeviceIoRequested);

public sealed record VisualSignalCompilationReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    int ContractVersion,
    long Sequence,
    int SemanticChannelCount,
    bool SharedAccentValidated,
    bool SafetyColorsIsolated,
    bool DeviceIoRequested,
    bool ReadyForOwnedProcessPrototype,
    bool ReadyForShellMutation,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed,
    VisualSignalFrame SafeFrame,
    IReadOnlyList<string> Failures);

public static class VisualSignalFrameFactory
{
    private static readonly LinearRgbColor WarningColor =
        LinearRgbColor.FromSrgb(0xFF, 0xB0, 0x00);

    private static readonly LinearRgbColor FaultColor =
        LinearRgbColor.FromSrgb(0xFF, 0x36, 0x5D);

    public static VisualSignalFrame Create(
        long sequence,
        double monotonicSeconds,
        double tempoBpm,
        double transition,
        RgbFrame accent)
    {
        LinearRgbColor accentColor =
            LinearRgbColor.FromSrgb(
                accent.Red,
                accent.Green,
                accent.Blue);
        return new VisualSignalFrame(
            VisualSignalContract.ContractVersion,
            VisualSignalContract.ContractId,
            VisualSignalContract.ColorSpace,
            sequence,
            monotonicSeconds,
            accent.Phase,
            tempoBpm,
            transition,
            accent,
            [
                new(
                    "accent",
                    accentColor,
                    1.0,
                    VisualSignalContract.SharedRgbSource),
                new(
                    "active",
                    accentColor,
                    1.0,
                    VisualSignalContract.SharedRgbSource),
                new(
                    "pulse",
                    accentColor,
                    accent.BrightnessScale,
                    VisualSignalContract.SharedRgbSource),
                new(
                    "warning",
                    WarningColor,
                    0.0,
                    VisualSignalContract.SafetySource),
                new(
                    "fault",
                    FaultColor,
                    0.0,
                    VisualSignalContract.SafetySource),
            ],
            false);
    }

    public static VisualSignalFrame CreateInactive()
    {
        RgbFrame accent = new(
            VisualSignalContract.ContractVersion,
            "static",
            0.0,
            0.0,
            0.0,
            0.0,
            0.0,
            0,
            0,
            0,
            "#000000");
        VisualSignalFrame frame =
            Create(0, 0.0, 0.0, 0.0, accent);
        return frame with
        {
            SemanticChannels = frame.SemanticChannels
                .Select(channel => channel with { Intensity = 0.0 })
                .ToArray(),
        };
    }

    internal static LinearRgbColor GetWarningColor() => WarningColor;

    internal static LinearRgbColor GetFaultColor() => FaultColor;
}

public static class VisualSignalFrameCompiler
{
    private const double Epsilon = 0.0000001;

    public static VisualSignalCompilationReceipt Compile(
        VisualSignalFrame frame)
    {
        List<string> failures = [];
        Require(
            frame.ContractVersion ==
                VisualSignalContract.ContractVersion &&
            frame.ContractId == VisualSignalContract.ContractId &&
            frame.ColorSpace == VisualSignalContract.ColorSpace,
            "visual-signal-identity-invalid",
            failures);
        Require(
            frame.Sequence >= 0 &&
            IsFiniteRange(frame.MonotonicSeconds, 0.0, double.MaxValue) &&
            IsFiniteRange(frame.Phase, 0.0, 1.0, maximumExclusive: true) &&
            IsFiniteRange(frame.TempoBpm, 0.0, 480.0) &&
            IsFiniteRange(frame.Transition, 0.0, 1.0),
            "visual-signal-timing-invalid",
            failures);
        Require(
            frame.Accent.ContractVersion ==
                VisualSignalContract.ContractVersion &&
            RgbEffectEngine.IsSupportedEffect(frame.Accent.EffectId) &&
            NearlyEqual(frame.Accent.Phase, frame.Phase) &&
            IsFiniteRange(frame.Accent.HueDegrees, 0.0, 360.0,
                maximumExclusive: true) &&
            IsFiniteRange(frame.Accent.Saturation, 0.0, 1.0) &&
            IsFiniteRange(frame.Accent.Value, 0.0, 1.0) &&
            IsFiniteRange(frame.Accent.BrightnessScale, 0.0, 1.0),
            "visual-signal-accent-invalid",
            failures);
        (byte expectedRed, byte expectedGreen, byte expectedBlue) =
            RgbEffectEngine.HsvToRgb(
                frame.Accent.HueDegrees,
                frame.Accent.Saturation,
                frame.Accent.Value);
        Require(
            frame.Accent.Red == expectedRed &&
            frame.Accent.Green == expectedGreen &&
            frame.Accent.Blue == expectedBlue &&
            frame.Accent.Hex ==
                $"#{expectedRed:X2}{expectedGreen:X2}{expectedBlue:X2}",
            "visual-signal-accent-encoding-invalid",
            failures);
        Require(
            !frame.DeviceIoRequested,
            "visual-signal-device-io-forbidden",
            failures);

        bool exactChannels = ValidateChannelSet(frame.SemanticChannels);
        Require(
            exactChannels,
            "visual-signal-channel-set-invalid",
            failures);

        LinearRgbColor expectedAccent =
            LinearRgbColor.FromSrgb(
                frame.Accent.Red,
                frame.Accent.Green,
                frame.Accent.Blue);
        bool sharedAccentValidated =
            exactChannels &&
            frame.SemanticChannels
                .Where(channel =>
                    channel.Id is "accent" or "active" or "pulse")
                .All(channel =>
                    channel.Source ==
                        VisualSignalContract.SharedRgbSource &&
                    EqualColor(channel.Color, expectedAccent));
        Require(
            sharedAccentValidated,
            "visual-signal-shared-accent-invalid",
            failures);

        bool safetyColorsIsolated =
            exactChannels &&
            MatchesSafetyChannel(
                frame.SemanticChannels,
                "warning",
                VisualSignalFrameFactory.GetWarningColor()) &&
            MatchesSafetyChannel(
                frame.SemanticChannels,
                "fault",
                VisualSignalFrameFactory.GetFaultColor());
        Require(
            safetyColorsIsolated,
            "visual-signal-safety-color-invalid",
            failures);

        bool channelsValid =
            frame.SemanticChannels.All(channel =>
                IsValidColor(channel.Color) &&
                IsFiniteRange(channel.Intensity, 0.0, 1.0));
        Require(
            channelsValid,
            "visual-signal-channel-value-invalid",
            failures);

        bool passed = failures.Count == 0;
        return new VisualSignalCompilationReceipt(
            1,
            "jarvisv2-visual-signal-frame-compilation",
            passed
                ? "admitted-owned-process-frame"
                : "blocked-inactive-frame",
            frame.ContractVersion,
            frame.Sequence,
            frame.SemanticChannels.Count,
            sharedAccentValidated,
            safetyColorsIsolated,
            frame.DeviceIoRequested,
            passed,
            false,
            false,
            "not-run",
            false,
            passed
                ? frame
                : VisualSignalFrameFactory.CreateInactive(),
            failures.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static bool ValidateChannelSet(
        IReadOnlyList<SemanticVisualColor> channels) =>
        channels.Count ==
            VisualSignalContract.SemanticChannelOrder.Count &&
        channels.Select(channel => channel.Id)
            .SequenceEqual(
                VisualSignalContract.SemanticChannelOrder,
                StringComparer.Ordinal) &&
        channels.Select(channel => channel.Id)
            .Distinct(StringComparer.Ordinal)
            .Count() == channels.Count;

    private static bool MatchesSafetyChannel(
        IEnumerable<SemanticVisualColor> channels,
        string id,
        LinearRgbColor expected) =>
        channels.SingleOrDefault(channel => channel.Id == id) is
            SemanticVisualColor channel &&
        channel.Source == VisualSignalContract.SafetySource &&
        EqualColor(channel.Color, expected);

    private static bool IsValidColor(LinearRgbColor color) =>
        IsFiniteRange(color.Red, 0.0, 1.0) &&
        IsFiniteRange(color.Green, 0.0, 1.0) &&
        IsFiniteRange(color.Blue, 0.0, 1.0);

    private static bool EqualColor(
        LinearRgbColor left,
        LinearRgbColor right) =>
        NearlyEqual(left.Red, right.Red) &&
        NearlyEqual(left.Green, right.Green) &&
        NearlyEqual(left.Blue, right.Blue);

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= Epsilon;

    private static bool IsFiniteRange(
        double value,
        double minimum,
        double maximum,
        bool maximumExclusive = false) =>
        double.IsFinite(value) &&
        value >= minimum &&
        (maximumExclusive ? value < maximum : value <= maximum);

    private static void Require(
        bool condition,
        string failure,
        ICollection<string> failures)
    {
        if (!condition)
        {
            failures.Add(failure);
        }
    }
}
