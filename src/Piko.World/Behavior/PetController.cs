using Piko.World.Geometry;
using Piko.World.Model;

namespace Piko.World.Behavior;

public sealed class PetController
{
    private readonly PetBehaviorOptions _options;
    private int _autonomousCycle;

    public PetController(PetBehaviorOptions? options = null)
    {
        _options = options ?? new PetBehaviorOptions();
    }

    public PetBodyState State { get; private set; } = PetBodyState.Create(default);

    public void Initialize(DesktopWorld world, PixelPoint? preferredFeet = null)
    {
        var floor = PreferredFloor(world);
        var feet = preferredFeet ?? (floor is null
            ? new PixelPoint(world.Source.VirtualDesktop.Left + 160, world.Source.VirtualDesktop.Top + 160)
            : new PixelPoint(floor.Horizontal.Start + floor.Horizontal.Length * 0.72, floor.Y));

        State = PetBodyState.Create(feet);
        State = LandOrFall(world, feet, default, PetMode.Standing, "我来啦");
    }

    public PetBodyState Tick(DesktopWorld world, PetRuntimeInput input, double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(input);

        var dt = Math.Clamp(elapsedSeconds, 0.001, 0.1);
        var state = State with { ModeElapsedSeconds = State.ModeElapsedSeconds + dt };

        if (input.IsDragging && input.DragFeet is { } dragFeet)
        {
            State = state with
            {
                Feet = dragFeet,
                Velocity = default,
                Mode = PetMode.Dragging,
                SupportSurfaceId = null,
                SupportOwnerWindowId = null,
                TargetId = null,
                TargetX = null,
                ModeElapsedSeconds = 0,
                Message = "被你抱起来了"
            };
            return State;
        }

        if (state.Mode == PetMode.Dragging)
        {
            state = state with
            {
                Mode = PetMode.Falling,
                Velocity = new PixelPoint(0, 80),
                ModeElapsedSeconds = 0,
                Message = "轻轻落下"
            };
        }

        if (input.Command is { } command)
        {
            state = StartCommand(world, state, command, input.Cursor);
        }

        state = state.Mode switch
        {
            PetMode.Falling or PetMode.Jumping => AdvanceAirborne(world, state, dt),
            PetMode.Climbing => AdvanceClimbing(world, state, dt),
            PetMode.Walking => AdvanceWalking(world, state, dt),
            PetMode.Peeking when state.ModeElapsedSeconds >= 5 => Recall(world, "躲猫猫结束"),
            PetMode.Greeting when state.ModeElapsedSeconds >= 1.5 => ToStanding(world, state, "陪着你"),
            PetMode.Resting when state.ModeElapsedSeconds >= 8 => ToStanding(world, state, "睡醒了"),
            PetMode.PointerDwell => AdvancePointerApproach(world, state, dt),
            _ => FollowSupportOrFall(world, state)
        };

        if (input.FileActivity.IsActive && state.Mode is not (PetMode.Dragging or PetMode.Peeking))
        {
            state = state with
            {
                Mode = PetMode.ObservingTransfer,
                ModeElapsedSeconds = state.Mode == PetMode.ObservingTransfer ? state.ModeElapsedSeconds : 0,
                Message = input.FileActivity.Progress is { } progress
                    ? $"进度大约 {progress:P0}"
                    : "正在观察文件活动"
            };
        }
        else if (state.Mode == PetMode.ObservingTransfer)
        {
            state = ToStanding(world, state, "完成了吗？");
        }

        if (input.PointerAwarenessEnabled && input.CursorIsIdle &&
            state.Mode == PetMode.Standing && TryStartPointerDwell(world, state, input.Cursor, out var pointerState))
        {
            state = pointerState;
        }

        if (input.AutonomousBehaviorEnabled && state.Mode == PetMode.Standing &&
            state.ModeElapsedSeconds >= _options.AutonomousIntervalSeconds)
        {
            var autonomousCommand = NextAutonomousCommand(input.WindowExplorationEnabled);
            state = StartCommand(world, state, autonomousCommand, input.Cursor);
        }

        State = state;
        return State;
    }

    private PetBodyState StartCommand(
        DesktopWorld world,
        PetBodyState state,
        PetCommand command,
        PixelPoint cursor) => command switch
    {
        PetCommand.Recall => Recall(world, "回到这里啦"),
        PetCommand.Walk => StartWalking(world, state),
        PetCommand.Climb => StartClimbing(world, state),
        PetCommand.Jump => StartJumping(world, state),
        PetCommand.Peek => StartPeeking(world, state),
        PetCommand.Rest => state with
        {
            Mode = PetMode.Resting,
            Velocity = default,
            ModeElapsedSeconds = 0,
            Message = "把窗口当成小床"
        },
        PetCommand.Greet => state with
        {
            Mode = PetMode.Greeting,
            ModeElapsedSeconds = 0,
            Message = "你好呀"
        },
        _ => state
    };

    private PetCommand NextAutonomousCommand(bool windowExplorationEnabled)
    {
        var commands = windowExplorationEnabled
            ? new[] { PetCommand.Walk, PetCommand.Rest, PetCommand.Jump, PetCommand.Climb, PetCommand.Peek }
            : new[] { PetCommand.Walk, PetCommand.Rest, PetCommand.Greet };
        return commands[_autonomousCycle++ % commands.Length];
    }

    private PetBodyState StartWalking(DesktopWorld world, PetBodyState state)
    {
        var supported = FollowSupportOrFall(world, state);
        if (supported.Mode == PetMode.Falling)
        {
            return supported;
        }

        return supported with
        {
            Mode = PetMode.Walking,
            FacingRight = !supported.FacingRight,
            ModeElapsedSeconds = 0,
            Message = "沿着窗口散步"
        };
    }

    private PetBodyState AdvanceWalking(DesktopWorld world, PetBodyState state, double dt)
    {
        var supported = FollowSupportOrFall(world, state);
        if (supported.Mode == PetMode.Falling)
        {
            return supported;
        }

        var surface = FindSupport(world, supported);
        if (surface is null)
        {
            return BeginFall(supported);
        }

        var direction = supported.FacingRight ? 1 : -1;
        var nextX = supported.Feet.X + direction * _options.WalkSpeed * dt;
        var minX = surface.Horizontal.Start + _options.MinimumSurfaceInset;
        var maxX = surface.Horizontal.End - _options.MinimumSurfaceInset;

        if (nextX <= minX || nextX >= maxX || supported.ModeElapsedSeconds >= 4)
        {
            return supported with
            {
                Feet = new PixelPoint(Math.Clamp(nextX, minX, maxX), surface.Y),
                Mode = PetMode.Standing,
                FacingRight = !supported.FacingRight,
                ModeElapsedSeconds = 0,
                Message = "看看四周"
            };
        }

        return supported with
        {
            Feet = new PixelPoint(nextX, surface.Y),
            SupportLocalX = ResolveLocalX(world, surface, nextX)
        };
    }

    private PetBodyState StartJumping(DesktopWorld world, PetBodyState state)
    {
        var candidates = world.Surfaces
            .Where(surface => surface.Id != state.SupportSurfaceId)
            .Select(surface => new
            {
                Surface = surface,
                X = surface.Horizontal.Start + surface.Horizontal.Length / 2
            })
            .Where(item => item.Surface.Horizontal.Length >= 40)
            .Where(item => Math.Abs(item.X - state.Feet.X) is >= 80 and <= 650)
            .Where(item => Math.Abs(item.Surface.Y - state.Feet.Y) <= 360)
            .OrderBy(item => Math.Abs(item.X - state.Feet.X) + Math.Abs(item.Surface.Y - state.Feet.Y))
            .FirstOrDefault();

        if (candidates is null)
        {
            return state with { ModeElapsedSeconds = 0, Message = "没有找到安全的跳台" };
        }

        const double flightTime = 0.9;
        var dx = candidates.X - state.Feet.X;
        var dy = candidates.Surface.Y - state.Feet.Y;
        var velocity = new PixelPoint(
            dx / flightTime,
            (dy - 0.5 * _options.Gravity * flightTime * flightTime) / flightTime);

        return state with
        {
            Mode = PetMode.Jumping,
            Velocity = velocity,
            FacingRight = dx >= 0,
            SupportSurfaceId = null,
            SupportOwnerWindowId = null,
            TargetId = candidates.Surface.Id,
            TargetX = candidates.X,
            ModeElapsedSeconds = 0,
            Message = "跳到另一个窗口"
        };
    }

    private PetBodyState StartClimbing(DesktopWorld world, PetBodyState state)
    {
        var target = world.Source.Windows
            .Where(window => window.IsEligible && !window.IsMinimized)
            .Select(window => new
            {
                Window = window,
                Distance = Math.Min(
                    Math.Abs(state.Feet.X - window.Bounds.Left),
                    Math.Abs(state.Feet.X - window.Bounds.Right)) +
                    Math.Abs(state.Feet.Y - window.Bounds.Bottom) * 0.35
            })
            .OrderBy(item => item.Distance)
            .FirstOrDefault();

        if (target is null)
        {
            return state with { ModeElapsedSeconds = 0, Message = "没有可以攀爬的窗口" };
        }

        var rightSide = Math.Abs(state.Feet.X - target.Window.Bounds.Right) <
                        Math.Abs(state.Feet.X - target.Window.Bounds.Left);
        var sideX = rightSide
            ? target.Window.Bounds.Right - _options.MinimumSurfaceInset
            : target.Window.Bounds.Left + _options.MinimumSurfaceInset;

        return state with
        {
            Feet = new PixelPoint(sideX, Math.Max(state.Feet.Y, target.Window.Bounds.Bottom)),
            Velocity = default,
            Mode = PetMode.Climbing,
            FacingRight = !rightSide,
            SupportSurfaceId = null,
            SupportOwnerWindowId = null,
            TargetId = target.Window.Id,
            TargetX = sideX,
            ModeElapsedSeconds = 0,
            Message = "沿着窗口边缘往上爬"
        };
    }

    private PetBodyState AdvanceClimbing(DesktopWorld world, PetBodyState state, double dt)
    {
        var window = world.Source.Windows.FirstOrDefault(item => item.Id == state.TargetId && item.IsEligible);
        if (window is null)
        {
            return BeginFall(state with { Message = "窗口消失，安全落下" });
        }

        var y = state.Feet.Y - _options.ClimbSpeed * dt;
        if (y > window.Bounds.Top)
        {
            return state with { Feet = new PixelPoint(state.Feet.X, y) };
        }

        var surface = world.Surfaces.FirstOrDefault(item =>
            item.OwnerWindowId == window.Id &&
            ContainsWithInset(item, state.Feet.X, _options.MinimumSurfaceInset));

        return surface is null
            ? BeginFall(state with { Feet = new PixelPoint(state.Feet.X, window.Bounds.Top) })
            : Land(world, state, surface, state.Feet.X, "爬上来啦");
    }

    private PetBodyState StartPeeking(DesktopWorld world, PetBodyState state)
    {
        var monitor = PreferredMonitor(world);
        if (monitor is null)
        {
            return state;
        }

        return state with
        {
            Feet = new PixelPoint(monitor.Bounds.Right + 42, monitor.WorkArea.Top + monitor.WorkArea.Height * 0.58),
            Velocity = default,
            Mode = PetMode.Peeking,
            FacingRight = false,
            SupportSurfaceId = null,
            SupportOwnerWindowId = null,
            TargetId = monitor.Id,
            TargetX = null,
            ModeElapsedSeconds = 0,
            Message = "只露出眼睛看看你"
        };
    }

    private bool TryStartPointerDwell(
        DesktopWorld world,
        PetBodyState state,
        PixelPoint cursor,
        out PetBodyState result)
    {
        result = state;
        var support = FindSupport(world, state);
        if (support is null || Math.Abs(cursor.Y - support.Y) > 240)
        {
            return false;
        }

        var distance = Math.Abs(cursor.X - state.Feet.X);
        if (distance > _options.PointerApproachRadius || distance < _options.PointerStopDistance)
        {
            return false;
        }

        var targetX = cursor.X + (cursor.X >= state.Feet.X
            ? -_options.PointerStopDistance
            : _options.PointerStopDistance);
        targetX = Math.Clamp(
            targetX,
            support.Horizontal.Start + _options.MinimumSurfaceInset,
            support.Horizontal.End - _options.MinimumSurfaceInset);

        result = state with
        {
            FacingRight = cursor.X >= state.Feet.X,
            Mode = PetMode.PointerDwell,
            ModeElapsedSeconds = 0,
            TargetX = targetX,
            Message = "去鼠标旁边看看"
        };
        return true;
    }

    private PetBodyState AdvancePointerApproach(DesktopWorld world, PetBodyState state, double dt)
    {
        var supported = FollowSupportOrFall(world, state);
        if (supported.Mode == PetMode.Falling)
        {
            return supported;
        }

        if (supported.TargetX is null)
        {
            return supported.ModeElapsedSeconds >= 3
                ? ToStanding(world, supported, "不打扰你")
                : supported;
        }

        var surface = FindSupport(world, supported);
        if (surface is null)
        {
            return BeginFall(supported);
        }

        var distance = supported.TargetX.Value - supported.Feet.X;
        var step = Math.Sign(distance) * _options.WalkSpeed * 1.25 * dt;
        if (Math.Abs(distance) <= Math.Abs(step) + 2)
        {
            var x = supported.TargetX.Value;
            return supported with
            {
                Feet = new PixelPoint(x, surface.Y),
                SupportLocalX = ResolveLocalX(world, surface, x),
                TargetX = null,
                ModeElapsedSeconds = 0,
                Message = "在鼠标旁边待一会儿"
            };
        }

        var nextX = supported.Feet.X + step;
        return supported with
        {
            Feet = new PixelPoint(nextX, surface.Y),
            FacingRight = step >= 0,
            SupportLocalX = ResolveLocalX(world, surface, nextX)
        };
    }

    private PetBodyState AdvanceAirborne(DesktopWorld world, PetBodyState state, double dt)
    {
        var previous = state.Feet;
        var velocity = new PixelPoint(
            state.Velocity.X,
            state.Velocity.Y + _options.Gravity * dt);
        var next = new PixelPoint(
            previous.X + velocity.X * dt,
            previous.Y + velocity.Y * dt);

        if (velocity.Y >= 0)
        {
            var landing = world.Surfaces
                .Where(surface => previous.Y <= surface.Y && next.Y >= surface.Y)
                .Where(surface => ContainsWithInset(surface, next.X, _options.MinimumSurfaceInset))
                .OrderBy(surface => surface.Y)
                .FirstOrDefault();

            if (landing is not null)
            {
                return Land(world, state, landing, next.X, "稳稳落下");
            }
        }

        var virtualDesktop = world.Source.VirtualDesktop;
        if (next.Y > virtualDesktop.Bottom + 300 ||
            next.X < virtualDesktop.Left - 400 || next.X > virtualDesktop.Right + 400)
        {
            return Recall(world, "差点跑丢，回来啦");
        }

        return state with { Feet = next, Velocity = velocity };
    }

    private PetBodyState FollowSupportOrFall(DesktopWorld world, PetBodyState state)
    {
        if (state.Mode is PetMode.Falling or PetMode.Jumping or PetMode.Climbing or PetMode.Peeking)
        {
            return state;
        }

        var support = FindSupport(world, state);
        if (support is null)
        {
            return BeginFall(state);
        }

        var x = state.Feet.X;
        if (state.SupportOwnerWindowId is { } ownerId)
        {
            var owner = world.Source.Windows.FirstOrDefault(window => window.Id == ownerId && window.IsEligible);
            if (owner is null)
            {
                return BeginFall(state);
            }

            x = owner.Bounds.Left + state.SupportLocalX;
            support = world.Surfaces.FirstOrDefault(surface =>
                surface.OwnerWindowId == ownerId &&
                ContainsWithInset(surface, x, _options.MinimumSurfaceInset));
            if (support is null)
            {
                return BeginFall(state with { Feet = new PixelPoint(x, state.Feet.Y) });
            }
        }

        return state with
        {
            Feet = new PixelPoint(x, support.Y),
            SupportSurfaceId = support.Id,
            SupportOwnerWindowId = support.OwnerWindowId
        };
    }

    private PetBodyState ToStanding(DesktopWorld world, PetBodyState state, string message)
    {
        var followed = FollowSupportOrFall(world, state);
        return followed.Mode == PetMode.Falling
            ? followed
            : followed with { Mode = PetMode.Standing, ModeElapsedSeconds = 0, Message = message };
    }

    private PetBodyState Recall(DesktopWorld world, string message)
    {
        var floor = PreferredFloor(world);
        if (floor is null)
        {
            var desktop = world.Source.VirtualDesktop;
            return PetBodyState.Create(new PixelPoint(
                desktop.Left + desktop.Width / 2,
                desktop.Top + desktop.Height / 2));
        }

        return Land(
            world,
            State,
            floor,
            floor.Horizontal.Start + floor.Horizontal.Length * 0.72,
            message);
    }

    private PetBodyState LandOrFall(
        DesktopWorld world,
        PixelPoint feet,
        PixelPoint velocity,
        PetMode desiredMode,
        string message)
    {
        var surface = world.Surfaces
            .Where(item => ContainsWithInset(item, feet.X, _options.MinimumSurfaceInset))
            .Where(item => item.Y >= feet.Y - 4)
            .OrderBy(item => item.Y)
            .FirstOrDefault();

        if (surface is null)
        {
            return PetBodyState.Create(feet) with { Velocity = velocity, Message = message };
        }

        return Land(world, PetBodyState.Create(feet), surface, feet.X, message) with { Mode = desiredMode };
    }

    private static PetBodyState BeginFall(PetBodyState state) => state with
    {
        Mode = PetMode.Falling,
        Velocity = new PixelPoint(state.Velocity.X, Math.Max(40, state.Velocity.Y)),
        SupportSurfaceId = null,
        SupportOwnerWindowId = null,
        TargetId = null,
        TargetX = null,
        ModeElapsedSeconds = 0,
        Message = "落脚点变化，正在降落"
    };

    private static PetBodyState Land(
        DesktopWorld world,
        PetBodyState state,
        Surface surface,
        double x,
        string message) => state with
    {
        Feet = new PixelPoint(x, surface.Y),
        Velocity = default,
        Mode = PetMode.Standing,
        SupportSurfaceId = surface.Id,
        SupportOwnerWindowId = surface.OwnerWindowId,
        SupportLocalX = ResolveLocalX(world, surface, x),
        TargetId = null,
        TargetX = null,
        ModeElapsedSeconds = 0,
        Message = message
    };

    private static double ResolveLocalX(DesktopWorld world, Surface surface, double x)
    {
        if (surface.OwnerWindowId is null)
        {
            return x - surface.Horizontal.Start;
        }

        var owner = world.Source.Windows.FirstOrDefault(window => window.Id == surface.OwnerWindowId);
        return owner is null ? x - surface.Horizontal.Start : x - owner.Bounds.Left;
    }

    private static Surface? FindSupport(DesktopWorld world, PetBodyState state)
    {
        if (state.SupportSurfaceId is { } surfaceId)
        {
            var exact = world.Surfaces.FirstOrDefault(surface => surface.Id == surfaceId);
            if (exact is not null)
            {
                return exact;
            }
        }

        return state.SupportOwnerWindowId is { } ownerId
            ? world.Surfaces.FirstOrDefault(surface => surface.OwnerWindowId == ownerId)
            : world.Surfaces
                .Where(surface => surface.Kind == SurfaceKind.MonitorFloor)
                .OrderBy(surface => Math.Abs(surface.Y - state.Feet.Y))
                .FirstOrDefault();
    }

    private static bool ContainsWithInset(Surface surface, double x, double inset)
    {
        var usableInset = Math.Min(inset, surface.Horizontal.Length / 3);
        return x >= surface.Horizontal.Start + usableInset &&
               x <= surface.Horizontal.End - usableInset;
    }

    private static Surface? PreferredFloor(DesktopWorld world)
    {
        var monitor = PreferredMonitor(world);
        return world.Surfaces.FirstOrDefault(surface =>
                   surface.Kind == SurfaceKind.MonitorFloor && surface.MonitorId == monitor?.Id)
               ?? world.Surfaces.FirstOrDefault(surface => surface.Kind == SurfaceKind.MonitorFloor);
    }

    private static MonitorSnapshot? PreferredMonitor(DesktopWorld world) =>
        world.Source.Monitors.FirstOrDefault(monitor => monitor.IsPrimary)
        ?? world.Source.Monitors.FirstOrDefault();
}
