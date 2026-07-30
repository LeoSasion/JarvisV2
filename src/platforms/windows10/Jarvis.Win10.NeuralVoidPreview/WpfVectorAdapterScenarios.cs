using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Jarvis.VisualEffects;

namespace Jarvis.Win10.NeuralVoidPreview;

internal sealed record WpfVectorAdapterScenario(
    string Name,
    bool Passed,
    string Detail);

internal sealed record WpfVectorAdapterTestReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    int ScenarioCount,
    int PassedCount,
    bool ShellMutationSupported,
    bool DeviceIntegrationSupported,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed,
    IReadOnlyList<WpfVectorAdapterScenario> Scenarios);

internal static class WpfVectorAdapterScenarios
{
    public static WpfVectorAdapterTestReceipt Run()
    {
        List<WpfVectorAdapterScenario> scenarios = [];
        IReadOnlyDictionary<string, Color> palette =
            Win10NeuralVectorSceneFactory.CreatePalette();
        WpfRetainedVectorSceneRenderer renderer =
            new(palette);

        Add(
            scenarios,
            "win10-static-scene-renders",
            () =>
            {
                WpfVectorSceneRenderReceipt receipt =
                    Render(
                        renderer,
                        Win10NeuralVectorSceneFactory
                            .CreateStaticScene());
                return
                    receipt.Result ==
                        "rendered-retained-vector-scene" &&
                    receipt.CommandsDrawn == 14 &&
                    receipt.PrimitiveKindCount == 3 &&
                    receipt.SceneCompiled &&
                    receipt.PaletteValidated;
            });
        Add(
            scenarios,
            "all-common-primitives-render",
            () =>
            {
                WpfVectorSceneRenderReceipt receipt =
                    Render(
                        renderer,
                        RetainedVectorSceneFactory
                            .CreateContractProbe());
                return
                    receipt.Result ==
                        "rendered-retained-vector-scene" &&
                    receipt.CommandsDrawn == 8 &&
                    receipt.PrimitiveKindCount == 8;
            });
        Add(
            scenarios,
            "aperture-contours-render-at-reviewed-sizes",
            () =>
            {
                (double Width,
                    double Height,
                    double Radius,
                    double Length,
                    Color Color)[] cases =
                    [
                        (
                            595.0,
                            488.0,
                            24.0,
                            38.0,
                            Color.FromRgb(
                                0x1B,
                                0x25,
                                0x24)),
                        (
                            28.0,
                            28.0,
                            4.0,
                            6.0,
                            Color.FromArgb(
                                0x98,
                                0x00,
                                0xFF,
                                0x9A)),
                        (
                            1000.0,
                            620.0,
                            22.0,
                            42.0,
                            Color.FromRgb(
                                0x34,
                                0x40,
                                0x3E)),
                        (
                            200.0,
                            112.0,
                            12.0,
                            18.0,
                            Color.FromRgb(
                                0x34,
                                0x40,
                                0x3E)),
                    ];
                return cases.All(testCase =>
                {
                    bool created =
                        Win10ApertureVectorSceneFactory.TryCreate(
                            testCase.Width,
                            testCase.Height,
                            testCase.Radius,
                            testCase.Length,
                            new SolidColorBrush(testCase.Color),
                            out
                                Win10ApertureVectorSceneInputs?
                                inputs);
                    if (!created || inputs is null)
                    {
                        return false;
                    }

                    VectorSceneCompilationReceipt compilation =
                        RetainedVectorSceneCompiler.Compile(
                            inputs.Scene);
                    WpfVectorSceneRenderReceipt receipt =
                        Render(
                            new WpfRetainedVectorSceneRenderer(
                                inputs.Palette),
                            inputs.Scene);
                    return
                        compilation.Result ==
                            "compiled-retained-vector-scene" &&
                        compilation.CommandCount == 4 &&
                        compilation.ArcCount == 4 &&
                        compilation.PathCount == 1 &&
                        compilation.RectangleCount == 3 &&
                        receipt.Result ==
                            "rendered-retained-vector-scene" &&
                        receipt.CommandsDrawn == 4 &&
                        receipt.PrimitiveKindCount == 2;
                });
            });
        Add(
            scenarios,
            "zero-radius-aperture-path-compiles",
            () =>
            {
                bool created =
                    Win10ApertureVectorSceneFactory.TryCreate(
                        200.0,
                        112.0,
                        0.0,
                        0.0,
                        new SolidColorBrush(
                            Color.FromRgb(
                                0x34,
                                0x40,
                                0x3E)),
                        out
                            Win10ApertureVectorSceneInputs?
                            inputs);
                if (!created || inputs is null)
                {
                    return false;
                }

                VectorSceneCompilationReceipt receipt =
                    RetainedVectorSceneCompiler.Compile(
                        inputs.Scene);
                return
                    receipt.Result ==
                        "compiled-retained-vector-scene" &&
                    receipt.CommandCount == 4 &&
                    receipt.PathCount == 1 &&
                    receipt.RectangleCount == 3 &&
                    receipt.ArcCount == 0;
            });
        Add(
            scenarios,
            "aperture-adapter-emits-visible-pixels",
            () =>
            {
                bool created =
                    Win10ApertureVectorSceneFactory.TryCreate(
                        200.0,
                        112.0,
                        12.0,
                        18.0,
                        new SolidColorBrush(
                            Color.FromRgb(
                                0x34,
                                0x40,
                                0x3E)),
                        out
                            Win10ApertureVectorSceneInputs?
                            inputs);
                if (!created || inputs is null)
                {
                    return false;
                }

                (WpfVectorSceneRenderReceipt receipt,
                    int visibleBytes) =
                    RenderWithVisibleBytes(
                        new WpfRetainedVectorSceneRenderer(
                            inputs.Palette),
                        inputs.Scene,
                        200,
                        112);
                if (receipt.Result !=
                        "rendered-retained-vector-scene" ||
                    visibleBytes == 0)
                {
                    throw new InvalidOperationException(
                        $"aperture-visible-failed:" +
                        $"{receipt.Result}:{visibleBytes}");
                }
                return true;
            });
        Add(
            scenarios,
            "minimal-aperture-registration-scene-is-safe",
            () =>
            {
                bool created =
                    Win10ApertureVectorSceneFactory.TryCreate(
                        4.0,
                        4.0,
                        0.0,
                        0.0,
                        new SolidColorBrush(
                            Color.FromRgb(
                                0x34,
                                0x40,
                                0x3E)),
                        out
                            Win10ApertureVectorSceneInputs?
                            inputs);
                if (!created || inputs is null)
                {
                    return false;
                }

                VectorSceneCompilationReceipt receipt =
                    RetainedVectorSceneCompiler.Compile(
                        inputs.Scene);
                return
                    receipt.Result ==
                        "compiled-retained-vector-scene" &&
                    receipt.CommandCount == 3 &&
                    receipt.PathCount == 0 &&
                    receipt.RectangleCount == 3 &&
                    receipt.ArcCount == 0;
            });
        Add(
            scenarios,
            "unsupported-aperture-brush-fails-closed",
            () =>
            {
                bool contourCreated =
                    Win10ApertureVectorSceneFactory.TryCreate(
                        200.0,
                        112.0,
                        12.0,
                        18.0,
                        new LinearGradientBrush(),
                        out
                            Win10ApertureVectorSceneInputs?
                            inputs);
                bool focusCreated =
                    Win10ApertureVectorSceneFactory.TryCreateFocus(
                        200.0,
                        112.0,
                        ApertureFocusCorner.TopLeft,
                        new LinearGradientBrush(),
                        out
                            Win10ApertureVectorSceneInputs?
                            focusInputs);
                return
                    !contourCreated &&
                    inputs is null &&
                    !focusCreated &&
                    focusInputs is null;
            });
        Add(
            scenarios,
            "aperture-focus-corners-render-independently",
            () =>
            {
                HashSet<string> hashes =
                    new(StringComparer.Ordinal);
                List<string> renderDiagnostics = [];
                ApertureFocusCorner[] corners =
                [
                    ApertureFocusCorner.None,
                    ApertureFocusCorner.TopLeft,
                    ApertureFocusCorner.TopRight,
                    ApertureFocusCorner.BottomLeft,
                    ApertureFocusCorner.BottomRight,
                ];
                foreach (ApertureFocusCorner corner in corners)
                {
                    bool created =
                        Win10ApertureVectorSceneFactory
                            .TryCreateFocus(
                                200.0,
                                112.0,
                                corner,
                                new SolidColorBrush(
                                    Color.FromRgb(
                                        0x00,
                                        0xFF,
                                        0x9A)),
                                out
                                    Win10ApertureVectorSceneInputs?
                                    inputs);
                    if (!created || inputs is null)
                    {
                        throw new InvalidOperationException(
                            $"focus-create-failed:{corner}");
                    }

                    VectorSceneCompilationReceipt compilation =
                        RetainedVectorSceneCompiler.Compile(
                            inputs.Scene);
                    int expectedCommands =
                        corner == ApertureFocusCorner.None
                            ? 0
                            : 6;
                    if (compilation.Result !=
                            "compiled-retained-vector-scene" ||
                        compilation.CommandCount !=
                            expectedCommands ||
                        compilation.PerFrameCommandCount !=
                            expectedCommands ||
                        compilation.SharedSignalCommandCount !=
                            expectedCommands ||
                        compilation.LineCount !=
                            (corner ==
                                ApertureFocusCorner.None
                                ? 0
                                : 4) ||
                        compilation.PathCount !=
                            0 ||
                        compilation.EllipseCount !=
                            (corner ==
                                ApertureFocusCorner.None
                                ? 0
                                : 1) ||
                        compilation.ArcCount !=
                            (corner ==
                                ApertureFocusCorner.None
                                ? 0
                                : 2) ||
                        compilation.PointCount !=
                            (corner ==
                                ApertureFocusCorner.None
                                ? 0
                                : 1))
                    {
                        throw new InvalidOperationException(
                            $"focus-compile-failed:{corner}:" +
                            $"{compilation.Result}:" +
                            $"commands={compilation.CommandCount}:" +
                            $"lines={compilation.LineCount}:" +
                            $"paths={compilation.PathCount}:" +
                            $"ellipses={compilation.EllipseCount}:" +
                            $"arcs={compilation.ArcCount}:" +
                            $"points={compilation.PointCount}");
                    }

                    ApertureFrame frame =
                        new()
                        {
                            Width = 200.0,
                            Height = 112.0,
                            CornerRadius = 12.0,
                            CornerLength = 18.0,
                            FocusCorner = corner,
                            LineBrush =
                                new SolidColorBrush(
                                    Color.FromRgb(
                                        0x34,
                                        0x40,
                                        0x3E)),
                            AccentBrush =
                                new SolidColorBrush(
                                    Color.FromRgb(
                                        0x00,
                                        0xFF,
                                        0x9A)),
                        };
                    frame.Measure(new Size(200.0, 112.0));
                    frame.Arrange(
                        new Rect(0.0, 0.0, 200.0, 112.0));
                    frame.UpdateLayout();

                    RenderTargetBitmap bitmap =
                        new(
                            200,
                            112,
                            96.0,
                            96.0,
                            PixelFormats.Pbgra32);
                    bitmap.Render(frame);
                    byte[] pixels =
                        new byte[200 * 112 * 4];
                    bitmap.CopyPixels(
                        pixels,
                        200 * 4,
                        0);
                    string hash =
                        Convert.ToHexString(
                            SHA256.HashData(pixels));
                    hashes.Add(hash);
                    int visibleBytes =
                        pixels.Count(value => value != 0);
                    renderDiagnostics.Add(
                        $"{corner}:{hash[..8]}:" +
                        $"visible={visibleBytes}");
                }

                if (hashes.Count != corners.Length)
                {
                    throw new InvalidOperationException(
                        $"focus-render-hash-count:{hashes.Count}:" +
                        string.Join(
                            ",",
                            renderDiagnostics));
                }
                return true;
            });
        Add(
            scenarios,
            "missing-semantic-color-fails-closed",
            () =>
            {
                Dictionary<string, Color> incomplete =
                    new(palette, StringComparer.Ordinal);
                incomplete.Remove("neutral-plane");
                WpfVectorSceneRenderReceipt receipt =
                    Render(
                        new WpfRetainedVectorSceneRenderer(incomplete),
                        Win10NeuralVectorSceneFactory
                            .CreateStaticScene());
                return
                    receipt.Result ==
                        "blocked-empty-vector-scene" &&
                    receipt.CommandsDrawn == 0 &&
                    receipt.Failures.Contains(
                        "wpf-vector-palette-invalid:neutral-plane",
                        StringComparer.Ordinal);
            });
        Add(
            scenarios,
            "non-opaque-palette-entry-fails-closed",
            () =>
            {
                Dictionary<string, Color> invalid =
                    new(palette, StringComparer.Ordinal)
                    {
                        ["neutral-ghost"] =
                            Color.FromArgb(
                                0x80,
                                0x2D,
                                0x3A,
                                0x38),
                    };
                WpfVectorSceneRenderReceipt receipt =
                    Render(
                        new WpfRetainedVectorSceneRenderer(invalid),
                        Win10NeuralVectorSceneFactory
                            .CreateStaticScene());
                return
                    receipt.Result ==
                        "blocked-empty-vector-scene" &&
                    receipt.CommandsDrawn == 0;
            });
        Add(
            scenarios,
            "invalid-common-scene-fails-closed",
            () =>
            {
                RetainedVectorScene invalid =
                    Win10NeuralVectorSceneFactory
                        .CreateStaticScene() with
                    {
                        BitmapResourcesRequested = true,
                    };
                WpfVectorSceneRenderReceipt receipt =
                    Render(renderer, invalid);
                return
                    receipt.Result ==
                        "blocked-empty-vector-scene" &&
                    receipt.CommandsDrawn == 0 &&
                    !receipt.SceneCompiled &&
                    !receipt.ReadyForShellMutation &&
                    !receipt.ActivationPermitted &&
                    receipt.LiveExplorer == "not-run" &&
                    !receipt.MutationPerformed;
            });
        Add(
            scenarios,
            "palette-is-snapshotted",
            () =>
            {
                Dictionary<string, Color> mutable =
                    new(palette, StringComparer.Ordinal);
                WpfRetainedVectorSceneRenderer snapshotRenderer =
                    new(mutable);
                mutable["neutral-plane"] =
                    Color.FromArgb(
                        0x00,
                        0x2D,
                        0x3A,
                        0x38);
                WpfVectorSceneRenderReceipt receipt =
                    Render(
                        snapshotRenderer,
                        Win10NeuralVectorSceneFactory
                            .CreateStaticScene());
                return
                    receipt.Result ==
                        "rendered-retained-vector-scene" &&
                    receipt.CommandsDrawn == 14 &&
                    receipt.PaletteValidated;
            });
        Add(
            scenarios,
            "empty-safe-scene-is-renderable",
            () =>
            {
                WpfVectorSceneRenderReceipt receipt =
                    Render(
                        renderer,
                        RetainedVectorSceneFactory
                            .CreateEmptySafeScene(1600.0, 900.0));
                return
                    receipt.Result ==
                        "rendered-retained-vector-scene" &&
                    receipt.CommandsDrawn == 0 &&
                    receipt.SceneCompiled &&
                    receipt.PaletteValidated;
            });

        int passedCount =
            scenarios.Count(scenario => scenario.Passed);
        return new WpfVectorAdapterTestReceipt(
            1,
            "jarvisv2-win10-wpf-vector-adapter-test",
            passedCount == scenarios.Count
                ? "passed"
                : "failed",
            scenarios.Count,
            passedCount,
            false,
            false,
            false,
            "not-run",
            false,
            scenarios);
    }

    private static WpfVectorSceneRenderReceipt Render(
        WpfRetainedVectorSceneRenderer renderer,
        RetainedVectorScene scene)
    {
        DrawingVisual visual = new();
        using DrawingContext context = visual.RenderOpen();
        return renderer.Render(context, scene);
    }

    private static (
        WpfVectorSceneRenderReceipt Receipt,
        int VisibleBytes)
        RenderWithVisibleBytes(
            WpfRetainedVectorSceneRenderer renderer,
            RetainedVectorScene scene,
            int width,
            int height)
    {
        DrawingVisual visual = new();
        WpfVectorSceneRenderReceipt receipt;
        using (DrawingContext context = visual.RenderOpen())
        {
            receipt = renderer.Render(context, scene);
        }

        RenderTargetBitmap bitmap =
            new(
                width,
                height,
                96.0,
                96.0,
                PixelFormats.Pbgra32);
        bitmap.Render(visual);
        byte[] pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return (
            receipt,
            pixels.Count(value => value != 0));
    }

    private static void Add(
        ICollection<WpfVectorAdapterScenario> scenarios,
        string name,
        Func<bool> action)
    {
        try
        {
            bool passed = action();
            scenarios.Add(
                new(
                    name,
                    passed,
                    passed
                        ? "passed"
                        : "assertion returned false"));
        }
        catch (Exception exception)
        {
            scenarios.Add(
                new(
                    name,
                    false,
                    $"{exception.GetType().Name}: " +
                    exception.Message));
        }
    }
}
