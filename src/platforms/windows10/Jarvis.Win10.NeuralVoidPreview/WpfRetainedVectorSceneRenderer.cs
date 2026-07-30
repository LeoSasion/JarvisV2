using System.Windows;
using System.Windows.Media;
using Jarvis.VisualEffects;

namespace Jarvis.Win10.NeuralVoidPreview;

internal sealed record WpfVectorSceneRenderReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    string SceneId,
    int CommandsDrawn,
    int PrimitiveKindCount,
    bool SceneCompiled,
    bool PaletteValidated,
    bool ReadyForShellMutation,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed,
    IReadOnlyList<string> Failures);

internal sealed class WpfRetainedVectorSceneRenderer
{
    private readonly IReadOnlyDictionary<string, Color> _palette;

    public WpfRetainedVectorSceneRenderer(
        IReadOnlyDictionary<string, Color> palette)
    {
        _palette =
            palette.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
    }

    public WpfVectorSceneRenderReceipt Render(
        DrawingContext destination,
        RetainedVectorScene scene)
    {
        VectorSceneCompilationReceipt compilation =
            RetainedVectorSceneCompiler.Compile(scene);
        if (compilation.Result !=
            "compiled-retained-vector-scene")
        {
            return Receipt(
                "blocked-empty-vector-scene",
                scene.SceneId,
                0,
                0,
                false,
                false,
                compilation.Failures);
        }

        string[] missingChannels =
            scene.Commands
                .Select(command => command.Material.ColorChannel)
                .Distinct(StringComparer.Ordinal)
                .Where(channel =>
                    !_palette.TryGetValue(
                        channel,
                        out Color color) ||
                    color.A != byte.MaxValue)
                .Order(StringComparer.Ordinal)
                .ToArray();
        if (missingChannels.Length != 0)
        {
            return Receipt(
                "blocked-empty-vector-scene",
                scene.SceneId,
                0,
                0,
                true,
                false,
                missingChannels.Select(channel =>
                    $"wpf-vector-palette-invalid:{channel}")
                    .ToArray());
        }

        try
        {
            DrawingGroup staged = new();
            using (DrawingContext context = staged.Open())
            {
                foreach (VectorCommand command in scene.Commands)
                {
                    DrawCommand(context, command);
                }
            }
            staged.Freeze();
            destination.DrawDrawing(staged);
            return Receipt(
                "rendered-retained-vector-scene",
                scene.SceneId,
                scene.Commands.Count,
                scene.Commands
                    .Select(command => command.Kind)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                true,
                true,
                []);
        }
        catch (InvalidOperationException)
        {
            return Receipt(
                "blocked-empty-vector-scene",
                scene.SceneId,
                0,
                0,
                true,
                true,
                ["wpf-vector-staging-failed"]);
        }
    }

    private void DrawCommand(
        DrawingContext context,
        VectorCommand command)
    {
        SolidColorBrush brush = CreateBrush(command.Material);
        switch (command)
        {
            case VectorPointCommand point:
                context.DrawEllipse(
                    brush,
                    null,
                    ToPoint(point.Center),
                    point.Radius,
                    point.Radius);
                break;
            case VectorLineCommand line:
                context.DrawLine(
                    CreatePen(brush, line.Stroke),
                    ToPoint(line.Start),
                    ToPoint(line.End));
                break;
            case VectorPolylineCommand polyline:
                context.DrawGeometry(
                    null,
                    CreatePen(brush, polyline.Stroke),
                    CreatePolyline(polyline.Points));
                break;
            case VectorArcCommand arc:
                context.DrawGeometry(
                    null,
                    CreatePen(brush, arc.Stroke),
                    CreateArc(arc));
                break;
            case VectorPathCommand path:
                context.DrawGeometry(
                    null,
                    CreatePen(brush, path.Stroke),
                    CreatePath(path.Figures));
                break;
            case VectorPlaneCommand plane:
                context.DrawGeometry(
                    brush,
                    null,
                    CreatePolygon(plane.Points));
                break;
            default:
                throw new InvalidOperationException(
                    "Unsupported retained vector command.");
        }
    }

    private SolidColorBrush CreateBrush(VectorMaterial material)
    {
        Color source = _palette[material.ColorChannel];
        SolidColorBrush brush =
            new(
                Color.FromArgb(
                    ToByte(material.Opacity),
                    ToByte((source.R / 255.0) * material.Luminance),
                    ToByte((source.G / 255.0) * material.Luminance),
                    ToByte((source.B / 255.0) * material.Luminance)));
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePen(
        Brush brush,
        VectorStroke stroke)
    {
        Pen pen = new(brush, stroke.Width)
        {
            StartLineCap = ToLineCap(stroke.LineCap),
            EndLineCap = ToLineCap(stroke.LineCap),
            LineJoin = ToLineJoin(stroke.LineJoin),
        };
        if (stroke.DashPattern.Count != 0)
        {
            pen.DashStyle =
                new DashStyle(stroke.DashPattern, 0.0);
        }
        pen.Freeze();
        return pen;
    }

    private static StreamGeometry CreatePolyline(
        IReadOnlyList<VectorPoint> points)
    {
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(ToPoint(points[0]), false, false);
            for (int index = 1; index < points.Count; index++)
            {
                context.LineTo(
                    ToPoint(points[index]),
                    true,
                    false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry CreatePolygon(
        IReadOnlyList<VectorPoint> points)
    {
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(ToPoint(points[0]), true, true);
            for (int index = 1; index < points.Count; index++)
            {
                context.LineTo(
                    ToPoint(points[index]),
                    true,
                    false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry CreateArc(VectorArcCommand arc)
    {
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(ToPoint(arc.Start), false, false);
            context.ArcTo(
                ToPoint(arc.End),
                new Size(arc.RadiusX, arc.RadiusY),
                arc.RotationDegrees,
                arc.LargeArc,
                arc.Sweep == "clockwise"
                    ? SweepDirection.Clockwise
                    : SweepDirection.Counterclockwise,
                true,
                false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry CreatePath(
        IReadOnlyList<VectorPathFigure> figures)
    {
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            foreach (VectorPathFigure figure in figures)
            {
                context.BeginFigure(
                    ToPoint(figure.Start),
                    false,
                    figure.Closed);
                foreach (
                    VectorPathSegment segment in
                    figure.Segments)
                {
                    switch (segment)
                    {
                        case VectorPathLineSegment line:
                            context.LineTo(
                                ToPoint(line.End),
                                true,
                                false);
                            break;
                        case VectorPathArcSegment arc:
                            context.ArcTo(
                                ToPoint(arc.End),
                                new Size(
                                    arc.RadiusX,
                                    arc.RadiusY),
                                arc.RotationDegrees,
                                arc.LargeArc,
                                arc.Sweep == "clockwise"
                                    ? SweepDirection.Clockwise
                                    : SweepDirection
                                        .Counterclockwise,
                                true,
                                false);
                            break;
                        default:
                            throw new InvalidOperationException(
                                "Unsupported retained path segment.");
                    }
                }
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private static Point ToPoint(VectorPoint point) =>
        new(point.X, point.Y);

    private static byte ToByte(double value) =>
        checked((byte)Math.Round(
            Math.Clamp(value, 0.0, 1.0) * 255.0,
            MidpointRounding.AwayFromZero));

    private static PenLineCap ToLineCap(string value) =>
        value switch
        {
            "butt" => PenLineCap.Flat,
            "round" => PenLineCap.Round,
            _ => PenLineCap.Square,
        };

    private static PenLineJoin ToLineJoin(string value) =>
        value switch
        {
            "miter" => PenLineJoin.Miter,
            "bevel" => PenLineJoin.Bevel,
            _ => PenLineJoin.Round,
        };

    private static WpfVectorSceneRenderReceipt Receipt(
        string result,
        string sceneId,
        int commandsDrawn,
        int primitiveKindCount,
        bool sceneCompiled,
        bool paletteValidated,
        IReadOnlyList<string> failures) =>
        new(
            1,
            "jarvisv2-win10-wpf-retained-vector-render",
            result,
            sceneId,
            commandsDrawn,
            primitiveKindCount,
            sceneCompiled,
            paletteValidated,
            false,
            false,
            "not-run",
            false,
            failures);
}
