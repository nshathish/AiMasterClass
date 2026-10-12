using System.Text.Json;
using OpenAI.Responses;
using OperationsCopilot.Workflows.Tools.Interfaces;

namespace OperationsCopilot.Workflows.Tools;

public class RefundTool : ICopilotTool
{
    public string Name => "refund";

    public ToolRisk Risk => ToolRisk.Write;

    public bool RequiresHuman => true;

    public ResponseTool Definition =>
        ResponseTool.CreateFunctionTool(
            functionName: Name,
            functionDescription: "Submits a refund request for an order with a provided reason.",
            functionParameters: BinaryData.FromObjectAsJson(new
            {
                type = "object",
                properties = new
                {
                    orderId = new { type = "string", description = "The ID of the order to refund." },
                    reason = new { type = "string", description = "The reason for the refund request." }
                },
                required = new[] { "orderId", "reason" },
                additionalProperties = false
            }),
            strictModeEnabled: true
        );

    public Task<string> ExecuteAsync(string args, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(args);
        var orderId = document.RootElement
            .GetProperty("orderId")
            .GetString();
        var reason = document.RootElement
            .GetProperty("reason")
            .GetString();

        if (string.IsNullOrWhiteSpace(orderId))
            return Task.FromResult(
                JsonSerializer.Serialize(new
                {
                    error = "orderId is required and cannot be empty."
                }));

        if (string.IsNullOrWhiteSpace(reason))
            return Task.FromResult(
                JsonSerializer.Serialize(new
                {
                    error = "reason is required and cannot be empty."
                }));

        // Simulated orders match the order-status tool's supported ranges.
        var isEligible = int.TryParse(orderId, out var numericOrderId)
                         && numericOrderId is >= 1000 and <= 1300;
        return Task.FromResult(
            isEligible
                ? JsonSerializer.Serialize(new
                {
                    orderId,
                    reason,
                    status = "refund_requested"
                })
                : JsonSerializer.Serialize(new
                {
                    error = "Order not found"
                }));
    }
}
