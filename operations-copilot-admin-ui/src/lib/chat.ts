export type SupportIntent = {
  intent: string;
  priority: string;
  requiresHuman: boolean;
  reason: string;
  reply: string;
};

export type WorkflowStatus = {
  runtimeStatus: string;
  customStatus?: { phase?: string; toolName?: string; approvalId?: string };
  output?: unknown;
};

const baseUrl = (import.meta.env.VITE_WORKFLOW_API_URL ?? '').replace(/\/$/, '');

async function readResponse(response: Response): Promise<unknown> {
  if (!response.ok) {
    const body = await response.json().catch(() => null) as { error?: unknown } | null;
    if (typeof body?.error === 'string') throw new Error(body.error);
    throw new Error(
      response.status === 404
        ? 'The workflow endpoint was not found. Check that the Functions app is running.'
        : `The workflow request failed (${response.status}). Please try again.`,
    );
  }
  return response.json();
}

export type ApprovalTicket = {
  approvalId: string;
  instanceId: string;
  proposal: { toolName: string; argumentsJson: string } | null;
  expiresAtUtc: string;
  approved: boolean | null;
  customerMessage: string | null;
  reviewResult: SupportIntent | null;
};

function parseTicket(value: unknown): ApprovalTicket {
  if (!value || typeof value !== 'object') throw new Error('Invalid approval ticket.');
  const data = value as Record<string, unknown>;
  const approvalId = data.approvalId ?? data.ApprovalId;
  const instanceId = data.instanceId ?? data.InstanceId;
  const expiresAtUtc = data.expiresAtUtc ?? data.ExpiresAtUtc;
  const approved = data.approved ?? data.Approved ?? null;
  const proposal = (data.proposal ?? data.Proposal) as Record<string, unknown> | null;
  const review = data.reviewResult ?? data.ReviewResult;
  if (typeof approvalId !== 'string' || typeof instanceId !== 'string'
    || typeof expiresAtUtc !== 'string' || Number.isNaN(Date.parse(expiresAtUtc))
    || (approved !== null && typeof approved !== 'boolean')) throw new Error('Invalid approval ticket.');
  return {
    approvalId, instanceId, expiresAtUtc, approved,
    customerMessage: (data.customerMessage ?? data.CustomerMessage ?? null) as string | null,
    proposal: proposal ? {
      toolName: String(proposal.toolName ?? proposal.ToolName),
      argumentsJson: String(proposal.argumentsJson ?? proposal.ArgumentsJson),
    } : null,
    reviewResult: review ? parseResult(review) : null,
  };
}

export async function loadApproval(approvalId: string, signal: AbortSignal): Promise<ApprovalTicket> {
  return parseTicket(await readResponse(await fetch(
    `${baseUrl}/api/approvals/${encodeURIComponent(approvalId)}`, { signal },
  )));
}

export async function createReview(statusUrl: string, signal: AbortSignal): Promise<ApprovalTicket> {
  const instanceId = new URL(statusUrl, window.location.origin).pathname.split('/').pop();
  if (!instanceId) throw new Error('Missing workflow ID.');
  return parseTicket(await readResponse(await fetch(
    `${baseUrl}/api/chat/${encodeURIComponent(decodeURIComponent(instanceId))}/review`,
    { method: 'POST', signal },
  )));
}

export async function decideApproval(approvalId: string, approved: boolean, signal: AbortSignal): Promise<void> {
  await readResponse(await fetch(`${baseUrl}/api/approvals/${encodeURIComponent(approvalId)}`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ approved }), signal,
  }));
}

export async function startChat(message: string, signal: AbortSignal): Promise<string> {
  const data = await readResponse(await fetch(`${baseUrl}/api/chat`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ message }),
    signal,
  })) as { statusQueryGetUri?: string; StatusQueryGetUri?: string };

  const statusUrl = data.statusQueryGetUri ?? data.StatusQueryGetUri;
  if (typeof statusUrl !== 'string' || !statusUrl) {
    throw new Error('The workflow did not return a status URL.');
  }

  // Use the same development proxy for the Durable Functions management route.
  const url = new URL(statusUrl, window.location.origin);
  if (!url.pathname.startsWith('/runtime/webhooks/durabletask/')) {
    throw new Error('The workflow returned an unsupported status URL.');
  }
  return `${baseUrl}${url.pathname}${url.search}`;
}

export async function getStatus(url: string, signal: AbortSignal): Promise<WorkflowStatus> {
  const data = await readResponse(await fetch(url, { signal })) as Record<string, unknown>;
  const runtimeStatus = data.runtimeStatus ?? data.RuntimeStatus;
  if (typeof runtimeStatus !== 'string') {
    throw new Error('The workflow returned an invalid status.');
  }
  return {
    runtimeStatus,
    customStatus: (data.customStatus ?? data.CustomStatus) as WorkflowStatus['customStatus'],
    output: data.output ?? data.Output,
  };
}

export function parseResult(output: unknown): SupportIntent {
  const value = typeof output === 'string' ? JSON.parse(output) : output;
  if (!value || typeof value !== 'object') throw new Error('The workflow returned an invalid result.');
  const result = value as Record<string, unknown>;
  const intent = result.intent ?? result.Intent;
  const priority = result.priority ?? result.Priority;
  const requiresHuman = result.requiresHuman ?? result.RequiresHuman;
  const reason = result.reason ?? result.Reason;
  const reply = result.reply ?? result.Reply;
  if (typeof intent !== 'string' || typeof priority !== 'string'
    || typeof requiresHuman !== 'boolean' || typeof reason !== 'string'
    || typeof reply !== 'string' || !reply.trim()) {
    throw new Error('The workflow returned an invalid classification.');
  }
  return { intent, priority, requiresHuman, reason, reply };
}

export function waitForPoll(signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    const abort = () => {
      window.clearTimeout(timer);
      reject(new DOMException('Aborted', 'AbortError'));
    };
    const timer = window.setTimeout(() => {
      signal.removeEventListener('abort', abort);
      resolve();
    }, 1500);
    signal.addEventListener('abort', abort, { once: true });
    if (signal.aborted) abort();
  });
}
