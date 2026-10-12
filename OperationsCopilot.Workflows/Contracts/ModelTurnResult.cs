namespace OperationsCopilot.Workflows.Contracts;

// A turn contains either a final classification or tool calls awaiting processing.
public record ModelTurnResult(
    SupportIntent? FinalResult,
    IReadOnlyList<ToolCallProposal> ToolCalls,
    string? ResponseId = null);
