using System.Windows.Media;
using Jarvis.VisualEffects;

namespace Jarvis.Win10.NeuralVoidPreview;

internal sealed record Win10ApertureVectorSceneInputs(
    RetainedVectorScene Scene,
    IReadOnlyDictionary<string, Color> Palette);

internal static class Win10ApertureVectorSceneFactory
{
    private static readonly VectorStroke Hairline =
        new(1.0, "square", "miter", []);

    public static bool TryCreate(
        double width,
        double height,
        double cornerLength,
        Brush lineBrush,
        out Win10ApertureVectorSceneInputs? inputs)
    {
        inputs = null;
        if (!IsFiniteRange(width, 4.0, 32768.0) ||
            !IsFiniteRange(height, 4.0, 32768.0) ||
            !IsFiniteRange(cornerLength, 0.0, 32768.0) ||
            lineBrush is not SolidColorBrush solidColorBrush ||
            !IsFiniteRange(solidColorBrush.Opacity, 0.0, 1.0))
        {
            return false;
        }

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

        AddOrthogonalCorner(
            figures,
            new(left, top + length),
            new(left, top),
            new(left + length, top));
        AddOrthogonalCorner(
            figures,
            new(right - length, top),
            new(right, top),
            new(right, top + length));
        AddOrthogonalCorner(
            figures,
            new(right, bottom - length),
            new(right, bottom),
            new(right - length, bottom));
        AddOrthogonalCorner(
            figures,
            new(left + length, bottom),
            new(left, bottom),
            new(left, bottom - length));

        AddSplitEdge(
            figures,
            new(left + length, top),
            new(right - length, top));
        AddSplitEdge(
            figures,
            new(right, top + length),
            new(right, bottom - length));
        AddSplitEdge(
            figures,
            new(right - length, bottom),
            new(left + length, bottom));
        AddSplitEdge(
            figures,
            new(left, bottom - length),
            new(left, top + length));

        Color source = solidColorBrush.Color;
        VectorMaterial material =
            CreateMaterial(
                "neutral-structure",
                source,
                solidColorBrush.Opacity,
                1.0);
        List<VectorCommand> commands = [];
        if (figures.Count != 0)
        {
            commands.Add(
                new VectorPathCommand(
                    "aperture-contour",
                    200,
                    10,
                    "static",
                    material,
                    figures,
                    Hairline));
        }
        AddRegistrationSquare(
            commands,
            "registration-top-left",
            new(left + 4.0, top + 4.0),
            20,
            material);
        AddRegistrationSquare(
            commands,
            "registration-top-right",
            new(right - 4.0, top + 4.0),
            30,
            material);
        AddRegistrationSquare(
            commands,
            "registration-bottom-right",
            new(right - 4.0, bottom - 4.0),
            40,
            material);
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
            CreatePalette("neutral-structure", source);
        inputs =
            new Win10ApertureVectorSceneInputs(
                scene,
                palette);
        return true;
    }

    public static bool TryCreateFocus(
        double width,
        double height,
        ApertureFocusCorner focusCorner,
        Brush accentBrush,
        out Win10ApertureVectorSceneInputs? inputs)
    {
        inputs = null;
        if (!IsFiniteRange(width, 4.0, 32768.0) ||
            !IsFiniteRange(height, 4.0, 32768.0) ||
            !Enum.IsDefined(focusCorner) ||
            accentBrush is not
                SolidColorBrush solidColorBrush ||
            !IsFiniteRange(
                solidColorBrush.Opacity,
                0.0,
                1.0))
        {
            return false;
        }

        Color source = solidColorBrush.Color;
        VectorMaterial accent =
            CreateMaterial(
                "accent",
                source,
                solidColorBrush.Opacity,
                1.0);
        List<VectorCommand> commands = [];
        if (focusCorner != ApertureFocusCorner.None)
        {
            VectorPoint focus =
                focusCorner switch
                {
                    ApertureFocusCorner.TopLeft =>
                        new(0.5, 0.5),
                    ApertureFocusCorner.TopRight =>
                        new(width - 0.5, 0.5),
                    ApertureFocusCorner.BottomLeft =>
                        new(0.5, height - 0.5),
                    ApertureFocusCorner.BottomRight =>
                        new(width - 0.5, height - 0.5),
                    _ => throw new InvalidOperationException(
                        "Unsupported aperture focus corner."),
                };
            double horizontalDirection =
                focusCorner is
                    ApertureFocusCorner.TopLeft or
                    ApertureFocusCorner.BottomLeft
                    ? 1.0
                    : -1.0;
            double verticalDirection =
                focusCorner is
                    ApertureFocusCorner.TopLeft or
                    ApertureFocusCorner.TopRight
                    ? 1.0
                    : -1.0;
            double horizontalLength =
                Math.Min(122.0, width * 0.34);
            double verticalLength =
                Math.Min(82.0, height * 0.28);

            commands.Add(
                new VectorLineCommand(
                    "focus-horizontal-ray",
                    300,
                    10,
                    "per-frame",
                    accent,
                    focus,
                    new(
                        focus.X +
                            (horizontalDirection *
                                horizontalLength),
                        focus.Y),
                    Hairline));
            commands.Add(
                new VectorLineCommand(
                    "focus-vertical-ray",
                    300,
                    20,
                    "per-frame",
                    accent,
                    focus,
                    new(
                        focus.X,
                        focus.Y +
                            (verticalDirection *
                                verticalLength)),
                    Hairline));
            commands.Add(
                new VectorLineCommand(
                    "focus-horizontal-cross",
                    300,
                    30,
                    "per-frame",
                    accent,
                    new(focus.X - 6.0, focus.Y),
                    new(focus.X + 6.0, focus.Y),
                    Hairline));
            commands.Add(
                new VectorLineCommand(
                    "focus-vertical-cross",
                    300,
                    40,
                    "per-frame",
                    accent,
                    new(focus.X, focus.Y - 6.0),
                    new(focus.X, focus.Y + 6.0),
                    Hairline));

            VectorMaterial registration =
                CreateMaterial(
                    "accent",
                    source,
                    solidColorBrush.Opacity,
                    0.42);
            VectorPoint registrationTopLeft =
                new(
                    horizontalDirection > 0.0
                        ? focus.X
                        : focus.X - 10.0,
                    verticalDirection > 0.0
                        ? focus.Y
                        : focus.Y - 10.0);
            commands.Add(
                new VectorRectangleCommand(
                    "focus-registration-square",
                    300,
                    50,
                    "per-frame",
                    registration,
                    registrationTopLeft,
                    10.0,
                    10.0,
                    Hairline));
            commands.Add(
                new VectorPointCommand(
                    "focus-core",
                    300,
                    60,
                    "per-frame",
                    accent,
                    focus,
                    2.0));
        }

        RetainedVectorScene scene =
            new(
                RetainedVectorSceneContract.ContractVersion,
                RetainedVectorSceneContract.ContractId,
                "win10-aperture-focus-v1",
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
        inputs =
            new Win10ApertureVectorSceneInputs(
                scene,
                CreatePalette("accent", source));
        return true;
    }

    private static void AddRegistrationSquare(
        ICollection<VectorCommand> commands,
        string id,
        VectorPoint center,
        int order,
        VectorMaterial material)
    {
        commands.Add(
            new VectorRectangleCommand(
                id,
                200,
                order,
                "static",
                material,
                new(center.X - 1.5, center.Y - 1.5),
                3.0,
                3.0,
                Hairline));
    }

    private static VectorMaterial CreateMaterial(
        string channel,
        Color source,
        double brushOpacity,
        double opacityScale) =>
        new(
            channel,
            1.0,
            (source.A / 255.0) *
                brushOpacity *
                opacityScale,
            "alpha");

    private static IReadOnlyDictionary<string, Color> CreatePalette(
        string channel,
        Color source) =>
        new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            [channel] =
                Color.FromRgb(
                    source.R,
                    source.G,
                    source.B),
        };

    private static void AddOrthogonalCorner(
        ICollection<VectorPathFigure> figures,
        VectorPoint start,
        VectorPoint corner,
        VectorPoint end)
    {
        List<VectorPathSegment> segments = [];
        VectorPoint current = start;
        AddLine(segments, ref current, corner);
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
