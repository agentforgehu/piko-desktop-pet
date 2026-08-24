namespace Piko.World.Behavior;

public enum FileActivityConfidence
{
    None,
    ActivityOnly,
    Estimated,
    Exact
}

public sealed record FileActivitySignal(
    bool IsActive,
    FileActivityConfidence Confidence,
    double? Progress,
    string Source)
{
    public static FileActivitySignal None { get; } =
        new(false, FileActivityConfidence.None, null, "none");
}
