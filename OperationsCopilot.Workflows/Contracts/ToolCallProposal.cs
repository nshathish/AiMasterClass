namespace OperationsCopilot.Workflows.Contracts;

// Risk and approval requirements are resolved from the trusted tool registry.
public record ToolCallProposal(
    string CallId,
    string ToolName,
    string ArgumentsJson);
