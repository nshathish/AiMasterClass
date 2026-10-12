using OpenAI.Responses;

namespace OperationsCopilot.Workflows.Tools.Interfaces;

public interface ICopilotTool
{
    string Name { get; }

    ResponseTool Definition { get; }

    ToolRisk Risk { get; }

    bool RequiresHuman { get; }

    Task<string> ExecuteAsync(string args, CancellationToken cancellationToken = default);
}