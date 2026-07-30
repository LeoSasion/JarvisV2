using Jarvis.VisualEffects;
using System.Windows.Media;

namespace Jarvis.Win10.NeuralVoidPreview;

internal static class Win10NeuralVectorSceneFactory
{
    private const double StructureOpacity = 120.0 / 255.0;
    private const double GhostOpacity = 52.0 / 255.0;
    private const double PlaneOpacity = 11.0 / 255.0;

    private static readonly VectorMaterial Structure =
        new(
            "neutral-structure",
            1.0,
            StructureOpacity,
            "alpha");

    private static readonly VectorMaterial Ghost =
        new(
            "neutral-ghost",
            1.0,
            GhostOpacity,
            "alpha");

    private static readonly VectorMaterial Plane =
        new(
            "neutral-plane",
            1.0,
            PlaneOpacity,
            "alpha");

    private static readonly VectorStroke Hairline =
        new(1.0, "square", "round", []);

    public static RetainedVectorScene CreateStaticScene()
    {
        List<VectorCommand> commands =
        [
            new VectorPlaneCommand(
                "upper-ghost-plane",
                100,
                10,
                "static",
                Plane,
                [
                    new(952.5, 60.5),
                    new(1547.5, 60.5),
                    new(1547.5, 86.5),
                    new(978.5, 86.5),
                ]),
            new VectorPlaneCommand(
                "lower-ghost-plane",
                100,
                20,
                "static",
                Plane,
                [
                    new(40.5, 851.5),
                    new(936.5, 851.5),
                    new(914.5, 877.5),
                    new(40.5, 877.5),
                ]),
        ];

        AddSplitLine(
            commands,
            "upper-horizontal-datum",
            new(63.5, 40.5),
            new(1547.5, 40.5),
            0.58,
            18.0,
            10);
        AddSplitLine(
            commands,
            "left-vertical-datum",
            new(40.5, 60.5),
            new(40.5, 851.5),
            0.64,
            18.0,
            30);
        AddSplitLine(
            commands,
            "lower-left-datum",
            new(63.5, 877.5),
            new(936.5, 877.5),
            0.52,
            18.0,
            50);
        AddSplitLine(
            commands,
            "lower-right-datum",
            new(952.5, 877.5),
            new(1567.5, 877.5),
            0.48,
            18.0,
            70);

        commands.Add(
            new VectorPolylineCommand(
                "lower-aperture-join",
                200,
                90,
                "static",
                Structure,
                [
                    new(40.5, 826.5),
                    new(40.5, 851.5),
                    new(63.5, 877.5),
                    new(82.5, 877.5),
                ],
                Hairline));
        commands.Add(
            new VectorPolylineCommand(
                "upper-aperture-join",
                200,
                100,
                "static",
                Ghost,
                [
                    new(952.5, 60.5),
                    new(978.5, 60.5),
                    new(992.5, 78.5),
                    new(1020.5, 78.5),
                ],
                Hairline));
        commands.Add(
            new VectorLineCommand(
                "right-vertical-datum",
                200,
                110,
                "static",
                Ghost,
                new(1567.5, 60.5),
                new(1567.5, 548.5),
                Hairline));
        commands.Add(
            new VectorLineCommand(
                "right-registration-tick",
                200,
                120,
                "static",
                Structure,
                new(1555.5, 548.5),
                new(1567.5, 548.5),
                Hairline));

        return new RetainedVectorScene(
            RetainedVectorSceneContract.ContractVersion,
            RetainedVectorSceneContract.ContractId,
            "win10-neural-void-static-vector-v1",
            1,
            1600.0,
            900.0,
            "balanced",
            RetainedVectorSceneContract.GetRequiredBudget("balanced"),
            RetainedVectorSceneContract.VisualSignalBinding,
            commands,
            false,
            false);
    }

    public static IReadOnlyDictionary<string, Color> CreatePalette() =>
        new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["neutral-structure"] =
                Color.FromRgb(0x2D, 0x3A, 0x38),
            ["neutral-ghost"] =
                Color.FromRgb(0x2D, 0x3A, 0x38),
            ["neutral-plane"] =
                Color.FromRgb(0xD7, 0xF8, 0xEC),
            ["accent"] =
                Color.FromRgb(0x00, 0xFF, 0x9A),
            ["active"] =
                Color.FromRgb(0x00, 0xFF, 0x9A),
            ["pulse"] =
                Color.FromRgb(0x00, 0xFF, 0x9A),
            ["warning"] =
                Color.FromRgb(0xFF, 0xB0, 0x00),
            ["fault"] =
                Color.FromRgb(0xFF, 0x36, 0x5D),
        };

    private static void AddSplitLine(
        ICollection<VectorCommand> commands,
        string id,
        VectorPoint start,
        VectorPoint end,
        double splitProgress,
        double gapLength,
        int order)
    {
        double deltaX = end.X - start.X;
        double deltaY = end.Y - start.Y;
        double length =
            Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (length <= gapLength)
        {
            return;
        }

        double unitX = deltaX / length;
        double unitY = deltaY / length;
        VectorPoint center =
            new(
                start.X + (deltaX * splitProgress),
                start.Y + (deltaY * splitProgress));
        double halfGap = gapLength / 2.0;
        VectorPoint firstEnd =
            new(
                center.X - (unitX * halfGap),
                center.Y - (unitY * halfGap));
        VectorPoint secondStart =
            new(
                center.X + (unitX * halfGap),
                center.Y + (unitY * halfGap));
        commands.Add(
            new VectorLineCommand(
                $"{id}-before-gap",
                200,
                order,
                "static",
                Ghost,
                start,
                firstEnd,
                Hairline));
        commands.Add(
            new VectorLineCommand(
                $"{id}-after-gap",
                200,
                order + 10,
                "static",
                Ghost,
                secondStart,
                end,
                Hairline));
    }
}
