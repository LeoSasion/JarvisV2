using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Threading;
using Jarvis.VisualEffects;

namespace Jarvis.ControlCenter;

internal sealed record HandoffStageBounds(
    double Left,
    double Top,
    double Width,
    double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;

    public double CenterX => Left + (Width / 2.0);
}

internal sealed record HandoffConstellationLayout(
    double Width,
    double Height,
    IReadOnlyList<HandoffStageBounds> Stages);

public sealed record HandoffConstellationProbeReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    string CompositionId,
    int StaticCommandCount,
    int MaximumPerFrameCommandCount,
    int StageCount,
    int SignalFixedStepHz,
    int RenderSampleHz,
    bool RetainedScenesCompiled,
    bool SharedRgbBound,
    bool ParticlesEnabled,
    bool PostProcessingEnabled,
    bool ReadyForShellMutation,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed,
    IReadOnlyList<string> Failures);

public static class HandoffConstellationProbe
{
    public static HandoffConstellationProbeReceipt Run()
    {
        HandoffConstellationLayout layout = new(
            1440.0,
            900.0,
            [
                new(240.0, 138.0, 202.0, 68.0),
                new(460.0, 138.0, 202.0, 68.0),
                new(680.0, 138.0, 202.0, 68.0),
                new(900.0, 138.0, 202.0, 68.0),
            ]);
        List<string> failures = [];

        RetainedVectorScene staticScene =
            HandoffConstellationSceneFactory.CreateStatic(layout);
        VectorSceneCompilationReceipt staticReceipt =
            RetainedVectorSceneCompiler.Compile(staticScene);
        bool allCompiled =
            staticReceipt.Result == "compiled-retained-vector-scene";
        int maximumPerFrame = 0;
        bool sharedRgbBound = staticScene.Commands
            .Where(command =>
                command.Material.ColorChannel is "accent" or "pulse")
            .All(command =>
                RetainedVectorSceneContract.SharedSignalChannels.Contains(
                    command.Material.ColorChannel));

        for (int stage = 0; stage < layout.Stages.Count; stage++)
        {
            for (
                int frame = 0;
                frame < HandoffConstellationSceneFactory.RenderSampleHz;
                frame++)
            {
                RetainedVectorScene dynamicScene =
                    HandoffConstellationSceneFactory.CreateFocus(
                        layout,
                        stage,
                        frame);
                VectorSceneCompilationReceipt dynamicReceipt =
                    RetainedVectorSceneCompiler.Compile(dynamicScene);
                allCompiled &=
                    dynamicReceipt.Result ==
                        "compiled-retained-vector-scene";
                maximumPerFrame = Math.Max(
                    maximumPerFrame,
                    dynamicReceipt.PerFrameCommandCount);
                sharedRgbBound &= dynamicScene.Commands.All(command =>
                    RetainedVectorSceneContract.SharedSignalChannels.Contains(
                        command.Material.ColorChannel));
            }
        }

        if (!allCompiled)
        {
            failures.Add("retained-handoff-scenes-did-not-compile");
        }
        if (!sharedRgbBound)
        {
            failures.Add("handoff-scenes-left-shared-rgb-boundary");
        }
        if (
            staticScene.Commands.Count >
                HandoffConstellationSceneFactory.MaxStaticCommands ||
            maximumPerFrame >
                HandoffConstellationSceneFactory.MaxPerFrameCommands)
        {
            failures.Add("handoff-scene-budget-exceeded");
        }

        return new HandoffConstellationProbeReceipt(
            1,
            "jarvisv2-control-center-handoff-vfx-probe",
            failures.Count == 0 ? "passed" : "failed",
            HandoffConstellationSceneFactory.CompositionId,
            staticScene.Commands.Count,
            maximumPerFrame,
            layout.Stages.Count,
            HandoffConstellationSceneFactory.SignalFixedStepHz,
            HandoffConstellationSceneFactory.RenderSampleHz,
            allCompiled,
            sharedRgbBound,
            false,
            false,
            false,
            false,
            "not-run",
            false,
            failures);
    }
}

internal static class HandoffConstellationSceneFactory
{
    public const string CompositionId =
        "handoff-constellation-with-active-corner-focus-v1";
    public const int SignalFixedStepHz = 60;
    public const int RenderSampleHz = 30;
    public const int MaxStaticCommands = 96;
    public const int MaxPerFrameCommands = 24;

    private static readonly VectorSceneBudget LowPowerBudget =
        RetainedVectorSceneContract.GetRequiredBudget("low-power");
    private static readonly VectorStroke Hairline =
        new(1.0, "round", "round", []);
    private static readonly VectorMaterial NeutralGhost =
        new("neutral-ghost", 0.78, 0.34, "alpha");
    private static readonly VectorMaterial AccentGhost =
        new("accent", 0.72, 0.34, "alpha");
    private static readonly VectorMaterial AccentNode =
        new("accent", 0.92, 0.68, "alpha");
    private static readonly VectorMaterial Pulse =
        new("pulse", 1.0, 1.0, "alpha");
    private static readonly VectorMaterial PulseGhost =
        new("pulse", 0.82, 0.34, "alpha");

    public static RetainedVectorScene CreateStatic(
        HandoffConstellationLayout layout)
    {
        List<VectorCommand> commands = [];
        int order = 0;

        void AddLine(
            string id,
            VectorPoint start,
            VectorPoint end,
            VectorMaterial material)
        {
            commands.Add(new VectorLineCommand(
                id,
                100,
                order++,
                "static",
                material,
                start,
                end,
                Hairline));
        }

        void AddPoint(
            string id,
            VectorPoint center,
            double radius,
            VectorMaterial material)
        {
            commands.Add(new VectorPointCommand(
                id,
                100,
                order++,
                "static",
                material,
                center,
                radius));
        }

        double headerLeft = Math.Max(320.0, layout.Width * 0.34);
        double headerRight = Math.Min(
            layout.Width - 330.0,
            layout.Width * 0.78);
        double headerSpan = Math.Max(180.0, headerRight - headerLeft);
        VectorPoint[] headerNodes =
        [
            new(headerLeft, 28.0),
            new(headerLeft + (headerSpan * 0.31), 20.0),
            new(headerLeft + (headerSpan * 0.64), 38.0),
            new(headerRight, 25.0),
        ];
        for (int index = 0; index < headerNodes.Length - 1; index++)
        {
            AddLine(
                $"header-link-{index + 1}",
                headerNodes[index],
                headerNodes[index + 1],
                NeutralGhost);
        }
        for (int index = 0; index < headerNodes.Length; index++)
        {
            AddPoint(
                $"header-node-{index + 1}",
                headerNodes[index],
                index == 1 ? 1.8 : 1.35,
                AccentGhost);
        }

        double leftStartY = Math.Clamp(
            layout.Height * 0.36,
            292.0,
            Math.Max(292.0, layout.Height - 360.0));
        VectorPoint[] leftNodes =
        [
            new(164.0, leftStartY),
            new(194.0, leftStartY + 50.0),
            new(146.0, leftStartY + 116.0),
            new(194.0, leftStartY + 184.0),
            new(
                160.0,
                Math.Min(layout.Height - 196.0, leftStartY + 248.0)),
        ];
        for (int index = 0; index < leftNodes.Length - 1; index++)
        {
            AddLine(
                $"left-link-{index + 1}",
                leftNodes[index],
                leftNodes[index + 1],
                NeutralGhost);
        }
        for (int index = 0; index < leftNodes.Length; index++)
        {
            AddPoint(
                $"left-node-{index + 1}",
                leftNodes[index],
                1.45,
                index is 0 or 3 ? AccentGhost : NeutralGhost);
        }

        double railY = layout.Stages[0].Bottom + 12.0;
        for (int index = 0; index < layout.Stages.Count - 1; index++)
        {
            AddLine(
                $"handoff-link-{index + 1}",
                new(layout.Stages[index].CenterX, railY),
                new(layout.Stages[index + 1].CenterX, railY),
                AccentGhost);
        }
        for (int index = 0; index < layout.Stages.Count; index++)
        {
            AddPoint(
                $"handoff-node-{index + 1}",
                new(layout.Stages[index].CenterX, railY),
                1.7,
                AccentNode);
        }

        return Scene(
            "control-center-handoff-constellation-static-v1",
            1,
            layout,
            commands);
    }

    public static RetainedVectorScene CreateFocus(
        HandoffConstellationLayout layout,
        int stageIndex,
        int frameIndex)
    {
        if (stageIndex < 0 || stageIndex >= layout.Stages.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(stageIndex));
        }
        if (frameIndex < 0 || frameIndex >= RenderSampleHz)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        }

        HandoffStageBounds stage = layout.Stages[stageIndex];
        double cornerX = Math.Clamp(stage.Right - 2.0, 0.0, layout.Width);
        double cornerY = Math.Clamp(stage.Bottom - 2.0, 0.0, layout.Height);
        double railY = Math.Clamp(
            layout.Stages[0].Bottom + 12.0,
            0.0,
            layout.Height);
        long revision =
            1 + (stageIndex * RenderSampleHz) + frameIndex;
        List<VectorCommand> commands =
        [
            new VectorLineCommand(
                "active-corner-horizontal",
                200,
                0,
                "per-frame",
                Pulse,
                new(cornerX - 18.0, cornerY),
                new(cornerX, cornerY),
                Hairline),
            new VectorLineCommand(
                "active-corner-vertical",
                200,
                1,
                "per-frame",
                Pulse,
                new(cornerX, cornerY - 18.0),
                new(cornerX, cornerY),
                Hairline),
            new VectorPointCommand(
                "active-corner-field",
                200,
                2,
                "per-frame",
                PulseGhost,
                new(cornerX, cornerY),
                5.0),
            new VectorEllipseCommand(
                "active-corner-ring",
                200,
                3,
                "per-frame",
                PulseGhost,
                new(cornerX, cornerY),
                7.0,
                7.0,
                0.56,
                Hairline),
            new VectorPointCommand(
                "active-corner-point",
                200,
                4,
                "per-frame",
                Pulse,
                new(cornerX, cornerY),
                1.8),
            new VectorLineCommand(
                "active-rail-segment",
                200,
                5,
                "per-frame",
                PulseGhost,
                new(stage.CenterX - 11.0, railY),
                new(stage.CenterX + 11.0, railY),
                Hairline),
            new VectorEllipseCommand(
                "active-rail-ring",
                200,
                6,
                "per-frame",
                PulseGhost,
                new(stage.CenterX, railY),
                5.0,
                5.0,
                0.48,
                Hairline),
            new VectorPointCommand(
                "active-rail-point",
                200,
                7,
                "per-frame",
                Pulse,
                new(stage.CenterX, railY),
                2.1),
        ];
        return Scene(
            $"control-center-handoff-focus-stage-{stageIndex + 1}-frame-{frameIndex + 1}",
            revision,
            layout,
            commands);
    }

    public static RgbFrame SampleAccent(int frameIndex) =>
        RgbEffectEngine.Sample(
            175.18,
            0.61712,
            0.87059,
            "signal-pulse",
            frameIndex / (double)RenderSampleHz);

    private static RetainedVectorScene Scene(
        string sceneId,
        long revision,
        HandoffConstellationLayout layout,
        IReadOnlyList<VectorCommand> commands) =>
        new(
            RetainedVectorSceneContract.ContractVersion,
            RetainedVectorSceneContract.ContractId,
            sceneId,
            revision,
            layout.Width,
            layout.Height,
            "low-power",
            LowPowerBudget,
            RetainedVectorSceneContract.VisualSignalBinding,
            commands,
            false,
            false);
}

public sealed class HandoffConstellationLayer : FrameworkElement
{
    private const int StaticPreviewFrame = 7;

    private static readonly Color NeutralGhostColor =
        Color.FromRgb(0x60, 0x74, 0x7C);
    private static readonly Color StaticAccentColor =
        Color.FromRgb(0x55, 0xDE, 0xD3);

    private readonly DrawingVisual staticVisual = new();
    private readonly DrawingVisual dynamicVisual = new();
    private readonly DispatcherTimer renderTimer = new(
        DispatcherPriority.Render)
    {
        Interval = TimeSpan.FromSeconds(
            1.0 / HandoffConstellationSceneFactory.RenderSampleHz),
    };
    private readonly List<DrawingGroup> dynamicFrames = [];
    private Window? owner;
    private FrameworkElement[] stages = [];
    private ConversationRuntimePhase phase =
        ConversationRuntimePhase.NotStarted;
    private bool ownerReviewPending;
    private bool handoffComplete;
    private bool activeTurn;
    private bool refreshQueued;
    private bool systemParametersSubscribed;
    private int activeStage;
    private int frameIndex = StaticPreviewFrame;

    public HandoffConstellationLayer()
    {
        IsHitTestVisible = false;
        Focusable = false;
        AddVisualChild(staticVisual);
        AddVisualChild(dynamicVisual);
        renderTimer.Tick += OnRenderTimerTick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override int VisualChildrenCount => 2;

    protected override AutomationPeer? OnCreateAutomationPeer() => null;

    public void Attach(
        Window ownerWindow,
        FrameworkElement userStage,
        FrameworkElement piStage,
        FrameworkElement toolStage,
        FrameworkElement jarvisStage)
    {
        ArgumentNullException.ThrowIfNull(ownerWindow);
        FrameworkElement[] nextStages =
            [userStage, piStage, toolStage, jarvisStage];
        if (nextStages.Any(stage => stage is null))
        {
            throw new ArgumentNullException(nameof(userStage));
        }

        DetachOwnerAndStages();
        owner = ownerWindow;
        stages = nextStages;
        owner.StateChanged += OnOwnerStateChanged;
        owner.IsVisibleChanged += OnOwnerVisibilityChanged;
        foreach (FrameworkElement stage in stages)
        {
            stage.SizeChanged += OnStageSizeChanged;
        }
        QueueRefresh();
    }

    public void SetState(
        double handoffProgress,
        bool isHandoffComplete,
        bool isOwnerReviewPending,
        bool hasActiveTurn,
        ConversationRuntimePhase runtimePhase)
    {
        int nextStage =
            isOwnerReviewPending || isHandoffComplete
                ? 0
                : handoffProgress switch
                {
                    <= 0.75 => 0,
                    < 1.75 => 1,
                    < 2.75 => 2,
                    _ => 3,
                };
        bool rebuild =
            activeStage != nextStage ||
            phase != runtimePhase;
        activeStage = nextStage;
        phase = runtimePhase;
        ownerReviewPending = isOwnerReviewPending;
        handoffComplete = isHandoffComplete;
        activeTurn = hasActiveTurn;
        if (rebuild)
        {
            frameIndex = StaticPreviewFrame;
            QueueRefresh();
            return;
        }
        UpdateTimerState();
    }

    public void Detach()
    {
        StopTimer();
        if (systemParametersSubscribed)
        {
            SystemParameters.StaticPropertyChanged -=
                OnSystemParametersChanged;
            systemParametersSubscribed = false;
        }
        DetachOwnerAndStages();
        ClearVisual(staticVisual);
        ClearVisual(dynamicVisual);
        dynamicFrames.Clear();
    }

    protected override Visual GetVisualChild(int index) =>
        index switch
        {
            0 => staticVisual,
            1 => dynamicVisual,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

    protected override void OnRenderSizeChanged(
        SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        QueueRefresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (!systemParametersSubscribed)
        {
            SystemParameters.StaticPropertyChanged +=
                OnSystemParametersChanged;
            systemParametersSubscribed = true;
        }
        QueueRefresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        StopTimer();
        if (systemParametersSubscribed)
        {
            SystemParameters.StaticPropertyChanged -=
                OnSystemParametersChanged;
            systemParametersSubscribed = false;
        }
    }

    private void OnSystemParametersChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs) =>
        QueueRefresh();

    private void OnOwnerStateChanged(
        object? sender,
        EventArgs eventArgs)
    {
        UpdateTimerState();
        if (owner?.WindowState != WindowState.Minimized)
        {
            QueueRefresh();
        }
    }

    private void OnOwnerVisibilityChanged(
        object sender,
        DependencyPropertyChangedEventArgs eventArgs) =>
        UpdateTimerState();

    private void OnStageSizeChanged(
        object sender,
        SizeChangedEventArgs eventArgs) =>
        QueueRefresh();

    private void OnRenderTimerTick(
        object? sender,
        EventArgs eventArgs)
    {
        if (!CanAnimate())
        {
            UpdateTimerState();
            return;
        }
        frameIndex =
            (frameIndex + 1) %
            HandoffConstellationSceneFactory.RenderSampleHz;
        ReplayDynamicFrame();
    }

    private void QueueRefresh()
    {
        if (refreshQueued || !IsLoaded)
        {
            return;
        }
        refreshQueued = true;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                refreshQueued = false;
                RefreshAll();
            }));
    }

    private void RefreshAll()
    {
        StopTimer();
        dynamicFrames.Clear();
        if (
            SystemParameters.HighContrast ||
            !TryCreateLayout(out HandoffConstellationLayout? layout))
        {
            ClearVisual(staticVisual);
            ClearVisual(dynamicVisual);
            return;
        }

        RetainedVectorScene staticScene =
            HandoffConstellationSceneFactory.CreateStatic(layout);
        DrawingGroup? staticDrawing = BuildFrozenDrawing(
            staticScene,
            StaticAccentColor);
        if (staticDrawing is null)
        {
            ClearVisual(staticVisual);
            ClearVisual(dynamicVisual);
            return;
        }
        Replay(staticVisual, staticDrawing);

        for (
            int index = 0;
            index < HandoffConstellationSceneFactory.RenderSampleHz;
            index++)
        {
            RetainedVectorScene frameScene =
                HandoffConstellationSceneFactory.CreateFocus(
                    layout,
                    activeStage,
                    index);
            RgbFrame accent =
                HandoffConstellationSceneFactory.SampleAccent(index);
            DrawingGroup? frame = BuildFrozenDrawing(
                frameScene,
                Color.FromRgb(accent.Red, accent.Green, accent.Blue));
            if (frame is null)
            {
                dynamicFrames.Clear();
                ClearVisual(dynamicVisual);
                return;
            }
            dynamicFrames.Add(frame);
        }
        ReplayDynamicFrame();
        UpdateTimerState();
    }

    private bool TryCreateLayout(
        out HandoffConstellationLayout layout)
    {
        layout = null!;
        if (
            ActualWidth < 1.0 ||
            ActualHeight < 1.0 ||
            stages.Length != 4 ||
            stages.Any(stage =>
                stage.ActualWidth < 1.0 || stage.ActualHeight < 1.0))
        {
            return false;
        }
        try
        {
            HandoffStageBounds[] bounds = stages
                .Select(stage =>
                {
                    Point origin = stage.TranslatePoint(
                        new Point(0.0, 0.0),
                        this);
                    return new HandoffStageBounds(
                        origin.X,
                        origin.Y,
                        stage.ActualWidth,
                        stage.ActualHeight);
                })
                .ToArray();
            layout = new(
                ActualWidth,
                ActualHeight,
                bounds);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static DrawingGroup? BuildFrozenDrawing(
        RetainedVectorScene scene,
        Color accent)
    {
        VectorSceneCompilationReceipt compilation =
            RetainedVectorSceneCompiler.Compile(scene);
        if (compilation.Result != "compiled-retained-vector-scene")
        {
            return null;
        }

        DrawingGroup group = new();
        using (DrawingContext context = group.Open())
        {
            foreach (VectorCommand command in scene.Commands)
            {
                Color source = command.Material.ColorChannel switch
                {
                    "neutral-ghost" => NeutralGhostColor,
                    "accent" or "active" or "pulse" => accent,
                    _ => Colors.Transparent,
                };
                if (source == Colors.Transparent)
                {
                    return null;
                }
                SolidColorBrush brush = CreateBrush(
                    source,
                    command.Material);
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
                    case VectorEllipseCommand ellipse:
                        context.PushOpacity(ellipse.DrawingOpacity);
                        context.DrawEllipse(
                            null,
                            CreatePen(brush, ellipse.Stroke),
                            ToPoint(ellipse.Center),
                            ellipse.RadiusX,
                            ellipse.RadiusY);
                        context.Pop();
                        break;
                    default:
                        return null;
                }
            }
        }
        group.Freeze();
        return group;
    }

    private static SolidColorBrush CreateBrush(
        Color source,
        VectorMaterial material)
    {
        SolidColorBrush brush = new(
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
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        return pen;
    }

    private static Point ToPoint(VectorPoint point) =>
        new(point.X, point.Y);

    private static byte ToByte(double value) =>
        checked((byte)Math.Round(
            Math.Clamp(value, 0.0, 1.0) * 255.0,
            MidpointRounding.AwayFromZero));

    private void ReplayDynamicFrame()
    {
        if (dynamicFrames.Count == 0)
        {
            ClearVisual(dynamicVisual);
            return;
        }
        frameIndex = Math.Clamp(
            frameIndex,
            0,
            dynamicFrames.Count - 1);
        Replay(dynamicVisual, dynamicFrames[frameIndex]);
    }

    private static void Replay(
        DrawingVisual visual,
        DrawingGroup group)
    {
        using DrawingContext context = visual.RenderOpen();
        foreach (Drawing drawing in group.Children)
        {
            context.DrawDrawing(drawing);
        }
    }

    private static void ClearVisual(DrawingVisual visual)
    {
        using DrawingContext context = visual.RenderOpen();
    }

    private bool CanAnimate() =>
        IsLoaded &&
        owner?.IsVisible == true &&
        owner.WindowState != WindowState.Minimized &&
        !SystemParameters.HighContrast &&
        SystemParameters.ClientAreaAnimation &&
        (RenderCapability.Tier >> 16) > 0 &&
        phase == ConversationRuntimePhase.Ready &&
        activeTurn &&
        !ownerReviewPending &&
        !handoffComplete;

    private void UpdateTimerState()
    {
        if (phase == ConversationRuntimePhase.Faulted)
        {
            StopTimer();
            ClearVisual(dynamicVisual);
            return;
        }
        if (CanAnimate())
        {
            if (!renderTimer.IsEnabled)
            {
                renderTimer.Start();
            }
            return;
        }
        StopTimer();
        frameIndex = StaticPreviewFrame;
        ReplayDynamicFrame();
    }

    private void StopTimer()
    {
        if (renderTimer.IsEnabled)
        {
            renderTimer.Stop();
        }
    }

    private void DetachOwnerAndStages()
    {
        if (owner is not null)
        {
            owner.StateChanged -= OnOwnerStateChanged;
            owner.IsVisibleChanged -= OnOwnerVisibilityChanged;
        }
        foreach (FrameworkElement stage in stages)
        {
            stage.SizeChanged -= OnStageSizeChanged;
        }
        owner = null;
        stages = [];
    }
}
