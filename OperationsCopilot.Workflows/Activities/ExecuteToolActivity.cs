using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using OperationsCopilot.Workflows.Contracts;
using OperationsCopilot.Workflows.Tools;
using OperationsCopilot.Workflows.Tools.Interfaces;

namespace OperationsCopilot.Workflows.Activities;

public class ExecuteToolActivity(IToolRegistry<ICopilotTool> toolRegistry, IApprovalStore approvalStore)
{
    [Function(nameof(ExecuteToolActivity))]
    public async Task<ToolCallResult> RunAsync(
        [ActivityTrigger] ToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var proposal = request.Proposal;
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposal.CallId);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposal.ToolName);

        var tool = toolRegistry.GetToolByName(proposal.ToolName);
        if (tool is null)
            return Error(proposal.CallId, "unknown_tool", "The requested tool is not registered.");

        if (tool.Risk != ToolRisk.ReadOnly || tool.RequiresHuman)
        {
            var approval = request.ApprovalId is null ? null
                : await approvalStore.GetAsync(request.ApprovalId, cancellationToken);
            if (approval?.Approved != true || approval.Proposal != proposal
                || approval.InstanceId != request.InstanceId)
                return Error(proposal.CallId, "approval_required",
                    "This action was not executed. A matching human approval is required.");
        }

        try
        {
            using var arguments = JsonDocument.Parse(proposal.ArgumentsJson);
            if (arguments.RootElement.ValueKind != JsonValueKind.Object)
                return Error(proposal.CallId, "invalid_arguments", "Tool arguments must be a JSON object.");

            var output = await tool.ExecuteAsync(proposal.ArgumentsJson, cancellationToken);
            return new ToolCallResult(proposal.CallId, output);
        }
        catch (JsonException)
        {
            return Error(proposal.CallId, "invalid_arguments", "Tool arguments contain invalid JSON.");
        }
        catch (KeyNotFoundException)
        {
            return Error(proposal.CallId, "invalid_arguments", "A required tool argument is missing.");
        }
        catch (InvalidOperationException)
        {
            return Error(proposal.CallId, "tool_failed", "The tool could not process the supplied arguments or complete the operation.");
        }
    }

    private static ToolCallResult Error(string callId, string code, string message) =>
        new(callId, JsonSerializer.Serialize(new { error = code, message }));
}
