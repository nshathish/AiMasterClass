using System.Net;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using OperationsCopilot.Workflows.Contracts;

namespace OperationsCopilot.Workflows.Clients;

public class ApprovalEndpoints(IApprovalStore approvalStore, ILogger<ApprovalEndpoints> logger)
{
    [Function(nameof(CreateReviewTicket))]
    public async Task<HttpResponseData> CreateReviewTicket(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "chat/{instanceId}/review")] HttpRequestData req,
        string instanceId, [DurableClient] DurableTaskClient client, CancellationToken cancellationToken)
    {
        var instance = await client.GetInstanceAsync(instanceId, getInputsAndOutputs: true,
            cancellation: cancellationToken);
        if (instance is null)
            return await RespondAsync(req, HttpStatusCode.NotFound, new { error = "Workflow not found." });
        if (instance.RuntimeStatus != OrchestrationRuntimeStatus.Completed)
            return await RespondAsync(req, HttpStatusCode.Conflict, new { error = "The workflow has not completed." });
        var result = instance.ReadOutputAs<SupportIntent>();
        var request = instance.ReadInputAs<ChatRequest>();
        if (result?.RequiresHuman != true || request is null)
            return await RespondAsync(req, HttpStatusCode.Conflict, new { error = "This request does not require human review." });

        // One review ticket per workflow, including retries after a lost HTTP response.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"review:{instanceId}"));
        var approvalId = new Guid(hash.AsSpan(0, 16)).ToString("N");
        var existing = await approvalStore.GetAsync(approvalId, cancellationToken);
        if (existing is null)
        {
            var ticket = new ApprovalRequest(approvalId, instanceId, null,
                instance.LastUpdatedAt.UtcDateTime.AddHours(24),
                CustomerMessage: request.Message, ReviewResult: result);
            await approvalStore.CreateAsync(ticket, cancellationToken);
            logger.LogInformation("Created human review ticket {ApprovalId} for workflow {InstanceId}.", approvalId, instanceId);
        }
        var stored = await approvalStore.GetAsync(approvalId, cancellationToken)
                     ?? throw new InvalidOperationException("The review ticket could not be loaded after creation.");
        return await RespondAsync(req, HttpStatusCode.OK, stored);
    }

    [Function(nameof(GetApproval))]
    public async Task<HttpResponseData> GetApproval(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "approvals/{approvalId}")] HttpRequestData req,
        string approvalId, 
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(approvalId, "N", out _))
            return await RespondAsync(req, HttpStatusCode.BadRequest, new { error = "Invalid approval ID." });

        var approval = await approvalStore.GetAsync(approvalId, cancellationToken);
        logger.LogDebug("Approval lookup for {ApprovalId}: found = {Found}.", approvalId, approval is not null);
        
        return approval is null
            ? await RespondAsync(req, HttpStatusCode.NotFound, new { error = "Approval not found." })
            : await RespondAsync(req, HttpStatusCode.OK, approval);
    }

    [Function(nameof(DecideApproval))]
    public async Task<HttpResponseData> DecideApproval(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "approvals/{approvalId}")] HttpRequestData req,
        string approvalId, 
        [DurableClient] DurableTaskClient client, 
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(approvalId, "N", out _))
            return await RespondAsync(req, HttpStatusCode.BadRequest, new { error = "Invalid approval ID." });

        ApprovalDecision? decision;
        try
        {
            decision = await JsonSerializer.DeserializeAsync<ApprovalDecision>(req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
        }
        catch (JsonException)
        {
            return await RespondAsync(req, HttpStatusCode.BadRequest, new { error = "Expected JSON with a boolean approved field." });
        }
        if (decision?.Approved is not { } approved)
            return await RespondAsync(req, HttpStatusCode.BadRequest, new { error = "A boolean approved field is required." });

        var approval = await approvalStore.GetAsync(approvalId, cancellationToken);
        if (approval is null)
            return await RespondAsync(req, HttpStatusCode.NotFound, new { error = "Approval not found." });

        if (approval.Proposal is null)
        {
            if (approval.ReviewResult is null)
                return await RespondAsync(req, HttpStatusCode.Conflict, new { error = "Invalid review ticket." });
            if (approval.ExpiresAtUtc <= DateTime.UtcNow
                || !await approvalStore.TryDecideAsync(approvalId, approved, cancellationToken))
                return await RespondAsync(req, HttpStatusCode.Conflict, new { error = "Review expired or a different decision was already recorded." });
            logger.LogInformation("Recorded review decision for {ApprovalId}: approved = {Approved}.", approvalId, approved);
            return await RespondAsync(req, HttpStatusCode.OK, new { approvalId, approved });
        }

        var instance = await client.GetInstanceAsync(approval.InstanceId, getInputsAndOutputs: true,
            cancellation: cancellationToken);
        if (instance?.RuntimeStatus != OrchestrationRuntimeStatus.Running
            || approval.ExpiresAtUtc <= DateTime.UtcNow)
            return await RespondAsync(req, HttpStatusCode.Conflict, new { error = "Approval is expired or the workflow is not running." });

        var status = instance.ReadCustomStatusAs<JsonElement>();
        if (status.ValueKind != JsonValueKind.Object
            || !status.TryGetProperty("approvalId", out var pendingId)
            || pendingId.GetString() != approvalId)
            return await RespondAsync(req, HttpStatusCode.Conflict, new { error = "The workflow is not waiting for this approval." });

        if (!await approvalStore.TryDecideAsync(approvalId, approved, cancellationToken))
            return await RespondAsync(req, HttpStatusCode.Conflict, new { error = "Approval expired or a different decision was already recorded." });

        // Repeating the same decision resends the event if delivery previously failed.
        await client.RaiseEventAsync(approval.InstanceId, approval.EventName, approved, cancellationToken);
        logger.LogInformation(
            "Sent approval decision for {ApprovalId} to orchestration {InstanceId}: approved = {Approved}.",
            approvalId, approval.InstanceId, approved);
        return await RespondAsync(req, HttpStatusCode.Accepted, new { approvalId, approved });
    }

    private static async Task<HttpResponseData> RespondAsync(HttpRequestData req, HttpStatusCode status, object body)
    {
        var response = req.CreateResponse(status);
        await response.WriteAsJsonAsync(body);
        return response;
    }
}
