using System.Collections.ObjectModel;
using System.Windows;

namespace Jarvis.Win10.NeuralVoidPreview;

internal enum PartitionAxis { Vertical, Horizontal }
internal readonly record struct PartitionCutKey(PartitionAxis Axis, int GridCoordinate);
internal sealed record PartitionDividerSpan(PartitionCutKey Key, double Position, double Start, double End);

/// <summary>Immutable, staged projection of shared catalog edges; never reserves a gutter.</summary>
internal sealed class PartitionGeometry
{
    internal const double MinimumWidth = 320;
    internal const double MinimumHeight = 188;
    internal LayoutPreset Preset { get; }
    internal Rect WorkArea { get; }
    internal IReadOnlyList<Rect> Panes { get; }
    internal IReadOnlyDictionary<PartitionCutKey, double> Ratios { get; }
    internal IReadOnlyList<PartitionDividerSpan> Dividers { get; }

    private PartitionGeometry(LayoutPreset preset, Rect area, Rect[] panes,
        Dictionary<PartitionCutKey, double> ratios, PartitionDividerSpan[] dividers)
    {
        Preset = preset; WorkArea = area;
        Panes = Array.AsReadOnly(panes);
        Ratios = new ReadOnlyDictionary<PartitionCutKey, double>(ratios);
        Dividers = Array.AsReadOnly(dividers);
    }

    internal static bool TryCreate(LayoutPreset preset, Rect area,
        IReadOnlyDictionary<PartitionCutKey, double>? saved, out PartitionGeometry? geometry)
    {
        geometry = null;
        if (!Enum.IsDefined(preset) || !ValidArea(area)) return false;
        LayoutDefinition definition = LayoutCatalog.Get(preset);
        Dictionary<PartitionCutKey, double> ratios = [];
        Dictionary<(PartitionAxis Axis, int Coordinate), double> edges = [];
        foreach (PartitionAxis axis in Enum.GetValues<PartitionAxis>())
        {
            int[] coordinates = definition.Panes.SelectMany(p => axis == PartitionAxis.Vertical
                ? new[] { p.X, p.Right } : new[] { p.Y, p.Bottom }).Distinct().Order().ToArray();
            double start = axis == PartitionAxis.Vertical ? area.Left : area.Top;
            double length = axis == PartitionAxis.Vertical ? area.Width : area.Height;
            double previousRatio = -1, previousEdge = double.NegativeInfinity;
            foreach (int coordinate in coordinates)
            {
                PartitionCutKey key = new(axis, coordinate);
                double ratio = coordinate / (double)LayoutCatalog.GridSize;
                if (coordinate is > 0 and < LayoutCatalog.GridSize)
                {
                    if (saved is not null && !saved.TryGetValue(key, out ratio)) return false;
                    ratios.Add(key, ratio);
                }
                if (!double.IsFinite(ratio) || ratio < 0 || ratio > 1 || ratio <= previousRatio) return false;
                double projected = start + length * ratio;
                double edge = Math.Round(projected, MidpointRounding.AwayFromZero);
                if (!double.IsFinite(projected) || !double.IsFinite(edge) || edge <= previousEdge) return false;
                edges.Add((axis, coordinate), edge); previousRatio = ratio; previousEdge = edge;
            }
        }
        if (saved is not null && saved.Count != ratios.Count) return false;
        double X(int coordinate) => edges[(PartitionAxis.Vertical, coordinate)];
        double Y(int coordinate) => edges[(PartitionAxis.Horizontal, coordinate)];
        Rect[] panes = definition.Panes.Select(p => new Rect(X(p.X), Y(p.Y), X(p.Right)-X(p.X), Y(p.Bottom)-Y(p.Y))).ToArray();
        if (!IsExactCover(panes, area)) return false;
        List<PartitionDividerSpan> dividers = [];
        foreach (PartitionCutKey key in ratios.Keys)
        {
            List<(double Start, double End)> segments = [];
            foreach (LayoutPane first in definition.Panes)
                foreach (LayoutPane second in definition.Panes)
                {
                    bool vertical = key.Axis == PartitionAxis.Vertical;
                    if (vertical ? first.Right != key.GridCoordinate || second.X != key.GridCoordinate
                        : first.Bottom != key.GridCoordinate || second.Y != key.GridCoordinate) continue;
                    double start = vertical ? Y(Math.Max(first.Y,second.Y)) : X(Math.Max(first.X,second.X));
                    double end = vertical ? Y(Math.Min(first.Bottom,second.Bottom)) : X(Math.Min(first.Right,second.Right));
                    if (end > start) segments.Add((start,end));
                }
            double position = edges[(key.Axis,key.GridCoordinate)];
            foreach ((double start, double end) in segments.OrderBy(segment => segment.Start))
            {
                if (dividers.Count > 0 && dividers[^1].Key == key && start <= dividers[^1].End)
                    dividers[^1] = dividers[^1] with { End = Math.Max(end,dividers[^1].End) };
                else dividers.Add(new(key,position,start,end));
            }
        }
        if (dividers.Any(span => !double.IsFinite(span.Position) || !double.IsFinite(span.Start) ||
            !double.IsFinite(span.End) || span.End <= span.Start)) return false;
        geometry = new(preset,area,panes,ratios,dividers.ToArray());
        return true;
    }

    internal bool TryGetLimits(PartitionCutKey key, out double minimum, out double maximum)
    {
        minimum = double.NegativeInfinity; maximum = double.PositiveInfinity;
        if (!Ratios.ContainsKey(key)) return false;
        bool vertical = key.Axis == PartitionAxis.Vertical;
        LayoutDefinition definition = LayoutCatalog.Get(Preset);
        for (int index=0;index<definition.PaneCount;index++)
        {
            LayoutPane pane = definition.Panes[index]; Rect bounds = Panes[index];
            if ((vertical ? pane.Right : pane.Bottom) == key.GridCoordinate)
                minimum = Math.Max(minimum,(vertical ? bounds.Left+MinimumWidth : bounds.Top+MinimumHeight));
            if ((vertical ? pane.X : pane.Y) == key.GridCoordinate)
                maximum = Math.Min(maximum,(vertical ? bounds.Right-MinimumWidth : bounds.Bottom-MinimumHeight));
        }
        return double.IsFinite(minimum) && double.IsFinite(maximum) && minimum <= maximum;
    }

    internal bool TryResize(PartitionCutKey key, double position, out PartitionGeometry? candidate)
    {
        candidate = null;
        if (!double.IsFinite(position) || !TryGetLimits(key,out double minimum,out double maximum)) return false;
        double edge = Math.Clamp(Math.Round(position,MidpointRounding.AwayFromZero),minimum,maximum);
        double start = key.Axis == PartitionAxis.Vertical ? WorkArea.Left : WorkArea.Top;
        double length = key.Axis == PartitionAxis.Vertical ? WorkArea.Width : WorkArea.Height;
        double ratio = (edge-start)/length;
        if (!double.IsFinite(ratio)) return false;
        Dictionary<PartitionCutKey,double> staged = Ratios.ToDictionary(pair => pair.Key,pair => pair.Value);
        staged[key] = ratio;
        return TryCreate(Preset,WorkArea,staged,out candidate);
    }

    internal static bool IsExactCover(IReadOnlyList<Rect> panes, Rect area)
    {
        if (panes.Count == 0 || !ValidArea(area) || panes.Any(p => !ValidArea(p) ||
            p.Width < MinimumWidth || p.Height < MinimumHeight || !area.Contains(p))) return false;
        double[] xs = panes.SelectMany(p => new[] { p.Left,p.Right }).Append(area.Left).Append(area.Right).Distinct().Order().ToArray();
        double[] ys = panes.SelectMany(p => new[] { p.Top,p.Bottom }).Append(area.Top).Append(area.Bottom).Distinct().Order().ToArray();
        for (int x=0;x<xs.Length-1;x++)
            for (int y=0;y<ys.Length-1;y++)
                if (panes.Count(p => p.Left <= xs[x] && p.Right >= xs[x+1] && p.Top <= ys[y] && p.Bottom >= ys[y+1]) != 1) return false;
        for (int first=0;first<panes.Count;first++)
            for (int second=first+1;second<panes.Count;second++)
                if (Math.Max(panes[first].Left,panes[second].Left) < Math.Min(panes[first].Right,panes[second].Right) &&
                    Math.Max(panes[first].Top,panes[second].Top) < Math.Min(panes[first].Bottom,panes[second].Bottom)) return false;
        return true;
    }

    private static bool ValidArea(Rect area) => !area.IsEmpty && area.Width > 0 && area.Height > 0 &&
        new[] {area.Left,area.Top,area.Right,area.Bottom,area.Width,area.Height}.All(value =>
            double.IsFinite(value) && Math.Abs(value) < 4503599627370496 && value == Math.Round(value));
}
