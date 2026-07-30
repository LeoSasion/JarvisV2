using System.Text.Json.Serialization;

namespace Jarvis.VisualEffects;

public static class RetainedVectorSceneContract
{
    public const int ContractVersion = 1;
    public const string ContractId = "jarvis-retained-vector-scene-v1";
    public const string VisualSignalBinding = "jarvis-visual-signal-v1";

    public static readonly IReadOnlySet<string> ColorChannels =
        new HashSet<string>(
            [
                "neutral-structure",
                "neutral-ghost",
                "neutral-plane",
                "accent",
                "active",
                "pulse",
                "warning",
                "fault",
            ],
            StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> SharedSignalChannels =
        new HashSet<string>(
            ["accent", "active", "pulse", "warning", "fault"],
            StringComparer.Ordinal);

    public static VectorSceneBudget GetRequiredBudget(
        string qualityProfile) =>
        qualityProfile switch
        {
            "low-power" => new(512, 4096, 256, 64),
            "balanced" => new(2048, 16384, 1024, 256),
            "cinematic-preview" => new(8192, 65536, 4096, 1024),
            _ => new(0, 0, 0, 0),
        };
}

public sealed record VectorPoint(
    double X,
    double Y);

public sealed record VectorMaterial(
    string ColorChannel,
    double Luminance,
    double Opacity,
    string BlendMode);

public sealed record VectorStroke(
    double Width,
    string LineCap,
    string LineJoin,
    IReadOnlyList<double> DashPattern);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$primitive")]
[JsonDerivedType(typeof(VectorPointCommand), "point")]
[JsonDerivedType(typeof(VectorLineCommand), "line")]
[JsonDerivedType(typeof(VectorPolylineCommand), "polyline")]
[JsonDerivedType(typeof(VectorArcCommand), "arc")]
[JsonDerivedType(typeof(VectorPlaneCommand), "plane")]
public abstract record VectorCommand(
    string Id,
    int Layer,
    int Order,
    string UpdateClass,
    VectorMaterial Material)
{
    public abstract string Kind { get; }

    public abstract int VertexCount { get; }
}

public sealed record VectorPointCommand(
    string Id,
    int Layer,
    int Order,
    string UpdateClass,
    VectorMaterial Material,
    VectorPoint Center,
    double Radius)
    : VectorCommand(
        Id,
        Layer,
        Order,
        UpdateClass,
        Material)
{
    public override string Kind => "point";

    public override int VertexCount => 1;
}

public sealed record VectorLineCommand(
    string Id,
    int Layer,
    int Order,
    string UpdateClass,
    VectorMaterial Material,
    VectorPoint Start,
    VectorPoint End,
    VectorStroke Stroke)
    : VectorCommand(
        Id,
        Layer,
        Order,
        UpdateClass,
        Material)
{
    public override string Kind => "line";

    public override int VertexCount => 2;
}

public sealed record VectorPolylineCommand(
    string Id,
    int Layer,
    int Order,
    string UpdateClass,
    VectorMaterial Material,
    IReadOnlyList<VectorPoint> Points,
    VectorStroke Stroke)
    : VectorCommand(
        Id,
        Layer,
        Order,
        UpdateClass,
        Material)
{
    public override string Kind => "polyline";

    public override int VertexCount => Points.Count;
}

public sealed record VectorArcCommand(
    string Id,
    int Layer,
    int Order,
    string UpdateClass,
    VectorMaterial Material,
    VectorPoint Start,
    VectorPoint End,
    double RadiusX,
    double RadiusY,
    double RotationDegrees,
    bool LargeArc,
    string Sweep,
    VectorStroke Stroke)
    : VectorCommand(
        Id,
        Layer,
        Order,
        UpdateClass,
        Material)
{
    public override string Kind => "arc";

    public override int VertexCount => 2;
}

public sealed record VectorPlaneCommand(
    string Id,
    int Layer,
    int Order,
    string UpdateClass,
    VectorMaterial Material,
    IReadOnlyList<VectorPoint> Points)
    : VectorCommand(
        Id,
        Layer,
        Order,
        UpdateClass,
        Material)
{
    public override string Kind => "plane";

    public override int VertexCount => Points.Count;
}

public sealed record VectorSceneBudget(
    int MaxCommands,
    int MaxVertices,
    int MaxArcs,
    int MaxPlanes);

public sealed record RetainedVectorScene(
    int ContractVersion,
    string ContractId,
    string SceneId,
    long Revision,
    double DesignWidth,
    double DesignHeight,
    string QualityProfile,
    VectorSceneBudget Budget,
    string VisualSignalBinding,
    IReadOnlyList<VectorCommand> Commands,
    bool BitmapResourcesRequested,
    bool RuntimeEffectsRequested);

public sealed record VectorSceneCompilationReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    string SceneId,
    long Revision,
    string QualityProfile,
    int CommandCount,
    int VertexCount,
    int PointCount,
    int LineCount,
    int PolylineCount,
    int ArcCount,
    int PlaneCount,
    int StaticCommandCount,
    int PerFrameCommandCount,
    int SharedSignalCommandCount,
    bool DeterministicOrderValidated,
    bool BudgetValidated,
    bool BitmapResourcesRequested,
    bool RuntimeEffectsRequested,
    bool ReadyForOwnedProcessPrototype,
    bool ReadyForShellMutation,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed,
    RetainedVectorScene SafeScene,
    IReadOnlyList<string> Failures);

public static class RetainedVectorSceneFactory
{
    private static readonly VectorMaterial Structure =
        new("neutral-structure", 0.22, 0.48, "alpha");

    private static readonly VectorMaterial Ghost =
        new("neutral-ghost", 0.18, 0.22, "alpha");

    private static readonly VectorMaterial Plane =
        new("neutral-plane", 0.08, 0.12, "alpha");

    private static readonly VectorMaterial Pulse =
        new("pulse", 1.0, 1.0, "alpha");

    private static readonly VectorStroke Hairline =
        new(1.0, "square", "round", []);

    public static RetainedVectorScene CreateContractProbe() =>
        new(
            RetainedVectorSceneContract.ContractVersion,
            RetainedVectorSceneContract.ContractId,
            "neural-void-vector-contract-probe-v1",
            1,
            1600.0,
            900.0,
            "balanced",
            RetainedVectorSceneContract.GetRequiredBudget("balanced"),
            RetainedVectorSceneContract.VisualSignalBinding,
            [
                new VectorPlaneCommand(
                    "background-plane",
                    100,
                    10,
                    "static",
                    Plane,
                    [
                        new(960.0, 60.0),
                        new(1548.0, 60.0),
                        new(1548.0, 86.0),
                        new(986.0, 86.0),
                    ]),
                new VectorLineCommand(
                    "horizontal-datum",
                    200,
                    10,
                    "static",
                    Ghost,
                    new(64.0, 40.0),
                    new(1548.0, 40.0),
                    Hairline),
                new VectorPolylineCommand(
                    "aperture-join",
                    200,
                    20,
                    "static",
                    Structure,
                    [
                        new(40.0, 826.0),
                        new(40.0, 852.0),
                        new(64.0, 878.0),
                        new(82.0, 878.0),
                    ],
                    Hairline),
                new VectorArcCommand(
                    "tangent-corner",
                    200,
                    30,
                    "static",
                    Structure,
                    new(952.0, 76.0),
                    new(968.0, 60.0),
                    16.0,
                    16.0,
                    0.0,
                    false,
                    "clockwise",
                    Hairline),
                new VectorPointCommand(
                    "focus-junction",
                    300,
                    10,
                    "per-frame",
                    Pulse,
                    new(186.5, 78.5),
                    2.2),
            ],
            false,
            false);

    public static RetainedVectorScene CreateEmptySafeScene(
        double designWidth,
        double designHeight) =>
        new(
            RetainedVectorSceneContract.ContractVersion,
            RetainedVectorSceneContract.ContractId,
            "empty-safe-scene",
            1,
            SafeDimension(designWidth),
            SafeDimension(designHeight),
            "low-power",
            RetainedVectorSceneContract.GetRequiredBudget("low-power"),
            RetainedVectorSceneContract.VisualSignalBinding,
            [],
            false,
            false);

    private static double SafeDimension(double value) =>
        double.IsFinite(value)
            ? Math.Clamp(value, 1.0, 32768.0)
            : 1.0;
}

public static class RetainedVectorSceneCompiler
{
    public static VectorSceneCompilationReceipt Compile(
        RetainedVectorScene scene)
    {
        List<string> failures = [];
        Require(
            scene.ContractVersion ==
                RetainedVectorSceneContract.ContractVersion &&
            scene.ContractId ==
                RetainedVectorSceneContract.ContractId &&
            IsStableId(scene.SceneId) &&
            scene.Revision >= 1,
            "vector-scene-identity-invalid",
            failures);
        Require(
            IsFiniteRange(
                scene.DesignWidth,
                1.0,
                32768.0) &&
            IsFiniteRange(
                scene.DesignHeight,
                1.0,
                32768.0),
            "vector-scene-design-space-invalid",
            failures);
        Require(
            scene.VisualSignalBinding ==
                RetainedVectorSceneContract.VisualSignalBinding,
            "vector-scene-visual-signal-binding-invalid",
            failures);
        Require(
            !scene.BitmapResourcesRequested,
            "vector-scene-bitmap-resource-forbidden",
            failures);
        Require(
            !scene.RuntimeEffectsRequested,
            "vector-scene-runtime-effect-forbidden",
            failures);

        VectorSceneBudget requiredBudget =
            RetainedVectorSceneContract.GetRequiredBudget(
                scene.QualityProfile);
        bool knownProfile = requiredBudget.MaxCommands > 0;
        bool exactBudget =
            knownProfile &&
            scene.Budget == requiredBudget;
        Require(
            exactBudget,
            "vector-scene-quality-budget-invalid",
            failures);

        bool uniqueIds =
            scene.Commands
                .Select(command => command.Id)
                .Distinct(StringComparer.Ordinal)
                .Count() == scene.Commands.Count &&
            scene.Commands.All(command => IsStableId(command.Id));
        Require(
            uniqueIds,
            "vector-scene-command-id-invalid",
            failures);

        VectorCommand[] expectedOrder =
            scene.Commands
                .OrderBy(command => command.Layer)
                .ThenBy(command => command.Order)
                .ThenBy(command => command.Id, StringComparer.Ordinal)
                .ToArray();
        bool deterministicOrder =
            scene.Commands.SequenceEqual(expectedOrder);
        Require(
            deterministicOrder,
            "vector-scene-command-order-invalid",
            failures);

        foreach (VectorCommand command in scene.Commands)
        {
            ValidateCommand(
                command,
                scene.DesignWidth,
                scene.DesignHeight,
                failures);
        }

        int commandCount = scene.Commands.Count;
        int vertexCount =
            scene.Commands.Sum(command => command.VertexCount);
        int pointCount =
            scene.Commands.Count(command => command.Kind == "point");
        int lineCount =
            scene.Commands.Count(command => command.Kind == "line");
        int polylineCount =
            scene.Commands.Count(command => command.Kind == "polyline");
        int arcCount =
            scene.Commands.Count(command => command.Kind == "arc");
        int planeCount =
            scene.Commands.Count(command => command.Kind == "plane");
        int staticCount =
            scene.Commands.Count(command =>
                command.UpdateClass == "static");
        int perFrameCount =
            scene.Commands.Count(command =>
                command.UpdateClass == "per-frame");
        int sharedSignalCount =
            scene.Commands.Count(command =>
                RetainedVectorSceneContract.SharedSignalChannels.Contains(
                    command.Material.ColorChannel));
        bool budgetValidated =
            exactBudget &&
            commandCount <= scene.Budget.MaxCommands &&
            vertexCount <= scene.Budget.MaxVertices &&
            arcCount <= scene.Budget.MaxArcs &&
            planeCount <= scene.Budget.MaxPlanes;
        Require(
            budgetValidated,
            "vector-scene-budget-exceeded",
            failures);

        bool passed = failures.Count == 0;
        return new VectorSceneCompilationReceipt(
            1,
            "jarvisv2-retained-vector-scene-compilation",
            passed
                ? "compiled-retained-vector-scene"
                : "blocked-empty-vector-scene",
            scene.SceneId,
            scene.Revision,
            scene.QualityProfile,
            commandCount,
            vertexCount,
            pointCount,
            lineCount,
            polylineCount,
            arcCount,
            planeCount,
            staticCount,
            perFrameCount,
            sharedSignalCount,
            deterministicOrder,
            budgetValidated,
            scene.BitmapResourcesRequested,
            scene.RuntimeEffectsRequested,
            passed,
            false,
            false,
            "not-run",
            false,
            passed
                ? scene
                : RetainedVectorSceneFactory.CreateEmptySafeScene(
                    scene.DesignWidth,
                    scene.DesignHeight),
            failures.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static void ValidateCommand(
        VectorCommand command,
        double designWidth,
        double designHeight,
        ICollection<string> failures)
    {
        string failure = $"vector-command-invalid:{command.Id}";
        if (command.Layer < 0 ||
            command.Order < 0 ||
            command.UpdateClass is not ("static" or "per-frame") ||
            !ValidateMaterial(command.Material))
        {
            failures.Add(failure);
            return;
        }

        bool valid = command switch
        {
            VectorPointCommand point =>
                IsPointInDesignSpace(
                    point.Center,
                    designWidth,
                    designHeight) &&
                IsFiniteRange(
                    point.Radius,
                    0.25,
                    Math.Min(designWidth, designHeight)),
            VectorLineCommand line =>
                IsPointInDesignSpace(
                    line.Start,
                    designWidth,
                    designHeight) &&
                IsPointInDesignSpace(
                    line.End,
                    designWidth,
                    designHeight) &&
                line.Start != line.End &&
                ValidateStroke(line.Stroke),
            VectorPolylineCommand polyline =>
                polyline.Points.Count >= 2 &&
                polyline.Points.Count <= 4096 &&
                polyline.Points.All(point =>
                    IsPointInDesignSpace(
                        point,
                        designWidth,
                        designHeight)) &&
                HasAtLeastTwoDistinctPoints(polyline.Points) &&
                ValidateStroke(polyline.Stroke),
            VectorArcCommand arc =>
                IsPointInDesignSpace(
                    arc.Start,
                    designWidth,
                    designHeight) &&
                IsPointInDesignSpace(
                    arc.End,
                    designWidth,
                    designHeight) &&
                arc.Start != arc.End &&
                IsFiniteRange(
                    arc.RadiusX,
                    0.25,
                    designWidth) &&
                IsFiniteRange(
                    arc.RadiusY,
                    0.25,
                    designHeight) &&
                IsFiniteRange(
                    arc.RotationDegrees,
                    -360.0,
                    360.0) &&
                arc.Sweep is "clockwise" or "counter-clockwise" &&
                ValidateStroke(arc.Stroke),
            VectorPlaneCommand plane =>
                plane.Points.Count >= 3 &&
                plane.Points.Count <= 4096 &&
                plane.Points.All(point =>
                    IsPointInDesignSpace(
                        point,
                        designWidth,
                        designHeight)) &&
                Math.Abs(SignedArea(plane.Points)) >= 0.0001,
            _ => false,
        };
        if (!valid)
        {
            failures.Add(failure);
        }
    }

    private static bool ValidateMaterial(VectorMaterial material) =>
        RetainedVectorSceneContract.ColorChannels.Contains(
            material.ColorChannel) &&
        IsFiniteRange(material.Luminance, 0.0, 1.0) &&
        IsFiniteRange(material.Opacity, 0.0, 1.0) &&
        material.BlendMode == "alpha";

    private static bool ValidateStroke(VectorStroke stroke) =>
        IsFiniteRange(stroke.Width, 0.25, 64.0) &&
        stroke.LineCap is "butt" or "square" or "round" &&
        stroke.LineJoin is "miter" or "bevel" or "round" &&
        stroke.DashPattern.Count <= 16 &&
        stroke.DashPattern.All(value =>
            IsFiniteRange(value, 0.01, 4096.0));

    private static bool IsPointInDesignSpace(
        VectorPoint point,
        double designWidth,
        double designHeight) =>
        IsFiniteRange(point.X, 0.0, designWidth) &&
        IsFiniteRange(point.Y, 0.0, designHeight);

    private static bool HasAtLeastTwoDistinctPoints(
        IReadOnlyList<VectorPoint> points) =>
        points.Skip(1).Any(point => point != points[0]);

    private static double SignedArea(
        IReadOnlyList<VectorPoint> points)
    {
        double twiceArea = 0.0;
        for (int index = 0; index < points.Count; index++)
        {
            VectorPoint current = points[index];
            VectorPoint next = points[(index + 1) % points.Count];
            twiceArea +=
                (current.X * next.Y) -
                (next.X * current.Y);
        }
        return twiceArea / 2.0;
    }

    private static bool IsStableId(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 96 &&
        value[0] is >= 'a' and <= 'z' &&
        value.All(character =>
            character is >= 'a' and <= 'z' ||
            character is >= '0' and <= '9' ||
            character == '-');

    private static bool IsFiniteRange(
        double value,
        double minimum,
        double maximum) =>
        double.IsFinite(value) &&
        value >= minimum &&
        value <= maximum;

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
