using OperationsCopilot.Workflows.Tools.Interfaces;

namespace OperationsCopilot.Workflows.Tools;

public sealed class CopilotToolRegistry : IToolRegistry<ICopilotTool>
{
    private readonly IReadOnlyDictionary<string, ICopilotTool> _tools;

    public CopilotToolRegistry(IEnumerable<ICopilotTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        _tools = tools.ToDictionary(
            t => t.Name,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ICopilotTool> Tools =>
        _tools.Values.ToList();

    public ICopilotTool? GetToolByName(string name) =>
        _tools.GetValueOrDefault(name);
}