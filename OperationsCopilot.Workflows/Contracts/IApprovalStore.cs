namespace OperationsCopilot.Workflows.Contracts;

public interface IApprovalStore
{
    Task CreateAsync(ApprovalRequest request, CancellationToken cancellationToken);
    Task<ApprovalRequest?> GetAsync(string approvalId, CancellationToken cancellationToken);
    Task<bool> TryDecideAsync(string approvalId, bool approved, CancellationToken cancellationToken);
}
