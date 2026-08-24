namespace Piko.World.Compiler;

public sealed record WorldCompilerOptions(
    double MinimumSurfaceWidth = 32,
    bool IncludeMonitorFloors = true);
