namespace OperationsCopilot.Workflows.Contracts;

public record SupportIntent(
    string Intent,
    string Priority,
    bool RequiresHuman,
    string Reason,
    string Reply);
