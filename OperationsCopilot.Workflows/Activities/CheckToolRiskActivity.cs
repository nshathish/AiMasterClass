using Microsoft.Azure.Functions.Worker;
using OperationsCopilot.Workflows.Tools;
using OperationsCopilot.Workflows.Tools.Interfaces;

namespace OperationsCopilot.Workflows.Activities;

public class CheckToolRiskActivity(IToolRegistry<ICopilotTool> toolRegistry)
{
    [Function(nameof(CheckToolRiskActivity))]
    public ToolRiskResult Run(
        [ActivityTrigger] string toolName)
    {
        var tool = toolRegistry.GetToolByName(toolName)
                   ?? throw new InvalidOperationException(
                       $"Unknown tool: {toolName}");

        return new ToolRiskResult(
            tool.Risk,
            tool.RequiresHuman);
    }
}

public record ToolRiskResult(
    ToolRisk Risk,
    bool RequiresHuman)
{
    public bool NeedsApproval =>
        Risk != ToolRisk.ReadOnly || RequiresHuman;
}