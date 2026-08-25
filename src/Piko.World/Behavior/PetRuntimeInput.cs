using Piko.World.Geometry;

namespace Piko.World.Behavior;

public sealed record PetRuntimeInput(
    PixelPoint Cursor,
    bool CursorIsIdle,
    bool IsDragging,
    PixelPoint? DragFeet,
    FileActivitySignal FileActivity,
    PetCommand? Command = null,
    bool AutonomousBehaviorEnabled = true,
    bool PointerAwarenessEnabled = true,
    bool WindowExplorationEnabled = true,
    PetReaction? Reaction = null,
    PetEmotionState Emotion = default);
