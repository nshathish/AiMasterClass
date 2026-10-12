namespace OperationsCopilot.Workflows.Contracts;

public record ApprovalRequest(
    string ApprovalId,
    string InstanceId,
    ToolCallProposal? Proposal,
    DateTime ExpiresAtUtc,
    bool? Approved = null,
    string? CustomerMessage = null,
    SupportIntent? ReviewResult = null)
{
    public string EventName => $"approval:{ApprovalId}";
}
