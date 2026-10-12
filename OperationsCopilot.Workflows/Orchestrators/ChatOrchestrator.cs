using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using OperationsCopilot.Workflows.Activities;
using OperationsCopilot.Workflows.Contracts;

namespace OperationsCopilot.Workflows.Orchestrators;

public class ChatOrchestrator
{
    private const int MaxTurns = 10;

    [Function(nameof(ChatOrchestrator))]
    public static async Task<SupportIntent> RunAsync(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var request = context.GetInput<ChatRequest>()
                      ?? throw new ArgumentException("A chat request is required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Message);

        var logger = context.CreateReplaySafeLogger(nameof(ChatOrchestrator));
        var turnInput = new ModelTurnInput(request.Message);

        for (var turn = 1; turn <= MaxTurns; turn++)
        {
            context.SetCustomStatus(new { phase = "generating", turn, maxTurns = MaxTurns });
            logger.LogInformation("Generating agent turn {Turn} of {MaxTurns}.", turn, MaxTurns);

            var result = await context.CallActivityAsync<ModelTurnResult>(
                nameof(GenerateAgentTurnActivity), turnInput);

            if (result.ToolCalls is null)
                throw new InvalidOperationException("The model activity returned an invalid turn.");

            if (result.FinalResult is not null)
            {
                if (result.ToolCalls.Count > 0)
                    throw new InvalidOperationException(
                        "A model turn cannot contain both a final result and tool calls.");

                context.SetCustomStatus(new { phase = "completed", turn });
                return result.FinalResult;
            }

            if (result.ToolCalls.Count == 0 || string.IsNullOrWhiteSpace(result.ResponseId))
                throw new InvalidOperationException("A tool-call turn requires tool calls and a response ID.");

            // Do not execute tools if there is no remaining model turn to process their results.
            if (turn == MaxTurns)
                break;

            var callIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var proposal in result.ToolCalls)
            {
                if (string.IsNullOrWhiteSpace(proposal.CallId)
                    || string.IsNullOrWhiteSpace(proposal.ToolName)
                    || string.IsNullOrWhiteSpace(proposal.ArgumentsJson)
                    || !callIds.Add(proposal.CallId))
                    throw new InvalidOperationException(
                        "The model activity returned an invalid or duplicate tool call.");
            }

            var toolResults = new List<ToolCallResult>();
            foreach (var proposal in result.ToolCalls)
            {
                // STEP 1: Check tool risk
                string? approvalId = null;
                var risk = await context.CallActivityAsync<ToolRiskResult>(
                    nameof(CheckToolRiskActivity),
                    proposal.ToolName);

                // STEP 2: Handle tools requiring approval
                if (risk.NeedsApproval)
                {
                    var approval = new ApprovalRequest(
                        context.NewGuid().ToString("N"), context.InstanceId, proposal,
                        context.CurrentUtcDateTime.AddHours(24), CustomerMessage: request.Message);
                    await context.CallActivityAsync(nameof(CreateApprovalActivity), approval);
                    approvalId = approval.ApprovalId;
                    context.SetCustomStatus(new
                    {
                        phase = "approval_required",
                        turn,
                        toolName = proposal.ToolName,
                        approvalId,
                        expiresAtUtc = approval.ExpiresAtUtc,
                        proposal.ArgumentsJson
                    });

                    bool approved;
                    try
                    {
                        approved = await context.WaitForExternalEvent<bool>(
                            approval.EventName, approval.ExpiresAtUtc - context.CurrentUtcDateTime);
                    }
                    catch (TaskCanceledException)
                    {
                        toolResults.Add(new ToolCallResult(proposal.CallId,
                            """{"error":"approval_timed_out","message":"Approval expired. The action was not executed."}"""));
                        continue;
                    }

                    if (!approved)
                    {
                        toolResults.Add(new ToolCallResult(proposal.CallId,
                            """{"error":"approval_rejected","message":"A human rejected the action. It was not executed."}"""));
                        continue;
                    }
                }

                // STEP 3: Execute read-only or approved tool.
                context.SetCustomStatus(new
                {
                    phase = "processing_tool",
                    turn,
                    toolName = proposal.ToolName
                });

                var toolResult = await context.CallActivityAsync<ToolCallResult>(
                    nameof(ExecuteToolActivity),
                    new ToolExecutionRequest(proposal, context.InstanceId, approvalId));

                if (toolResult is null ||
                    toolResult.CallId != proposal.CallId ||
                    toolResult.Output is null)
                    throw new InvalidOperationException(
                        "The tool activity returned an invalid or mismatched result.");

                toolResults.Add(toolResult);
            }

            turnInput = new ModelTurnInput(request.Message, result.ResponseId, toolResults);
        }

        context.SetCustomStatus(new { phase = "turn_limit_reached", maxTurns = MaxTurns });
        throw new InvalidOperationException($"The agent exceeded its limit of {MaxTurns} model turns.");
    }
}
