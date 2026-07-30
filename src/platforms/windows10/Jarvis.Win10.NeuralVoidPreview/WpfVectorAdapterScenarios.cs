using System.Windows.Media;
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
                    receipt.CommandsDrawn == 5 &&
                    receipt.PrimitiveKindCount == 5;
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
