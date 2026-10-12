using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using System.Text.Json;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using OperationsCopilot.Workflows.Contracts;
using OperationsCopilot.Workflows.Orchestrators;

namespace OperationsCopilot.Workflows.Clients;

public class ChatEndpoints(ILogger<ChatEndpoints> logger)
{
    [Function(nameof(StartChat))]
    public async Task<HttpResponseData> StartChat(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "chat")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        CancellationToken cancellationToken)
    {
        ChatRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<ChatRequest>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken);
        }
        catch (JsonException)
        {
            return await RespondAsync(req, HttpStatusCode.BadRequest,
                new { error = "The request body must be valid JSON containing a message." });
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Message))
            return await RespondAsync(req, HttpStatusCode.BadRequest,
                new { error = "A non-empty message is required." });

        var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(ChatOrchestrator),
            request,
            cancellation: cancellationToken);

        logger.LogInformation("Started chat orchestration {InstanceId}.", instanceId);

        // Returns an HTTP 202 response with an instance management payload.
        // See https://learn.microsoft.com/azure/azure-functions/durable/durable-functions-http-api#start-orchestration
        return await client.CreateCheckStatusResponseAsync(req, instanceId, cancellation: cancellationToken);
    }

    private static async Task<HttpResponseData> RespondAsync(HttpRequestData req, HttpStatusCode status, object body)
    {
        var response = req.CreateResponse(status);
        await response.WriteAsJsonAsync(body);
        return response;
    }
}
