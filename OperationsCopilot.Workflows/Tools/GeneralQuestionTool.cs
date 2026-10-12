using System.Text.Json;
using OpenAI.Responses;
using OperationsCopilot.Workflows.Tools.Interfaces;

namespace OperationsCopilot.Workflows.Tools;

public class GeneralQuestionTool : ICopilotTool
{
    public string Name => "general_question";

    public ToolRisk Risk => ToolRisk.ReadOnly;

    public bool RequiresHuman => false;

    public ResponseTool Definition =>
        ResponseTool.CreateFunctionTool(
            functionName: Name,
            functionDescription: "Answers a general customer support question.",
            functionParameters: BinaryData.FromObjectAsJson(new
            {
                type = "object",
                properties = new
                {
                    question = new { type = "string", description = "The customer question to answer." }
                },
                required = new[] { "question" },
                additionalProperties = false
            }),
            strictModeEnabled: true
        );

    public Task<string> ExecuteAsync(string args, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(args);
        var question = document.RootElement
            .GetProperty("question")
            .GetString();

        if (string.IsNullOrWhiteSpace(question))
            return Task.FromResult(
                JsonSerializer.Serialize(new
                {
                    error = "question is required and cannot be empty."
                }));

        return Task.FromResult(
            JsonSerializer.Serialize(new
            {
                question,
                answer = "Thanks for your question. A support specialist will follow up shortly."
            }));
    }
}
