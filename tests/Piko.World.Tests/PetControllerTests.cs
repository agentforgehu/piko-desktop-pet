using Piko.World.Behavior;
using Piko.World.Compiler;
using Piko.World.Geometry;
using Piko.World.Model;

namespace Piko.World.Tests;

public sealed class PetControllerTests
{
    [Fact]
    public void PetMind_ModelExpressionIsAllowlistedBoundedAndChangesEmotion()
    {
        var mind = new PetMind();

        var reaction = mind.ReactToModel(new string('好', 800), "excited", "celebrate");

        Assert.Equal(PetCommand.Celebrate, reaction.Command);
        Assert.Equal(500, reaction.Message.Length);
        Assert.True(reaction.ShouldSpeak);
        Assert.True(reaction.Emotion.Valence > PetEmotionState.Baseline.Valence);
        Assert.True(reaction.Emotion.Arousal > PetEmotionState.Baseline.Arousal);
    }

    private readonly DesktopWorldCompiler _compiler = new();

    [Fact]
    public void PetMind_ConcernStaysSilentAndCelebrationChangesEmotion()
    {
        var mind = new PetMind();
        var baseline = mind.Emotion;

        var concern = mind.React(PetStimulus.SilentConcern, policyAllowsSpeech: true);

        Assert.Equal(PetCommand.Concern, concern.Command);
        Assert.False(concern.ShouldSpeak);
        Assert.True(concern.Emotion.Valence < baseline.Valence);

        var celebration = mind.React(PetStimulus.Celebrate, policyAllowsSpeech: true);

        Assert.Equal(PetCommand.Celebrate, celebration.Command);
        Assert.True(celebration.ShouldSpeak);
        Assert.True(celebration.Emotion.Valence > concern.Emotion.Valence);
        Assert.True(celebration.Emotion.Arousal > concern.Emotion.Arousal);
    }

    [Fact]
    public void Reaction_DrivesConcernedModeAndSpeechPolicy()
    {
        var world = World();
        var controller = new PetController();
        controller.Initialize(world);
        var mind = new PetMind();
        var reaction = mind.React(PetStimulus.SilentConcern, policyAllowsSpeech: false);

        controller.Tick(world, Input(reaction: reaction, emotion: mind.Emotion), 0.016);

        Assert.Equal(PetMode.Concerned, controller.State.Mode);
        Assert.False(controller.State.SpeechVisible);
        Assert.Contains("安静", controller.State.Message);
        Assert.Equal(mind.Emotion, controller.State.Emotion);
    }

    [Fact]
    public void FullscreenPolicy_RecognizesExactAndBorderlessCoverage()
    {
        var monitor = new PixelRect(1920, 0, 3840, 1080);

        Assert.True(FullscreenWindowPolicy.CoversMonitor(
            new PixelRect(1920, 0, 3840, 1080),
            monitor));
        Assert.True(FullscreenWindowPolicy.CoversMonitor(
            new PixelRect(1919, -1, 3841, 1081),
            monitor));
    }

    [Fact]
    public void FullscreenPolicy_DoesNotTreatMaximizedWorkAreaAsFullscreen()
    {
        var monitor = new PixelRect(0, 0, 1920, 1080);
        var maximizedWorkArea = new PixelRect(0, 0, 1920, 1040);

        Assert.False(FullscreenWindowPolicy.CoversMonitor(maximizedWorkArea, monitor));
        Assert.False(FullscreenWindowPolicy.CoversMonitor(default, monitor));
    }

    [Fact]
    public void Initialize_LandsOnPrimaryMonitorFloor()
    {
        var world = World();
        var controller = new PetController();

        controller.Initialize(world);

        Assert.Equal(PetMode.Standing, controller.State.Mode);
        Assert.Equal(1040, controller.State.Feet.Y);
        Assert.StartsWith("monitor:", controller.State.SupportSurfaceId);
    }

    [Fact]
    public void DragRelease_LandsOnWindowAndFollowsOwner()
    {
        var controller = new PetController();
        var first = World(Window("owner", new PixelRect(200, 400, 800, 900), 0));
        controller.Initialize(first);

        controller.Tick(first, Input(isDragging: true, dragFeet: new PixelPoint(450, 398)), 0.016);
        Advance(controller, first, 4);
        Assert.Equal("owner", controller.State.SupportOwnerWindowId);
        Assert.Equal(400, controller.State.Feet.Y);

        var moved = World(Window("owner", new PixelRect(300, 320, 900, 820), 0));
        controller.Tick(moved, Input(), 0.016);

        Assert.Equal(550, controller.State.Feet.X);
        Assert.Equal(320, controller.State.Feet.Y);
        Assert.Equal(PetMode.Standing, controller.State.Mode);
    }

    [Fact]
    public void MissingOwner_DetachesAndFalls()
    {
        var controller = new PetController();
        var withWindow = World(Window("owner", new PixelRect(200, 400, 800, 900), 0));
        controller.Initialize(withWindow);
        controller.Tick(withWindow, Input(isDragging: true, dragFeet: new PixelPoint(450, 398)), 0.016);
        Advance(controller, withWindow, 4);
        Assert.Equal("owner", controller.State.SupportOwnerWindowId);

        controller.Tick(World(), Input(), 0.016);

        Assert.Equal(PetMode.Falling, controller.State.Mode);
        Assert.Null(controller.State.SupportOwnerWindowId);
    }

    [Fact]
    public void PeekCommand_MovesPetToRecallableScreenEdge()
    {
        var world = World();
        var controller = new PetController();
        controller.Initialize(world);

        controller.Tick(world, Input(command: PetCommand.Peek), 0.016);

        Assert.Equal(PetMode.Peeking, controller.State.Mode);
        Assert.True(controller.State.Feet.X > world.Source.VirtualDesktop.Right);
        Assert.Contains("眼睛", controller.State.Message);
    }

    [Fact]
    public void RecallCommand_AlwaysReturnsToPrimaryFloor()
    {
        var world = World();
        var controller = new PetController();
        controller.Initialize(world);
        controller.Tick(world, Input(command: PetCommand.Peek), 0.016);

        controller.Tick(world, Input(command: PetCommand.Recall), 0.016);

        Assert.Equal(PetMode.Standing, controller.State.Mode);
        Assert.Equal(1040, controller.State.Feet.Y);
        Assert.InRange(controller.State.Feet.X, 0, 1920);
    }

    [Fact]
    public void FileActivity_UsesDedicatedObservingState()
    {
        var world = World();
        var controller = new PetController();
        controller.Initialize(world);
        var activity = new FileActivitySignal(
            true,
            FileActivityConfidence.ActivityOnly,
            null,
            "test");

        controller.Tick(world, Input(fileActivity: activity), 0.016);

        Assert.Equal(PetMode.ObservingTransfer, controller.State.Mode);
        Assert.Contains("文件", controller.State.Message);
    }

    [Fact]
    public void ClimbCommand_ReachesEligibleWindowTop()
    {
        var world = World(Window("climb-target", new PixelRect(200, 400, 800, 900), 0));
        var controller = new PetController();
        controller.Initialize(world);

        controller.Tick(world, Input(command: PetCommand.Climb), 0.016);
        Assert.Equal(PetMode.Climbing, controller.State.Mode);

        Advance(controller, world, 70, 0.1);

        Assert.Equal(PetMode.Standing, controller.State.Mode);
        Assert.Equal("climb-target", controller.State.SupportOwnerWindowId);
        Assert.Equal(400, controller.State.Feet.Y);
    }

    [Fact]
    public void JumpCommand_CanLandOnAnotherWindow()
    {
        var world = World(
            Window("from", new PixelRect(200, 400, 700, 900), 0),
            Window("to", new PixelRect(850, 320, 1150, 820), 1));
        var controller = new PetController();
        controller.Initialize(world);
        controller.Tick(world, Input(isDragging: true, dragFeet: new PixelPoint(450, 398)), 0.016);
        Advance(controller, world, 4);
        Assert.Equal("from", controller.State.SupportOwnerWindowId);

        controller.Tick(world, Input(command: PetCommand.Jump), 0.016);
        Assert.Equal(PetMode.Jumping, controller.State.Mode);
        Advance(controller, world, 15, 0.1);

        Assert.Equal(PetMode.Standing, controller.State.Mode);
        Assert.Equal("to", controller.State.SupportOwnerWindowId);
    }

    [Fact]
    public void IdlePointer_TriggersApproachWithoutBlockingPointer()
    {
        var world = World();
        var controller = new PetController();
        controller.Initialize(world);
        var startingX = controller.State.Feet.X;
        var input = Input(
            cursor: new PixelPoint(startingX - 220, 1000),
            cursorIsIdle: true,
            pointerAwareness: true);

        controller.Tick(world, input, 0.016);
        Assert.Equal(PetMode.PointerDwell, controller.State.Mode);
        Assert.Equal(startingX, controller.State.Feet.X);

        for (var index = 0; index < 30; index++)
        {
            controller.Tick(world, input, 0.1);
        }

        Assert.True(controller.State.Feet.X < startingX);
        Assert.True(Math.Abs(controller.State.Feet.X - input.Cursor.X) >= 65);
    }

    [Fact]
    public void RestCommand_TreatsCurrentSurfaceAsFurniture()
    {
        var world = World(Window("bed", new PixelRect(200, 400, 800, 900), 0));
        var controller = new PetController();
        controller.Initialize(world);
        controller.Tick(world, Input(isDragging: true, dragFeet: new PixelPoint(450, 398)), 0.016);
        Advance(controller, world, 4);

        controller.Tick(world, Input(command: PetCommand.Rest), 0.016);

        Assert.Equal(PetMode.Resting, controller.State.Mode);
        Assert.Equal("bed", controller.State.SupportOwnerWindowId);
        Assert.Contains("窗口", controller.State.Message);
    }

    [Fact]
    public void DeviceProjection_MapsRichStateToSimpleEyes()
    {
        var state = PetBodyState.Create(new PixelPoint(10, 20)) with
        {
            Mode = PetMode.ObservingTransfer,
            Message = "观察中"
        };

        var device = DevicePetState.From(7, state);

        Assert.Equal(7, device.Sequence);
        Assert.Equal("observingtransfer", device.Mode);
        Assert.Equal("focused", device.EyeShape);
    }

    private DesktopWorld World(params WindowSnapshot[] windows)
    {
        var monitor = new MonitorSnapshot(
            "primary",
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1040),
            96,
            96,
            true);
        var snapshot = DesktopSnapshot.Create(
            new[] { monitor },
            windows,
            new PixelPoint(100, 100),
            DateTimeOffset.UnixEpoch);
        return _compiler.Compile(snapshot);
    }

    private static WindowSnapshot Window(string id, PixelRect bounds, int zOrder) => new(
        id,
        bounds,
        zOrder,
        true,
        false,
        false,
        false,
        true,
        null,
        "primary",
        96,
        96);

    private static PetRuntimeInput Input(
        bool isDragging = false,
        PixelPoint? dragFeet = null,
        PetCommand? command = null,
        FileActivitySignal? fileActivity = null,
        PixelPoint? cursor = null,
        bool cursorIsIdle = false,
        bool pointerAwareness = false,
        PetReaction? reaction = null,
        PetEmotionState emotion = default) => new(
        cursor ?? new PixelPoint(100, 100),
        cursorIsIdle,
        isDragging,
        dragFeet,
        fileActivity ?? FileActivitySignal.None,
        command,
        false,
        pointerAwareness,
        true,
        reaction,
        emotion);

    private static void Advance(
        PetController controller,
        DesktopWorld world,
        int count,
        double elapsed = 0.016)
    {
        for (var index = 0; index < count; index++)
        {
            controller.Tick(world, Input(), elapsed);
        }
    }
}

