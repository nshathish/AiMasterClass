using System.Text.Json;
using OpenAI.Responses;
using OperationsCopilot.Workflows.Tools.Interfaces;

namespace OperationsCopilot.Workflows.Tools;

public class OrderStatusTool : ICopilotTool
{
    public string Name => "order_status";

    public ToolRisk Risk => ToolRisk.ReadOnly;

    public bool RequiresHuman => false;

    public ResponseTool Definition =>
        ResponseTool.CreateFunctionTool(
            functionName: Name,
            functionDescription: "Retrieves the current status of an order by order ID.",
            functionParameters: BinaryData.FromObjectAsJson(new
            {
                type = "object",
                properties = new
                {
                    orderId = new { type = "string", description = "The ID of the order to check." }
                },
                required = new[] { "orderId" },
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

        if (string.IsNullOrWhiteSpace(orderId))
            return Task.FromResult(
                JsonSerializer.Serialize(new
                {
                    error = "orderId is required and cannot be empty."
                }));

        // Simulated lookup with inclusive order ID ranges.
        var status = int.TryParse(orderId, out var numericOrderId)
            ? numericOrderId switch
            {
                >= 1000 and <= 1100 => "processing",
                >= 1101 and <= 1200 => "shipped",
                >= 1201 and <= 1300 => "delivered",
                _ => null
            }
            : null;
        return Task.FromResult(
            status is not null
                ? JsonSerializer.Serialize(new
                {
                    orderId,
                    status
                })
                : JsonSerializer.Serialize(new
                {
                    error = "Order not found"
                }));
    }
}
