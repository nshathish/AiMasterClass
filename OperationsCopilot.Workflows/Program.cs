using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker.Builder;
using Azure.Storage.Blobs;
using OperationsCopilot.Workflows.Contracts;
using OperationsCopilot.Workflows.Stores;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using OpenAI.Responses;
using OperationsCopilot.Workflows.Tools;
using OperationsCopilot.Workflows.Tools.Interfaces;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.Configure<JsonSerializerOptions>(options =>
{
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.PropertyNameCaseInsensitive = true;
});

var storageConnString = builder.Configuration["Approvals:StorageConnectionString"]
                        ?? builder.Configuration["AzureWebJobsStorage"]
                        ?? throw new InvalidOperationException("Approval storage is not configured.");
builder.Services.AddSingleton<IApprovalStore>(_ => new BlobApprovalStore(
    new BlobContainerClient(storageConnString, "copilot-approvals")));

var apiKey = builder.Configuration["OPENAI_API_KEY"]
             ?? throw new InvalidOperationException("OpenAI API key is not configured.");
builder.Services.AddSingleton(new ResponsesClient(apiKey));

builder.Services.AddSingleton<ICopilotTool, CancelOrderTool>();
builder.Services.AddSingleton<ICopilotTool, AccountIssueTool>();
builder.Services.AddSingleton<ICopilotTool, GeneralQuestionTool>();
builder.Services.AddSingleton<ICopilotTool, OrderStatusTool>();
builder.Services.AddSingleton<ICopilotTool, RefundTool>();
builder.Services.AddSingleton<IToolRegistry<ICopilotTool>, CopilotToolRegistry>();

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .UseAzureMonitorExporter();
}

builder.Build().Run();
