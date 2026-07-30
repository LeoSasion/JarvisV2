using System.Windows.Media;
using Jarvis.VisualEffects;

namespace Jarvis.Win10.NeuralVoidPreview;

internal sealed record Win10ApertureVectorSceneInputs(
    RetainedVectorScene Scene,
    IReadOnlyDictionary<string, Color> Palette);

internal static class Win10ApertureVectorSceneFactory
{
    private static readonly VectorStroke Hairline =
        new(1.0, "square", "round", []);

    public static bool TryCreate(
        double width,
        double height,
        double cornerRadius,
        double cornerLength,
        Brush lineBrush,
        out Win10ApertureVectorSceneInputs? inputs)
    {
        inputs = null;
        if (!IsFiniteRange(width, 4.0, 32768.0) ||
            !IsFiniteRange(height, 4.0, 32768.0) ||
            !IsFiniteRange(cornerRadius, 0.0, 32768.0) ||
            !IsFiniteRange(cornerLength, 0.0, 32768.0) ||
            lineBrush is not SolidColorBrush solidColorBrush ||
            !IsFiniteRange(solidColorBrush.Opacity, 0.0, 1.0))
        {
            return false;
        }

        double radius =
            Math.Min(
                cornerRadius,
                Math.Min(
                    (width - 1.0) / 4.0,
                    (height - 1.0) / 4.0));
        double length =
            Math.Min(
                cornerLength,
                Math.Min(
                    (width - 1.0) / 5.0,
                    (height - 1.0) / 5.0));
        double left = 0.5;
        double top = 0.5;
        double right = width - 0.5;
        double bottom = height - 0.5;
        List<VectorPathFigure> figures = [];

        AddTangentCorner(
            figures,
            new(left, top + radius + length),
            new(left, top + radius),
            new(left + radius, top),
            new(left + radius + length, top),
            radius);
        AddTangentCorner(
            figures,
            new(right - radius - length, top),
            new(right - radius, top),
            new(right, top + radius),
            new(right, top + radius + length),
            radius);
        AddTangentCorner(
            figures,
            new(right, bottom - radius - length),
            new(right, bottom - radius),
            new(right - radius, bottom),
            new(right - radius - length, bottom),
            radius);
        AddTangentCorner(
            figures,
            new(left + radius + length, bottom),
            new(left + radius, bottom),
            new(left, bottom - radius),
            new(left, bottom - radius - length),
            radius);

        AddSplitEdge(
            figures,
            new(left + radius + length, top),
            new(right - radius - length, top));
        AddSplitEdge(
            figures,
            new(right, top + radius + length),
            new(right, bottom - radius - length));
        AddSplitEdge(
            figures,
            new(right - radius - length, bottom),
            new(left + radius + length, bottom));
        AddSplitEdge(
            figures,
            new(left, bottom - radius - length),
            new(left, top + radius + length));

        Color source = solidColorBrush.Color;
        VectorMaterial material =
            new(
                "neutral-structure",
                1.0,
                (source.A / 255.0) *
                    solidColorBrush.Opacity,
                "alpha");
        IReadOnlyList<VectorCommand> commands =
            figures.Count == 0
                ? []
                :
                [
                    new VectorPathCommand(
                        "aperture-contour",
                        200,
                        10,
                        "static",
                        material,
                        figures,
                        Hairline),
                ];
        RetainedVectorScene scene =
            new(
                RetainedVectorSceneContract.ContractVersion,
                RetainedVectorSceneContract.ContractId,
                "win10-aperture-contour-v1",
                1,
                width,
                height,
                "low-power",
                RetainedVectorSceneContract.GetRequiredBudget(
                    "low-power"),
                RetainedVectorSceneContract.VisualSignalBinding,
                commands,
                false,
                false);
        IReadOnlyDictionary<string, Color> palette =
            new Dictionary<string, Color>(StringComparer.Ordinal)
            {
                ["neutral-structure"] =
                    Color.FromRgb(
                        source.R,
                        source.G,
                        source.B),
            };
        inputs =
            new Win10ApertureVectorSceneInputs(
                scene,
                palette);
        return true;
    }

    private static void AddTangentCorner(
        ICollection<VectorPathFigure> figures,
        VectorPoint start,
        VectorPoint arcStart,
        VectorPoint arcEnd,
        VectorPoint end,
        double radius)
    {
        List<VectorPathSegment> segments = [];
        VectorPoint current = start;
        AddLine(segments, ref current, arcStart);
        if (current != arcEnd && radius > 0.0)
        {
            segments.Add(
                new VectorPathArcSegment(
                    arcEnd,
                    radius,
                    radius,
                    0.0,
                    false,
                    "clockwise"));
            current = arcEnd;
        }
        AddLine(segments, ref current, end);
        if (segments.Count != 0)
        {
            figures.Add(new(start, segments, false));
        }
    }

    private static void AddSplitEdge(
        ICollection<VectorPathFigure> figures,
        VectorPoint start,
        VectorPoint end)
    {
        double deltaX = end.X - start.X;
        double deltaY = end.Y - start.Y;
        double length =
            Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (length < 24.0)
        {
            return;
        }

        VectorPoint firstEnd =
            new(
                start.X + (deltaX * 0.47),
                start.Y + (deltaY * 0.47));
        VectorPoint secondStart =
            new(
                start.X + (deltaX * 0.53),
                start.Y + (deltaY * 0.53));
        figures.Add(
            new(
                start,
                [new VectorPathLineSegment(firstEnd)],
                false));
        figures.Add(
            new(
                secondStart,
                [new VectorPathLineSegment(end)],
                false));
    }

    private static void AddLine(
        ICollection<VectorPathSegment> segments,
        ref VectorPoint current,
        VectorPoint end)
    {
        if (current == end)
        {
            return;
        }

        segments.Add(new VectorPathLineSegment(end));
        current = end;
    }

    private static bool IsFiniteRange(
        double value,
        double minimum,
        double maximum) =>
        double.IsFinite(value) &&
        value >= minimum &&
        value <= maximum;
}
