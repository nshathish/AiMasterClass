namespace OperationsCopilot.Workflows.Contracts;

public record ToolExecutionRequest(ToolCallProposal Proposal, string InstanceId, string? ApprovalId = null);
