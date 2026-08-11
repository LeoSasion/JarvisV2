using System.Collections.ObjectModel;
using System.Windows;

namespace Jarvis.Win10.NeuralVoidPreview;

public enum LayoutPreset
{
    Maximized = 100,

    EqualColumns = 200,
    WideLeftNarrowRight = 210,
    NarrowLeftWideRight = 220,
    EqualRows = 230,
    WideTopNarrowBottom = 240,
    NarrowTopWideBottom = 250,

    ThreeColumns = 300,
    CenterMainColumns = 310,
    LeftMainRightStack = 320,
    LeftStackRightMain = 330,
    ThreeRows = 340,
    CenterMainRows = 350,
    TopMainBottomSplit = 360,
    TopSplitBottomMain = 370,

    FourQuadrants = 400,
}

public enum LayoutFamily
{
    Single,
    Split,
    Stripe,
    Priority,
    Grid,
}

public readonly record struct LayoutPane(
    int X,
    int Y,
    int Width,
    int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}

public sealed class LayoutDefinition
{
    public LayoutDefinition(
        LayoutPreset preset,
        int railOrder,
        LayoutFamily family,
        string id,
        string automationName,
        IReadOnlyList<LayoutPane> panes)
    {
        Preset = preset;
        RailOrder = railOrder;
        Family = family;
        Id = id;
        AutomationName = automationName;
        Panes = new ReadOnlyCollection<LayoutPane>(panes.ToArray());
        Signature = LayoutCatalog.CanonicalSignature(Panes);
    }

    public LayoutPreset Preset { get; }

    public int RailOrder { get; }

    public LayoutFamily Family { get; }

    public string Id { get; }

    public string AutomationName { get; }

    public IReadOnlyList<LayoutPane> Panes { get; }

    public int PaneCount => Panes.Count;

    public string Signature { get; }
}

internal enum LayoutTopologyTransform
{
    MirrorHorizontal,
    MirrorVertical,
    RotateClockwise,
}

public static class LayoutCatalog
{
    public const int GridSize = 12;

    private static readonly ReadOnlyCollection<LayoutDefinition> Definitions =
        new(
        [
            new(
                LayoutPreset.Maximized,
                10,
                LayoutFamily.Single,
                "maximized",
                "Single maximized window",
                [new(0, 0, 12, 12)]),

            new(
                LayoutPreset.EqualColumns,
                20,
                LayoutFamily.Split,
                "equal-columns",
                "Equal left and right panes",
                [new(0, 0, 6, 12), new(6, 0, 6, 12)]),
            new(
                LayoutPreset.WideLeftNarrowRight,
                30,
                LayoutFamily.Split,
                "wide-left-narrow-right",
                "Wide left pane and narrow right pane",
                [new(0, 0, 8, 12), new(8, 0, 4, 12)]),
            new(
                LayoutPreset.NarrowLeftWideRight,
                40,
                LayoutFamily.Split,
                "narrow-left-wide-right",
                "Narrow left pane and wide right pane",
                [new(0, 0, 4, 12), new(4, 0, 8, 12)]),
            new(
                LayoutPreset.EqualRows,
                50,
                LayoutFamily.Split,
                "equal-rows",
                "Equal top and bottom panes",
                [new(0, 0, 12, 6), new(0, 6, 12, 6)]),
            new(
                LayoutPreset.WideTopNarrowBottom,
                60,
                LayoutFamily.Split,
                "wide-top-narrow-bottom",
                "Wide top pane and narrow bottom pane",
                [new(0, 0, 12, 8), new(0, 8, 12, 4)]),
            new(
                LayoutPreset.NarrowTopWideBottom,
                70,
                LayoutFamily.Split,
                "narrow-top-wide-bottom",
                "Narrow top pane and wide bottom pane",
                [new(0, 0, 12, 4), new(0, 4, 12, 8)]),

            new(
                LayoutPreset.ThreeColumns,
                80,
                LayoutFamily.Stripe,
                "three-columns",
                "Three equal columns",
                [new(0, 0, 4, 12), new(4, 0, 4, 12), new(8, 0, 4, 12)]),
            new(
                LayoutPreset.CenterMainColumns,
                90,
                LayoutFamily.Stripe,
                "center-main-columns",
                "Wide center column and two narrow side columns",
                [new(0, 0, 3, 12), new(3, 0, 6, 12), new(9, 0, 3, 12)]),
            new(
                LayoutPreset.LeftMainRightStack,
                100,
                LayoutFamily.Priority,
                "left-main-right-stack",
                "Large left pane and two stacked right panes",
                [new(0, 0, 8, 12), new(8, 0, 4, 6), new(8, 6, 4, 6)]),
            new(
                LayoutPreset.LeftStackRightMain,
                110,
                LayoutFamily.Priority,
                "left-stack-right-main",
                "Two stacked left panes and large right pane",
                [new(0, 0, 4, 6), new(0, 6, 4, 6), new(4, 0, 8, 12)]),
            new(
                LayoutPreset.ThreeRows,
                120,
                LayoutFamily.Stripe,
                "three-rows",
                "Three equal rows",
                [new(0, 0, 12, 4), new(0, 4, 12, 4), new(0, 8, 12, 4)]),
            new(
                LayoutPreset.CenterMainRows,
                130,
                LayoutFamily.Stripe,
                "center-main-rows",
                "Wide center row and two narrow outer rows",
                [new(0, 0, 12, 3), new(0, 3, 12, 6), new(0, 9, 12, 3)]),
            new(
                LayoutPreset.TopMainBottomSplit,
                140,
                LayoutFamily.Priority,
                "top-main-bottom-split",
                "Large top pane and two bottom panes",
                [new(0, 0, 12, 8), new(0, 8, 6, 4), new(6, 8, 6, 4)]),
            new(
                LayoutPreset.TopSplitBottomMain,
                150,
                LayoutFamily.Priority,
                "top-split-bottom-main",
                "Two top panes and large bottom pane",
                [new(0, 0, 6, 4), new(6, 0, 6, 4), new(0, 4, 12, 8)]),

            new(
                LayoutPreset.FourQuadrants,
                160,
                LayoutFamily.Grid,
                "four-quadrants",
                "Four equal panes",
                [new(0, 0, 6, 6), new(6, 0, 6, 6), new(0, 6, 6, 6), new(6, 6, 6, 6)]),
        ]);

    private static readonly ReadOnlyDictionary<LayoutPreset, LayoutDefinition>
        DefinitionsByPreset =
            new(
                Definitions.ToDictionary(
                    definition => definition.Preset));

    static LayoutCatalog()
    {
        ValidateCatalog();
    }

    public static IReadOnlyList<LayoutDefinition> All => Definitions;

    public static LayoutDefinition Get(LayoutPreset preset) =>
        DefinitionsByPreset.TryGetValue(preset, out LayoutDefinition? definition)
            ? definition
            : throw new ArgumentOutOfRangeException(
                nameof(preset),
                preset,
                "Unknown layout preset.");

    public static IReadOnlyList<Rect> Scale(
        LayoutPreset preset,
        Rect bounds)
    {
        LayoutDefinition definition = Get(preset);
        return definition.Panes.Select(
            pane => new Rect(
                bounds.X + bounds.Width * pane.X / GridSize,
                bounds.Y + bounds.Height * pane.Y / GridSize,
                bounds.Width * pane.Width / GridSize,
                bounds.Height * pane.Height / GridSize)).ToArray();
    }

    public static bool IsExactCover(LayoutDefinition definition)
    {
        if (definition.Panes.Count == 0)
        {
            return false;
        }

        bool[,] occupied = new bool[GridSize, GridSize];
        foreach (LayoutPane pane in definition.Panes)
        {
            if (pane.X < 0 || pane.Y < 0 ||
                pane.Width <= 0 || pane.Height <= 0 ||
                pane.Right > GridSize || pane.Bottom > GridSize)
            {
                return false;
            }

            for (int x = pane.X; x < pane.Right; x++)
            {
                for (int y = pane.Y; y < pane.Bottom; y++)
                {
                    if (occupied[x, y])
                    {
                        return false;
                    }

                    occupied[x, y] = true;
                }
            }
        }

        for (int x = 0; x < GridSize; x++)
        {
            for (int y = 0; y < GridSize; y++)
            {
                if (!occupied[x, y])
                {
                    return false;
                }
            }
        }

        return true;
    }

    internal static string CanonicalSignature(
        IEnumerable<LayoutPane> panes) =>
        string.Join(
            ";",
            panes
                .OrderBy(pane => pane.Y)
                .ThenBy(pane => pane.X)
                .ThenBy(pane => pane.Height)
                .ThenBy(pane => pane.Width)
                .Select(
                    pane =>
                        $"{pane.X},{pane.Y},{pane.Width},{pane.Height}"));

    internal static bool HasOrthogonalClosure()
    {
        HashSet<string> signatures =
            Definitions
                .Select(definition => definition.Signature)
                .ToHashSet(StringComparer.Ordinal);
        foreach (LayoutDefinition definition in Definitions)
        {
            foreach (LayoutTopologyTransform transform in
                     Enum.GetValues<LayoutTopologyTransform>())
            {
                string transformed = CanonicalSignature(
                    definition.Panes.Select(
                        pane => Transform(pane, transform)));
                if (!signatures.Contains(transformed))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static LayoutPane Transform(
        LayoutPane pane,
        LayoutTopologyTransform transform) =>
        transform switch
        {
            LayoutTopologyTransform.MirrorHorizontal =>
                new(
                    GridSize - pane.Right,
                    pane.Y,
                    pane.Width,
                    pane.Height),
            LayoutTopologyTransform.MirrorVertical =>
                new(
                    pane.X,
                    GridSize - pane.Bottom,
                    pane.Width,
                    pane.Height),
            LayoutTopologyTransform.RotateClockwise =>
                new(
                    GridSize - pane.Bottom,
                    pane.X,
                    pane.Height,
                    pane.Width),
            _ => throw new ArgumentOutOfRangeException(
                nameof(transform),
                transform,
                "Unknown topology transform."),
        };

    private static void ValidateCatalog()
    {
        if (Definitions.Count != Enum.GetValues<LayoutPreset>().Length ||
            Definitions.Select(definition => definition.Preset).Distinct().Count() !=
                Definitions.Count ||
            Definitions.Select(definition => definition.RailOrder).Distinct().Count() !=
                Definitions.Count ||
            Definitions.Select(definition => definition.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                Definitions.Count ||
            Definitions.Select(definition => definition.Signature)
                .Distinct(StringComparer.Ordinal).Count() !=
                Definitions.Count ||
            !Definitions.SequenceEqual(
                Definitions.OrderBy(definition => definition.RailOrder)) ||
            !Definitions.SequenceEqual(
                Definitions.OrderBy(definition => definition.PaneCount)
                    .ThenBy(definition => definition.RailOrder)) ||
            Definitions.Any(
                definition =>
                    definition.PaneCount is < 1 or > 4 ||
                    !IsExactCover(definition)) ||
            !HasOrthogonalClosure())
        {
            throw new InvalidOperationException(
                "The window layout catalog is incomplete or internally inconsistent.");
        }
    }
}
