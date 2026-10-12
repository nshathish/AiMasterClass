using System.Text.Json;
using OpenAI.Responses;
using OperationsCopilot.Workflows.Tools.Interfaces;

namespace OperationsCopilot.Workflows.Tools;

public class CancelOrderTool : ICopilotTool
{
    public string Name => "cancel_order";

    public ToolRisk Risk => ToolRisk.Destructive;

    public bool RequiresHuman => true;

    public ResponseTool Definition =>
        ResponseTool.CreateFunctionTool(
            functionName: Name,
            functionDescription: "Cancels an existing order by order ID.",
            functionParameters: BinaryData.FromObjectAsJson(new
            {
                type = "object",
                properties = new
                {
                    orderId = new { type = "string", description = "The ID of the order to cancel." }
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

        if(string.IsNullOrWhiteSpace(orderId))
            return Task.FromResult(
                JsonSerializer.Serialize(new
                {
                    error = "orderId is required and cannot be empty."
                }));

        // Only processing orders can be canceled in this simulation.
        var isSuccess = int.TryParse(orderId, out var numericOrderId)
                        && numericOrderId is >= 1000 and <= 1100;
        return Task.FromResult(
            isSuccess
                ? JsonSerializer.Serialize(new
                {
                    orderId,
                    status = "canceled"
                })
                : JsonSerializer.Serialize(new
                {
                    error = numericOrderId is >= 1101 and <= 1300
                        ? "Only processing orders can be canceled"
                        : "Order not found"
                }));
    }
}
