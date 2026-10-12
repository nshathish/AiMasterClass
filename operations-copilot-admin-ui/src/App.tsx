import { useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { getStatus, parseResult, startChat, waitForPoll } from './lib/chat';
import type { SupportIntent, WorkflowStatus } from './lib/chat';
import ApprovalCard from './components/ApprovalCard';

type Message = {
  id: string;
  role: 'user' | 'assistant';
  text: string;
  result?: SupportIntent;
};

type ReviewEntry = { id: string; approvalId?: string; reviewStatusUrl?: string };

const suggestions = [
  'Where is my order #1042?',
  'I would like a refund for order #1042.',
  'I cannot sign in to my account.',
];

function progressText(status: WorkflowStatus): string {
  const phase = status.customStatus?.phase;
  if (phase === 'approval_required') return 'Waiting for human approval';
  if (phase === 'processing_tool') return 'Checking the requested action';
  if (status.runtimeStatus === 'Suspended') return 'Workflow paused';
  return 'Reviewing your ticket';
}

export default function App() {
  const [messages, setMessages] = useState<Message[]>([]);
  const [draft, setDraft] = useState('');
  const [busy, setBusy] = useState(false);
  const [progress, setProgress] = useState('');
  const [error, setError] = useState('');
  const [tab, setTab] = useState<'customer' | 'admin'>('customer');
  const [tickets, setTickets] = useState<ReviewEntry[]>([]);
  const controller = useRef<AbortController | null>(null);
  const bottom = useRef<HTMLDivElement>(null);
  const input = useRef<HTMLTextAreaElement>(null);

  useEffect(() => () => controller.current?.abort(), []);
  useEffect(() => {
    bottom.current?.scrollIntoView({ behavior: 'smooth', block: 'end' });
  }, [messages, progress, error, tab]);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const message = draft.trim();
    if (!message || controller.current) return;

    const active = new AbortController();
    controller.current = active;
    setBusy(true);
    setError('');
    setProgress('Starting your request');
    setMessages((current) => [...current, { id: crypto.randomUUID(), role: 'user', text: message }]);
    setDraft('');

    try {
      const statusUrl = await startChat(message, active.signal);
      const seenApprovals = new Set<string>();
      while (!active.signal.aborted) {
        const status = await getStatus(statusUrl, active.signal);
        if (status.runtimeStatus === 'Completed') {
          const result = parseResult(status.output);
          setMessages((current) => [...current, {
            id: crypto.randomUUID(), role: 'assistant', text: result.reply, result,
          }]);
          if (result.requiresHuman && seenApprovals.size === 0) {
            setTickets((current) => [...current, { id: statusUrl, reviewStatusUrl: statusUrl }]);
          }
          break;
        }
        if (['Failed', 'Terminated', 'Canceled'].includes(status.runtimeStatus)) {
          throw new Error('This request could not be completed. Please submit the ticket again.');
        }
        setProgress(progressText(status));
        const approvalId = status.customStatus?.approvalId;
        if (status.customStatus?.phase === 'approval_required' && approvalId && !seenApprovals.has(approvalId)) {
          seenApprovals.add(approvalId);
          setTickets((current) => current.some((ticket) => ticket.id === approvalId)
            ? current : [...current, { id: approvalId, approvalId }]);
        }
        await waitForPoll(active.signal);
      }
    } catch (failure) {
      if (!active.signal.aborted) {
        setError(failure instanceof TypeError
          ? 'Unable to connect. Check that the workflow service is running, then try again.'
          : failure instanceof Error ? failure.message : 'Something went wrong. Please try again.');
        setDraft(message);
      }
    } finally {
      if (controller.current === active) {
        controller.current = null;
        setBusy(false);
        setProgress('');
        input.current?.focus();
      }
    }
  }

  function newChat() {
    setMessages([]);
    setDraft('');
    setError('');
    input.current?.focus();
  }

  return (
    <div className="workspace">
      <aside className="sidebar">
        <a className="brand" href="/" aria-label="Operations Copilot home">
          <span className="brand-mark">O<span /></span>
          <span>Operations<span className="brand-subtitle">COPILOT</span></span>
        </a>
        <div className="workspace-label">SUPPORT WORKSPACE</div>
        <nav aria-label="Main navigation">
          <a className="nav-item active" href="/" aria-current="page"><span aria-hidden="true">◈</span> Chat assistant</a>
        </nav>
        <div className="sidebar-note">
          <span className="small-label">A LITTLE HELP, EVERY DAY</span>
          <p>Turn support requests into clear next steps.</p>
        </div>
        <div className="profile"><span className="avatar">{tab === 'admin' ? 'A' : 'C'}</span><div>{tab === 'admin' ? 'Administrator' : 'Customer'}<small>{tab === 'admin' ? 'Operations team' : 'Customer support'}</small></div></div>
      </aside>

      <main className="main">
        <header className="topbar">
          <div><span className="breadcrumb">Workspace /</span> {tab === 'customer' ? 'Customer chat' : 'Admin reviews'}</div>
          <span className="workspace-badge">{tab === 'customer' ? 'Customer' : 'Admin'}</span>
        </header>
        <section className="chat-panel" aria-labelledby="chat-title">
          <div className="chat-heading">
            <div><span className="eyebrow">YOUR OPERATIONS PARTNER</span><h1 id="chat-title">{tab === 'customer' ? 'Chat assistant' : 'Review tickets'}</h1><p>{tab === 'customer' ? 'Ask a question or get help with your order.' : 'Review customer requests and approve proposed actions.'}</p></div>
            {tab === 'customer' && <button className="new-chat" type="button" onClick={newChat} disabled={busy || messages.length === 0}>＋ New chat</button>}
          </div>

          <div className="view-tabs" role="tablist" aria-label="Workspace view">
            {(['customer', 'admin'] as const).map((view) => <button key={view} type="button"
              id={`${view}-tab`} role="tab" aria-selected={tab === view} aria-controls={`${view}-panel`}
              tabIndex={tab === view ? 0 : -1} onClick={() => setTab(view)}
              onKeyDown={(event) => {
                if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
                  event.preventDefault();
                  const next = event.key === 'Home' ? 'customer' : event.key === 'End' ? 'admin'
                    : view === 'customer' ? 'admin' : 'customer';
                  setTab(next);
                  document.getElementById(`${next}-tab`)?.focus();
                }
              }}>
              {view === 'customer' ? 'Customer' : 'Admin'}
              {view === 'admin' && tickets.length > 0 && <span className="ticket-count" aria-label={`${tickets.length} tickets`}>{tickets.length}</span>}
            </button>)}
          </div>

          <div id="customer-panel" role="tabpanel" aria-labelledby="customer-tab" hidden={tab !== 'customer'} tabIndex={0}>
          <div className="conversation">
            {messages.length === 0 ? (
              <div className="welcome">
                <div className="assistant-icon" aria-hidden="true">✦</div>
                <span className="eyebrow">LET’S GET TO WORK</span>
                <h2>How can I help today?</h2>
                <p>Share a customer’s request. I’ll identify the intent,<br className="desktop-break" /> assess its priority, and flag anything that needs a human.</p>
                <div className="suggestions">
                  {suggestions.map((suggestion, index) => (
                    <button key={suggestion} type="button" onClick={() => { setDraft(suggestion); input.current?.focus(); }}>
                      <span className="suggestion-icon" aria-hidden="true">{['↗', '↶', '◎'][index]}</span>
                      <span>{suggestion}</span><span className="suggestion-arrow" aria-hidden="true">↗</span>
                    </button>
                  ))}
                </div>
              </div>
            ) : (
              <div className="messages" role="log" aria-label="Chat messages" aria-live="polite">
                {messages.map((message) => (
                  <article key={message.id} className={`message ${message.role}`}>
                    <span className={`message-avatar ${message.role}`} aria-hidden="true">{message.role === 'user' ? 'C' : '✦'}</span>
                    <div className="message-content"><span className="message-author">{message.role === 'user' ? 'You' : 'Operations Copilot'}</span>
                      <p>{message.text}</p>
                    </div>
                  </article>
                ))}
              </div>
            )}
            {busy && <div className="progress" role="status"><span className="pulse" />{progress}{progress === 'Waiting for human approval' && <small>An administrator must approve the action before it can continue.</small>}</div>}
            {error && <div className="error" role="alert">{error}</div>}
            <div ref={bottom} />
          </div>

          <form className="composer" onSubmit={submit}>
            <label className="sr-only" htmlFor="message">Support ticket or question</label>
            <textarea id="message" ref={input} value={draft} onChange={(event) => setDraft(event.target.value)}
              placeholder="Describe a support ticket or ask a question…" rows={2} maxLength={10000} disabled={busy}
              onKeyDown={(event) => {
                if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) {
                  event.preventDefault();
                  event.currentTarget.form?.requestSubmit();
                }
              }} />
            <div className="composer-footer"><span>Enter to send · Shift + Enter for a new line</span><button type="submit" disabled={busy || !draft.trim()} aria-label="Send message">Send <span aria-hidden="true">↑</span></button></div>
          </form>
          <p className="footnote">Each message is reviewed as a separate request. Actions may require human approval.</p>
          </div>

          <div id="admin-panel" role="tabpanel" aria-labelledby="admin-tab" hidden={tab !== 'admin'} tabIndex={0}>
            <p className="admin-intro">Tickets from this session appear here automatically. Approved actions resume the customer workflow; the reply appears in the Customer tab.</p>
            {tickets.length === 0 ? <div className="admin-empty"><div className="assistant-icon" aria-hidden="true">✓</div><h2>No review tickets yet</h2><p>Customer requests that need human review will appear here.</p></div>
              : <div className="review-list">{tickets.map((ticket) => <ApprovalCard key={ticket.id}
                approvalId={ticket.approvalId} reviewStatusUrl={ticket.reviewStatusUrl} />)}</div>}
          </div>
        </section>
      </main>
    </div>
  );
}
