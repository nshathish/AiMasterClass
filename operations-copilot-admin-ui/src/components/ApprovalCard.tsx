import { useEffect, useRef, useState } from 'react';
import { createReview, decideApproval, loadApproval, waitForPoll } from '../lib/chat';
import type { ApprovalTicket } from '../lib/chat';

export default function ApprovalCard({ approvalId, reviewStatusUrl }: {
  approvalId?: string; reviewStatusUrl?: string;
}) {
  const [ticket, setTicket] = useState<ApprovalTicket | null>(null);
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [retry, setRetry] = useState(0);
  const [now, setNow] = useState(Date.now);
  const decisionController = useRef<AbortController | null>(null);

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => { window.clearInterval(timer); decisionController.current?.abort(); };
  }, []);

  useEffect(() => {
    const active = new AbortController();
    async function load() {
      try {
        setError('');
        let loaded = approvalId ? await loadApproval(approvalId, active.signal)
          : await createReview(reviewStatusUrl!, active.signal);
        while (!active.signal.aborted) {
          setTicket(loaded);
          if (loaded.approved !== null || Date.parse(loaded.expiresAtUtc) <= Date.now()) break;
          await waitForPoll(active.signal);
          loaded = await loadApproval(loaded.approvalId, active.signal);
        }
      } catch (failure) {
        if (!active.signal.aborted) setError(failure instanceof Error ? failure.message : 'Unable to load the ticket.');
      }
    }
    void load();
    return () => active.abort();
  }, [approvalId, reviewStatusUrl, retry]);

  async function decide(approved: boolean) {
    if (!ticket || decisionController.current) return;
    const active = new AbortController();
    decisionController.current = active;
    setSubmitting(true);
    setError('');
    try {
      await decideApproval(ticket.approvalId, approved, active.signal);
      if (!active.signal.aborted) setTicket({ ...ticket, approved });
    } catch (failure) {
      if (!active.signal.aborted) setError(failure instanceof Error ? failure.message : 'Unable to submit the decision.');
    } finally {
      decisionController.current = null;
      if (!active.signal.aborted) { setSubmitting(false); setRetry((value) => value + 1); }
    }
  }

  const expired = ticket ? Date.parse(ticket.expiresAtUtc) <= now : false;
  const status = ticket?.approved === true ? 'Approved' : ticket?.approved === false ? 'Rejected'
    : expired ? 'Expired' : 'Awaiting review';
  let details: Record<string, unknown> = {};
  if (ticket?.proposal) {
    try { details = JSON.parse(ticket.proposal.argumentsJson); } catch { details = { details: ticket.proposal.argumentsJson }; }
  }

  return (
    <section className="approval-card" aria-label="Human review ticket">
      <div className="approval-heading"><h3>{ticket?.proposal ? 'Action approval' : 'Human review ticket'}</h3><span>{ticket ? status : 'Loading ticket…'}</span></div>
      {ticket && <>
        <p className="ticket-id">Ticket {ticket.approvalId}</p>
        {ticket.customerMessage && <div><h4>Customer request</h4><p>{ticket.customerMessage}</p></div>}
        {ticket.reviewResult && <>
          <div className="result-tags"><span>{ticket.reviewResult.intent.replaceAll('_', ' ')}</span><span>Priority: {ticket.reviewResult.priority}</span></div>
          <div><h4>Reason for review</h4><p>{ticket.reviewResult.reason}</p></div>
          <div><h4>Proposed customer reply</h4><p>{ticket.reviewResult.reply}</p></div>
        </>}
        {ticket.proposal && <div><h4>Proposed action: {ticket.proposal.toolName.replaceAll('_', ' ')}</h4>
          <dl>{Object.entries(details ?? {}).map(([key, value]) => <div key={key}><dt>{key}</dt><dd>{typeof value === 'object' ? JSON.stringify(value) : String(value)}</dd></div>)}</dl>
        </div>}
        <p className="approval-note">{ticket.proposal ? 'Approval allows the workflow to execute this exact action.' : 'Review this request and record your decision. Refunds and cancellations must be processed separately; this decision does not execute an action.'}</p>
        <p className="approval-deadline">Deadline: {new Date(ticket.expiresAtUtc).toLocaleString()}</p>
        {ticket.approved === null && !expired && <div className="approval-actions">
          <button type="button" disabled={submitting} onClick={() => void decide(false)}>Reject</button>
          <button className="approve" type="button" disabled={submitting} onClick={() => void decide(true)}>{submitting ? 'Submitting…' : ticket.proposal ? 'Approve action' : 'Approve review'}</button>
        </div>}
        {ticket.approved !== null && <p role="status" className="approval-note">{ticket.proposal
          ? ticket.approved
            ? 'Approval sent. The workflow will execute the action and show its result in the chat.'
            : 'Rejection sent. The action will not be executed; the workflow will return a reply.'
          : `Decision recorded: ${status.toLowerCase()}.`}</p>}
      </>}
      {error && <div className="error" role="alert">{error}<button type="button" className="retry-ticket" onClick={() => setRetry((value) => value + 1)}>Reload ticket</button></div>}
    </section>
  );
}
