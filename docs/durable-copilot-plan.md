# Durable Functions Copilot Plan

## Architecture and Scope

Host the copilot entirely in `OperationsCopilot.Workflows/`. This Functions application owns HTTP entry points, model integration, tool registration, execution, and human approval. The API project remains a reference implementation during development; it is not part of the target request flow. No shared library or separate web API is required.

Start with one ticket per orchestration, sequential tool calls, and one authorized reviewer per proposed action. Continue using simulated business tools until approval and recovery behavior are verified.

## What Is Implemented

| Component | Current behavior |
| --- | --- |
| `Clients/HttpStarter.cs` | Accepts `POST /api/chat`, validates JSON and a non-empty message, schedules `ChatOrchestrator`, and returns the built-in HTTP 202 management response. |
| `Orchestrators/ChatOrchestrator.cs` | Coordinates up to ten model turns, processes tool calls sequentially, checks result correlation, and returns `SupportIntent`. |
| `Activities/GenerateAgentTurnActivity.cs` | Makes one `gpt-5-mini` Responses API call with tool definitions and strict classification output; returns proposals or a validated classification. |
| `Activities/ExecuteToolActivity.cs` | Executes only registered `ReadOnly` tools with `RequiresHuman == false`. Other calls return `approval_required` without execution. |
| `Contracts/` | Contains `ChatRequest`, `SupportIntent`, `ModelTurnInput`, `ModelTurnResult`, `ToolCallProposal`, and `ToolCallResult`. |
| `Tools/` and `Program.cs` | Provide tool risk metadata, interfaces, registry, dependency injection, and the `ResponsesClient`. |

The solution has passed build checks. Local end-to-end execution and approval handling remain to be implemented or verified. The greeting orchestrator/activity are sample code and may be removed after the chat flow is validated.

## Project Organization

```text
OperationsCopilot.Workflows/
  Clients/         Chat starter and approval-decision HTTP trigger
  Orchestrators/   Agent loop and durable approval waits
  Activities/      Model calls, proposal preparation, tool execution, audit operations
  Contracts/       Serializable workflow and approval DTOs
  Tools/           Tool implementations, registry, risk enum, interfaces
  Services/        Approval policy, persistent records, execution recovery
  Program.cs       Dependency registrations and configuration
```

The orchestrator performs deterministic coordination. SDK calls, business operations, storage access, and notifications belong in activities or HTTP handlers.

## HTTP and Polling Flow

Submit a ticket:

```http
POST /api/chat
Content-Type: application/json

{"message":"What is the status of order 12345?"}
```

`CreateCheckStatusResponseAsync` returns the instance ID and management URLs. Poll `statusQueryGetUri` for `runtimeStatus`, `customStatus`, and final `output`; no custom chat-status endpoint is planned. The current custom phases include `generating`, `processing_tool`, `completed`, and `turn_limit_reached`. Add `awaiting_approval` with a safe review summary and approval ID.

Retain one additional application endpoint:

```text
POST /api/chat/{instanceId}/approvals/{approvalId}/decision
```

This endpoint authenticates and authorizes the reviewer, records the decision, and raises the orchestration event. A raw `sendEventPostUri` must not be treated as evidence of human authorization. Management URLs carry access capabilities; restrict their distribution and protect host access. Verify the chosen hosting authentication covers built-in management routes. If the target client cannot safely receive those URLs, revisit the polling design before production.

See the [Durable HTTP API documentation](https://learn.microsoft.com/en-us/azure/durable-task/durable-functions/durable-functions-http-api).

## Mandatory Approval Policy

Permit automatic execution only when the trusted registry reports:

```csharp
tool.Risk == ToolRisk.ReadOnly && !tool.RequiresHuman
```

All other risk values require review or rejection. In particular, every `Write` or `Destructive` call requires human approval even when `RequiresHuman` is false. Model-generated classification fields are informational and cannot grant execution permission.

Approval binds to one immutable tool call: workflow instance, call ID, tool name/version, canonical validated arguments, and relevant business resource version. Show the reviewer the target, operation, parameters, and expected effect. For real refunds, resolve and show amount/currency before approval. Changed arguments or changed business preconditions require a fresh proposal.

## Remaining Implementation Steps

### 1. Verify the Current Read-Only Flow

Run the Functions host with its configured storage and submit order-status and general-question tickets. Confirm that polling shows progress and final classification. Confirm refunds, cancellation, and account writes remain blocked. Exercise malformed JSON and missing messages; these should return HTTP 400 without starting a workflow.

Continuation currently uses `ModelTurnResult.ResponseId` and `ModelTurnInput.PreviousResponseId`, with stored OpenAI responses preserving earlier tool and reasoning context. Return every requested call's result under its original call ID. Approval deadlines must fit the availability of stored response context; if it expires, stop safely or explicitly rebuild context rather than repeating an action. Revisit storage-based continuation if response storage is unavailable under the deployment's data policy.

### 2. Prepare Concrete Actions for Review

Add `PrepareToolCallActivity` to resolve tool metadata, validate the full argument schema and business inputs, and return an execution proposal. Current execution checks JSON object shape and tool-specific checks; add complete validation before review.

Add approval/execution contracts with identifiers, proposal hash, timestamps, expiry, decision, and audit correlation. Extend the workflow input with trusted requester/tenant identity when authentication is introduced. Do not accept identity or risk values supplied by the model or request body as authoritative.

### 3. Persist Approval Requests and Wait

Add an approval-record service and creation activity. Use persistent records with atomic updates; choose the backing store during implementation. Records must survive host restarts and concurrent decisions.

For a gated proposal, persist a pending approval and set `awaiting_approval` status. Wait on `WaitForExternalEvent<ApprovalDecision>` using an event name scoped to the approval ID. Race the event against a durable timer using `context.CurrentUtcDateTime`; start with a configurable 24-hour deadline. Cancel the timer after an accepted decision.

On rejection or expiry, persist the outcome and supply a refusal/timeout `ToolCallResult` to the next model turn. Do not execute the action. Keep sequential execution and the ten-turn limit; define a total workflow deadline as well.

### 4. Add the Authenticated Decision Endpoint

Accept approve/reject and an optional comment. Resolve reviewer identity from validated authentication claims. Verify ownership/tenant boundaries, reviewer permission, pending state, expiry, and proposal identity. Atomically accept only the first valid decision.

Persist the decision and event-delivery work together, then deliver through `DurableTaskClient.RaiseEventAsync`. Retry delivery after outages so a recorded decision cannot strand the workflow. Treat duplicate events as harmless and ignore late decisions for expired actions. Link any notification to the review experience; visiting a link must never approve an action.

### 5. Enable Approved Execution

Extend `ExecuteToolActivity` input to carry proposal and approval identifiers. Re-resolve trusted metadata and independently verify the stored approval before mutation; an event payload or a boolean alone is insufficient. Keep the current blocking behavior until verification exists.

Use a stable execution key for each workflow/tool call, atomic execution records, and downstream idempotency support. Recheck resource versions and business preconditions. Recover uncertain outcomes by querying the downstream operation before retrying; a crash after success but before saving the result can otherwise duplicate a refund. Additional or changed actions need new approval.

### 6. Add Review, Audit, and Operational Support

Provide a minimal review client that displays the safe pending proposal from polling and submits the authenticated decision. Audit proposal, reviewer, decision, execution attempt, and outcome. Exclude secrets from workflow DTOs and redact sensitive logs. Set retention for Durable history and approval records.

Version orchestration changes while approvals are outstanding. Retry transient model/read failures with bounded policies; retry writes only under an established idempotency contract. Once the Functions flow is verified, remove the API project from the active solution if desired; existing API code can currently bypass the Functions approval boundary if run separately.

## Verification and Acceptance

Build with `dotnet build MasterClass.Exercises.Day1.slnx`. Run `func start` from `OperationsCopilot.Workflows/` with Azure Functions Core Tools v4 and the configured local storage emulator. Set `OPENAI_API_KEY` as a flat string in `local.settings.json` under `Values`, and as an application setting when hosted.

Add dedicated tests for policy, approval correlation, atomic decision handling, and execution recovery. Verify:

- Read-only calls complete without approval; writes and destructive calls never do.
- Rejected, expired, malformed, unknown, and unauthorized requests cause no mutation.
- Changed arguments invalidate approval; duplicate decisions do not authorize another execution.
- Each model call receives correctly correlated results, including blocked or rejected calls.
- Restarting the host during review preserves the pending action and deadline.
- A persisted decision is eventually delivered after an outage.
- Retries and uncertain execution outcomes do not duplicate business effects.
- Polling exposes pending review and final structured output; the ten-turn limit prevents further tool execution on the final turn.

## Next Concrete Task

Verify the current read-only path locally, then implement proposal preparation and approval contracts. Build the persistent approval lifecycle before enabling any write or destructive tool.
