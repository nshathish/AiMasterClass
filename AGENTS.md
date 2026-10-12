# Repository Guidelines

## Project Structure & Module Organization

`MasterClass.Exercises.Day1.slnx` groups two .NET 10 projects:

- `OperationsCopilot/`: ASP.NET Core API for support-ticket classification. `Endpoints/` defines HTTP routes; `Tools/` contains copilot tools and their registry; `Tools/Interfaces/` defines tool contracts. `Program.cs` configures dependency injection.
- `OperationsCopilot.Workflows/`: Azure Functions v4 isolated-worker application. `Clients/` contains HTTP starters, `Orchestrators/` coordinates durable workflows, and `Activities/` contains individual workflow operations.

Configuration lives in each project's settings files and `Properties/`. No dedicated test or asset directories currently exist. Avoid editing generated `bin/`, `obj/`, or `.vs/` files.

## Build, Test, and Development Commands

Install the .NET 10 SDK. Run these commands from the repository root:

- `dotnet restore MasterClass.Exercises.Day1.slnx`: restore dependencies for both projects.
- `dotnet build MasterClass.Exercises.Day1.slnx`: compile the solution.
- `dotnet run --project OperationsCopilot --launch-profile https`: start the API at `https://localhost:7105`; the development Scalar interface is available at `/scalar`.
- `dotnet watch --project OperationsCopilot run --launch-profile https`: run the API with automatic reloads.

For workflows, install Azure Functions Core Tools v4, configure local storage, and run `func start` from `OperationsCopilot.Workflows/`. Use Azurite when local configuration targets emulated storage.

## Coding Style & Naming Conventions

Follow existing C# conventions: four-space indentation, file-scoped namespaces, PascalCase types and methods, and camelCase parameters and locals. Prefix interfaces with `I` and asynchronous methods with `Async`. Keep filenames aligned with their primary type. Nullable reference types and implicit usings are enabled. No repository-specific formatter or lint configuration is present.

Implement new tools through `ICopilotTool` and register them in the API's `Program.cs`. Keep durable orchestrators deterministic; place external calls in activities.

## Testing Guidelines

No automated test framework or coverage threshold is configured. Build both projects before submitting changes. Manually exercise `POST /api/chat/classify-ticket` with representative tickets and verify workflow startup and completion. If adding tests, use a dedicated test project, name files `*Tests.cs`, document its framework, and run it with `dotnet test`.

## Commit & Pull Request Guidelines

Git history is unavailable in this checkout, so no existing commit convention can be verified. Use concise, imperative messages such as `Add refund tool validation`. PRs should describe the change, link relevant issues, and report build and manual verification results. Include request/response examples for API changes.

## Security & Configuration

Supply `OpenAI:ApiKey` through environment configuration, such as `OpenAI__ApiKey`. Keep API keys, storage credentials, and telemetry connection strings out of commits. Treat `local.settings.json` and IDE user settings as machine-specific configuration.
