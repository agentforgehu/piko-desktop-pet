namespace Piko.Runtime;

public sealed class RuntimePaths
{
    public RuntimePaths(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PikoDesktopPet");
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }
    public string StatusFile => Path.Combine(Root, "runtime-status.json");
    public string SettingsFile => Path.Combine(Root, "runtime-settings.json");
    public string MemoryDatabaseFile => Path.Combine(Root, "memory.db");
    public string AgentAuditFile => Path.Combine(Root, "agent-audit.jsonl");
}
