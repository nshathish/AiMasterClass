using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using OperationsCopilot.Workflows.Contracts;

namespace OperationsCopilot.Workflows.Stores;

public class BlobApprovalStore(BlobContainerClient container) : IApprovalStore
{
    public async Task CreateAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        try
        {
            await GetBlob(request.ApprovalId).UploadAsync(BinaryData.FromObjectAsJson(request),
                new BlobUploadOptions { Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All } },
                cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status is 409 or 412)
        {
            // Activity retries must preserve an existing request and its decision.
            var existing = await GetAsync(request.ApprovalId, cancellationToken);
            if (existing is null || existing.InstanceId != request.InstanceId
                || existing.Proposal != request.Proposal || existing.ExpiresAtUtc != request.ExpiresAtUtc
                || existing.CustomerMessage != request.CustomerMessage || existing.ReviewResult != request.ReviewResult)
                throw new InvalidOperationException("Approval ID is already used by a different request.", exception);
        }
    }

    public async Task<ApprovalRequest?> GetAsync(string approvalId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await GetBlob(approvalId).DownloadContentAsync(cancellationToken);
            return response.Value.Content.ToObjectFromJson<ApprovalRequest>();
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task<bool> TryDecideAsync(string approvalId, bool approved, CancellationToken cancellationToken)
    {
        var blob = GetBlob(approvalId);
        var response = await blob.DownloadContentAsync(cancellationToken);
        var request = response.Value.Content.ToObjectFromJson<ApprovalRequest>()
                      ?? throw new InvalidOperationException("Invalid stored approval request.");
        if (request.Approved.HasValue)
            return request.Approved == approved;
        if (request.ExpiresAtUtc <= DateTime.UtcNow)
            return false;

        try
        {
            await blob.UploadAsync(BinaryData.FromObjectAsJson(request with { Approved = approved }),
                new BlobUploadOptions
                {
                    Conditions = new BlobRequestConditions { IfMatch = response.Value.Details.ETag }
                }, cancellationToken);
            return true;
        }
        catch (RequestFailedException exception) when (exception.Status == 412)
        {
            return (await GetAsync(approvalId, cancellationToken))?.Approved == approved;
        }
    }

    private BlobClient GetBlob(string approvalId)
    {
        if (!Guid.TryParseExact(approvalId, "N", out _))
            throw new ArgumentException("Approval ID must be a GUID in N format.", nameof(approvalId));
        return container.GetBlobClient($"{approvalId}.json");
    }
}
