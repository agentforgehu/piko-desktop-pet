namespace Piko.Agent.Tools;

public sealed class AgentToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _tools = new(StringComparer.Ordinal);

    public void Register(IAgentTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        tool.Descriptor.Validate();
        if (!_tools.TryAdd(tool.Descriptor.Name, tool))
        {
            throw new InvalidOperationException($"Agent tool '{tool.Descriptor.Name}' is already registered.");
        }
    }

    public bool TryGet(string name, out IAgentTool tool) => _tools.TryGetValue(name, out tool!);

    public IReadOnlyList<AgentToolDescriptor> Descriptors =>
        _tools.Values.Select(tool => tool.Descriptor).OrderBy(item => item.Name).ToArray();
}
