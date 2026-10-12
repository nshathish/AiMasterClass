# Operations Copilot admin UI

React and TypeScript chat interface for the Durable Functions workflow.

The Customer tab contains the chat and workflow progress. The Admin tab contains
action approvals and completed-request review tickets from the current session.
Switching tabs keeps polling active; an admin decision resumes the workflow and
its final reply appears in Customer. Starting a new chat preserves admin tickets.
The tab count shows all session tickets, including tickets already decided.
Reloading the page resets the session list; stored tickets remain in Blob Storage.

## Run locally

Start the Functions app on http://localhost:7177, then run:

```sh
pnpm install
pnpm dev
```

Vite proxies /api and /runtime to the Functions app. The chat starts a workflow
with POST /api/chat and polls its returned status URL. Completed requests show
intent, priority, human review requirements, and the customer reply.

Each message starts an independent workflow; previous messages are displayed
for reference but are not sent as conversation history. Requests awaiting human
approval remain pending until an administrator decides through the approval API.
When a workflow is waiting for approval, the screen loads its action ticket and
offers Approve action and Reject buttons. Decisions resume the waiting workflow.
Completed requests flagged requiresHuman create a persisted human review ticket
through POST /api/chat/{instanceId}/review. Review decisions are recorded without
executing refunds or cancellations. Tickets show the customer request, reason,
proposed reply or tool arguments, deadline, and saved decision. Tickets expire
24 hours after workflow completion or action proposal.

For the action flow, submit "Refund order 1042 because it was ordered by mistake."
The agent checks the order, proposes the refund, and waits for Approve action.
After approval, the simulated tool returns refund_requested and the chat shows
the submitted-refund reply. Rejecting prevents execution. A request to cancel
and refund uses separate action approvals in sequence. The simulated tools do
not contact a payment provider: refunds support orders 1000–1300, and cancellation
supports processing orders 1000–1100.

Approval endpoints require Function authorization when hosted. Configure an
authenticated server proxy to supply the Function key; do not put it in the
frontend bundle or VITE environment variables.

## Configuration

The local proxy target is set in vite.config.ts. For separately hosted services,
set VITE_WORKFLOW_API_URL to the Functions origin before building and configure
that service to allow the UI origin through CORS. Without this variable,
production hosting must forward /api and /runtime to the Functions app.

## Checks

```sh
pnpm build
pnpm lint
```
