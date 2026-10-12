using System.Text.Json;
using OpenAI.Responses;
using OperationsCopilot.Workflows.Tools.Interfaces;

namespace OperationsCopilot.Workflows.Tools;

public class AccountIssueTool : ICopilotTool
{
    public string Name => "account_issue";

    public ToolRisk Risk => ToolRisk.Write;

    public bool RequiresHuman => true;

    public ResponseTool Definition =>
        ResponseTool.CreateFunctionTool(
            functionName: Name,
            functionDescription: "Logs or escalates an account-related issue for a specific account.",
            functionParameters: BinaryData.FromObjectAsJson(new
            {
                type = "object",
                properties = new
                {
                    accountId = new { type = "string", description = "The account identifier related to the issue." },
                    issue = new { type = "string", description = "A short description of the account issue." }
                },
                required = new[] { "accountId", "issue" },
                additionalProperties = false
            }),
            strictModeEnabled: true
        );

    public Task<string> ExecuteAsync(string args, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(args);
        var accountId = document.RootElement
            .GetProperty("accountId")
            .GetString();
        var issue = document.RootElement
            .GetProperty("issue")
            .GetString();

        if (string.IsNullOrWhiteSpace(accountId))
            return Task.FromResult(
                JsonSerializer.Serialize(new
                {
                    error = "accountId is required and cannot be empty."
                }));

        if (string.IsNullOrWhiteSpace(issue))
            return Task.FromResult(
                JsonSerializer.Serialize(new
                {
                    error = "issue is required and cannot be empty."
                }));

        var isKnownAccount = accountId == "acct-1001"; // Simulated account lookup logic
        return Task.FromResult(
            isKnownAccount
                ? JsonSerializer.Serialize(new
                {
                    accountId,
                    status = "issue_logged",
                    ticketId = "TCK-9001"
                })
                : JsonSerializer.Serialize(new
                {
                    error = "Account not found"
                }));
    }
}
