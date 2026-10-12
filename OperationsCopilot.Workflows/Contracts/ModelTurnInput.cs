namespace OperationsCopilot.Workflows.Contracts;

public record ModelTurnInput(
    string Message,
    string? PreviousResponseId = null,
    IReadOnlyList<ToolCallResult>? ToolResults = null);
