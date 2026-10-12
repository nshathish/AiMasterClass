using Microsoft.Azure.Functions.Worker;
using OperationsCopilot.Workflows.Contracts;

namespace OperationsCopilot.Workflows.Activities;

public class CreateApprovalActivity(IApprovalStore approvalStore)
{
    [Function(nameof(CreateApprovalActivity))]
    public async Task RunAsync(
        [ActivityTrigger] ApprovalRequest request,
        CancellationToken cancellationToken)
    {
        await approvalStore.CreateAsync(request, cancellationToken);
    }
}
