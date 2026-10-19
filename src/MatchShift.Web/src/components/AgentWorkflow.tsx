import { useEffect, useState } from 'react'
import type { AgentRuntimeStatus, ExecutionMode, Profile, PublishedInsight, StageStatus, TraceStep, WorkflowExecution } from '../api'
import type { Strings } from '../i18n'
import { AgentRefusal, api } from '../api'
import { EVALUATION, runtimeChips, verdict } from '../guide/guide'

// Node order of the Microsoft Agent Framework workflow graph (executor IDs, as in MatchShift.Agents/Workflow/Stages).
const STAGES = {
  grounding: 'deterministic-grounding',
  tactical: 'tactical-analyst',
  audience: 'audience-narrative',
  accessibility: 'accessibility-narrator',
  fanIn: 'fan-in',
  validation: 'deterministic-validation',
  verifier: 'verification-agent',
  final: 'final-validation',
  repair: 'constrained-repair',
} as const

const NODE_LABELS: Record<string, { title: string; agent: boolean; role: string }> = {
  [STAGES.grounding]: { title: 'Deterministic Match Analytics', agent: false, role: 'C# analytics fix the facts (InsightContext)' },
  [STAGES.tactical]: { title: 'Tactical Analyst', agent: true, role: 'What the evidence might mean' },
  [STAGES.audience]: { title: 'Audience Narrative', agent: true, role: 'Wording for this viewer' },
  [STAGES.accessibility]: { title: 'Accessibility Narrator', agent: true, role: 'Spoken and nonvisual version' },
  [STAGES.fanIn]: { title: 'Fan-in barrier', agent: false, role: 'Waits for both branches' },
  [STAGES.validation]: { title: 'Deterministic Validation', agent: false, role: 'Code checks every number, time, player and event ID' },
  [STAGES.verifier]: { title: 'Verification Agent', agent: true, role: 'Independent review of every claim' },
  [STAGES.final]: { title: 'Final Validation', agent: false, role: 'Code re-checks the verifier corrections' },
  [STAGES.repair]: { title: 'Constrained repair', agent: true, role: 'One rewrite of rejected wording' },
}

export const MODE_LABEL: Record<ExecutionMode, string> = {
  LIVE_FOUNDRY: 'LIVE · Microsoft Foundry',
  OFFLINE_TEMPLATE: 'OFFLINE TEMPLATE · no model calls',
  RECORDED_DEMO: 'RECORDED DEMO · replay, not live',
}

const OK: StageStatus[] = ['COMPLETED']
const NEUTRAL: StageStatus[] = ['TEMPLATE', 'SKIPPED', 'RUNNING']

function statusClass(s: StageStatus) {
  return OK.includes(s) ? 'ok' : NEUTRAL.includes(s) ? 'neutral' : s === 'DEGRADED' || s === 'FALLBACK' ? 'warn' : 'bad'
}

const ms = (v?: number) => (v === undefined ? '…' : v >= 1000 ? `${(v / 1000).toFixed(1)} s` : `${v} ms`)

/** All invocations of a stage (the repair loop can run validation and verification twice). */
const stepsFor = (x: WorkflowExecution | null, stage: string) => x?.steps.filter(s => s.stage === stage) ?? []

function Node({ x, stage }: { x: WorkflowExecution | null; stage: string }) {
  const runs = stepsFor(x, stage)
  const step = runs[runs.length - 1]
  const label = NODE_LABELS[stage]
  const af = step?.executionType === 'MICROSOFT_AGENT_FRAMEWORK'
  const status = step?.status
  return (
    <div className={`wf-node ${label.agent ? 'wf-agent' : 'wf-det'} ${af ? 'wf-live' : ''} ${status ? `wf-${statusClass(status)}` : 'wf-pending'}`}
      aria-label={`${label.title}: ${status ?? 'waiting'}`}>
      <div className="wf-node-top">
        <span className="wf-node-title">{label.title}{runs.length > 1 && <small> ×{runs.length}</small>}</span>
        {status && <span className={`wf-pill wf-${statusClass(status)}`}>{status}</span>}
      </div>
      <div className="wf-role">{label.role}</div>
      <div className="wf-kind">
        {step ? (af ? `MICROSOFT AGENT FRAMEWORK · ${step.modelDeployment ?? ''}` : 'DETERMINISTIC') : label.agent ? 'agent · waiting' : 'deterministic · waiting'}
      </div>
      {step && (
        <div className="wf-meta">
          <span>{ms(step.durationMs)}</span>
          {step.superstep !== undefined && <span>superstep {step.superstep}</span>}
          {step.modelCalls > 0 && <span>{step.modelCalls} call{step.modelCalls > 1 ? 's' : ''}</span>}
          {step.retryCount > 0 && <span className="wf-retry">{step.retryCount} retr{step.retryCount > 1 ? 'ies' : 'y'}</span>}
          {step.evidenceCount > 0 && <span>{step.evidenceCount} evidence</span>}
          {step.verificationDecision && <span className="wf-decision-inline">{step.verificationDecision}</span>}
        </div>
      )}
    </div>
  )
}

function Arrow({ label }: { label?: string }) {
  return <div className="wf-arrow" aria-hidden>{label && <span>{label}</span>}</div>
}

/** Bars from each step's real start/end: overlapping bars are concurrent execution. */
function Timeline({ x }: { x: WorkflowExecution }) {
  const start = Date.parse(x.startedAt)
  const end = Math.max(...x.steps.map(s => Date.parse(s.completedAt ?? new Date().toISOString())), start + 1)
  const span = end - start
  const c = x.concurrency
  return (
    <div className="wf-timeline">
      <p className="sr-only">
        {c ? `Audience and Accessibility branches ${c.overlapped ? `overlapped for ${c.overlapMs} milliseconds` : 'did not overlap'}${c.sameSuperstep ? ' in the same superstep' : ''}.` : ''}
      </p>
      {x.steps.map(s => {
        const left = ((Date.parse(s.startedAt) - start) / span) * 100
        const width = Math.max(0.8, ((Date.parse(s.completedAt ?? new Date(end).toISOString()) - Date.parse(s.startedAt)) / span) * 100)
        return (
          <div className="wf-row" key={s.sequence}>
            <span className="wf-row-label">{NODE_LABELS[s.stage]?.title ?? s.stage}</span>
            <span className="wf-track" aria-hidden>
              <i className={s.executionType === 'MICROSOFT_AGENT_FRAMEWORK' ? 'af' : 'det'} style={{ left: `${left}%`, width: `${width}%` }} />
            </span>
            <span className="wf-row-ms">{ms(s.durationMs)}</span>
          </div>
        )
      })}
    </div>
  )
}

function StepDetail({ s }: { s: TraceStep }) {
  return (
    <details className="wf-detail">
      <summary><b>{s.sequence}. {s.agentName}</b> · {s.status} · {ms(s.durationMs)}{s.superstep !== undefined ? ` · superstep ${s.superstep}` : ''}</summary>
      <div className="agent-io">{s.responsibility}</div>
      <div className="agent-io"><code>type</code> {s.executionType} · {s.executionMode} · {s.provider}{s.modelDeployment ? ` · ${s.modelDeployment}` : ''}</div>
      <div className="agent-io"><code>from</code> {s.handoffFrom.join(', ') || '—'} <code>to</code> {s.handoffTo.join(', ') || '—'}</div>
      <div className="agent-io"><code>in</code> {s.inputSummary}</div>
      <div className="agent-io"><code>out</code> {s.outputSummary}</div>
      {s.modelCalls > 0 && <div className="agent-io"><code>model</code> {s.modelCalls} call(s), {s.retryCount} retry, tokens in {s.inputTokens} / out {s.outputTokens}{s.attemptFailures.length ? ` · failures: ${s.attemptFailures.join(', ')}` : ''}</div>}
      {s.recoveryAction && <div className="agent-io"><code>recovery</code> {s.recoveryAction}</div>}
      {s.findings.length > 0 && <ul className="wf-findings">{s.findings.map((f, i) => <li key={i}>{f}</li>)}</ul>}
      {s.evidenceIds.length > 0 && <div className="wf-ids">{s.evidenceIds.slice(0, 12).map(id => <code key={id}>{id.replace(/^.*-(E\d+)$/, '$1')}</code>)}</div>}
    </details>
  )
}

/** The final verification outcome, prominent: VERIFIED / VERIFIED WITH CHANGES / REJECTED, never "verified" for templates. */
export function VerdictBanner({ x, s }: { x: WorkflowExecution | null; s: Strings }) {
  const v = verdict(x)
  if (!v) return null
  const [title, detail] = s.verdict[v.kind]
  return (
    <div id="wf-verdict" className={`wf-verdict wf-verdict-${v.tone}`} role="status">
      <span className="wf-verdict-title">{v.kind === 'verified' || v.kind === 'verifiedWithChanges' ? '✓ ' : ''}{title}</span>
      <span className="wf-verdict-detail">{detail}</span>
    </div>
  )
}

/** Compact, factual proof from the latest real Microsoft Foundry Evaluations run (see docs/EVALUATION.md). */
export function EvalProof({ s }: { s: Strings }) {
  const e = s.evalProof
  const E = EVALUATION
  return (
    <section id="eval-proof" className="eval-proof" aria-labelledby="eval-proof-title">
      <h3 id="eval-proof-title" className="wf-h">{e.title}</h3>
      <p className="eval-big"><b>{E.publishedPassing}/{E.publishedTotal}</b> {e.published}</p>
      <table className="eval-table">
        <caption>{e.defects}</caption>
        <tbody>
          <tr><th scope="row">{e.deterministic}</th><td>{E.deterministicCaught}/{E.adversarialTotal}</td></tr>
          <tr><th scope="row">{e.verifier}</th><td>{E.verifierCaught}/{E.adversarialTotal}</td></tr>
          <tr><th scope="row">{e.foundry}</th><td>{E.foundryGroundednessCaught}/{E.adversarialTotal}</td></tr>
        </tbody>
      </table>
      <p className="note">{e.note} <a href={E.docsUrl} target="_blank" rel="noreferrer">{e.link}</a> · <code>{E.runId.slice(0, 16)}…</code> · {E.date}</p>
    </section>
  )
}

export function AgentWorkflowPanel({ insightId, profile, defaultAccessible, s }: { insightId: string | null; profile: Profile; defaultAccessible: boolean; s: Strings }) {
  const [status, setStatus] = useState<AgentRuntimeStatus | null>(null)
  const [x, setX] = useState<WorkflowExecution | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [accessible, setAccessible] = useState(defaultAccessible)

  useEffect(() => { api.agentStatus().then(setStatus).catch(() => undefined) }, [])

  useEffect(() => {
    setX(null)
    setError(null)
    if (!insightId) return
    let cancelled = false
    let timer: number | undefined
    // Only the first request may start a workflow; every poll after it is read-only (run=false), so polling can never
    // trigger an additional (billed) execution.
    let first = true
    const load = async () => {
      try {
        const next = await api.agentTrace(insightId, { ...profile, accessible }, first)
        first = false
        if (cancelled) return
        if (next) setX(next)
        if (next?.status === 'RUNNING') timer = window.setTimeout(load, 700)
      } catch (e) {
        if (!cancelled) setError(e instanceof AgentRefusal ? e.message : `Agent workflow unavailable: ${(e as Error).message}`)
      }
    }
    void load()
    return () => { cancelled = true; window.clearTimeout(timer) }
  }, [insightId, profile.language, profile.mode, profile.focusTeamId, profile.focusPlayerId, accessible])

  const mode = x?.executionMode ?? status?.runtime.executionMode
  const p = x?.published
  const c = x?.concurrency
  return (
    <div className="wf">
      <h2 className="panel-title">Agent workflow</h2>
      <div className="wf-chips" aria-label="Execution status">
        {runtimeChips(status, x).map(c => <span key={c.key} className={`wf-chip wf-chip-${c.tone}`}>{c.label}</span>)}
      </div>
      {status && (
        <p className="wf-runtime">
          {status.runtime.orchestrator}
          {status.runtime.modelDeployment && <> · model <b>{status.runtime.modelDeployment}</b></>}
          {status.runtime.projectEndpointHost && <> · {status.runtime.projectEndpointHost}</>}
          {status.runtime.credential && <> · Entra ID ({status.runtime.credential})</>}
          {Object.keys(status.runtime.packages).length > 0 && <> · {Object.entries(status.runtime.packages).filter(([k]) => k.startsWith('Microsoft.Agents')).map(([k, v]) => `${k} ${v}`).join(', ')}</>}
          {status.runtime.configurationNote && <><br />{status.runtime.configurationNote}</>}
        </p>
      )}
      {x?.recordedFrom && <p className="wf-banner">Replay of a recorded trace ({x.recordedFrom}). Nothing is executing live in this static demo.</p>}
      {mode !== 'RECORDED_DEMO' && <label className="wf-toggle">
        <input type="checkbox" checked={accessible} onChange={e => setAccessible(e.target.checked)} />
        Include the Accessibility Narrator branch (when off, the branch is skipped without a model call)
      </label>}

      {!insightId && <p className="empty">Select an insight card to see its agent workflow.</p>}
      {error && <p className="empty">{error}</p>}

      {insightId && !error && (
        <>
          <VerdictBanner x={x} s={s} />
          <div className="wf-legend" aria-hidden>
            <span><i className="det" />Deterministic C#</span>
            <span><i className="agent" />AI agent · Microsoft Agent Framework</span>
            <span><i className="live" />ran live on Microsoft Foundry</span>
          </div>
          <div className="wf-graph" role="group" aria-label="Workflow topology">
            <Node x={x} stage={STAGES.grounding} />
            <Arrow label="InsightContext → shared workflow state" />
            <Node x={x} stage={STAGES.tactical} />
            <Arrow label="fan-out" />
            <div className="wf-branches">
              <Node x={x} stage={STAGES.audience} />
              <Node x={x} stage={STAGES.accessibility} />
            </div>
            {c && <div className={`wf-concurrency ${c.overlapped ? 'yes' : ''}`}>
              {c.sameSuperstep ? 'same Agent Framework superstep' : 'different supersteps'} · {c.overlapped ? `ran concurrently, overlapped ${ms(c.overlapMs)}` : 'no overlap (a branch was skipped or instant)'}
            </div>}
            <Arrow label="fan-in" />
            <Node x={x} stage={STAGES.fanIn} />
            <Arrow />
            <Node x={x} stage={STAGES.validation} />
            <Arrow />
            <Node x={x} stage={STAGES.verifier} />
            <Arrow />
            <Node x={x} stage={STAGES.final} />
            {stepsFor(x, STAGES.repair).length > 0 && <><div className="wf-loop">↺ verifier rejected: one constrained repair, then validation and verification again</div><Node x={x} stage={STAGES.repair} /></>}
            <Arrow />
            <div className={`wf-decision ${x?.outcome ?? 'pending'}`}>
              {x?.status === 'RUNNING' ? 'Running…' : x?.outcome ?? x?.status ?? 'Waiting'}
              {x && x.status !== 'RUNNING' && <small>{x.verified ? 'Verified: verifier approved and code re-validated' : 'Not AI-verified: deterministic insight shown'}</small>}
            </div>
          </div>

          {x && (
            <>
              <div className="wf-facts">
                <span>{x.frameworkSupersteps} supersteps · {x.frameworkExecutorInvocations} executor runs</span>
                <span>{x.modelCalls}/{x.modelCallLimit} model calls · {x.inputTokens}+{x.outputTokens} tokens</span>
                <span>{ms(x.durationMs)}</span>
                <span className={x.authoritativeFactsUnchanged ? 'ok' : ''}>
                  {x.authoritativeFingerprintAfter ? (x.authoritativeFactsUnchanged ? `✓ authoritative facts unchanged (${x.authoritativeFingerprintBefore})` : '✗ facts changed') : 'facts fingerprint pending'}
                </span>
              </div>

              {p && (
                <article className={`card wf-published ${p.verified ? '' : 'unverified'}`}>
                  <div className="card-top"><span className="card-kicker">{p.verified ? `Agent-verified insight · ${p.outcome}` : `Published: ${p.outcome}`}</span></div>
                  {p.verified ? (
                    <>
                      <h3 className="card-headline">{p.headline}</h3>
                      <p className="card-fact">{p.summary}</p>
                      <p className="card-sub"><b>Inference (AI): </b>{p.inference}</p>
                      {p.possibleExplanation && <p className="card-sub"><b>Possible explanation (AI): </b>{p.possibleExplanation}</p>}
                      {p.confidenceQualification && <p className="card-sub"><b>Confidence: </b>{p.confidenceQualification}</p>}
                      {p.spokenSummary && <p className="card-sub"><b>Spoken: </b>{p.spokenSummary}</p>}
                      {p.visualDescription && <p className="card-sub"><b>Visual description: </b>{p.visualDescription}</p>}
                      {p.limitations.length > 0 && <p className="card-sub"><b>Limitations: </b>{p.limitations.join(' ')}</p>}
                    </>
                  ) : null}
                  <p className="note">{p.notice}</p>
                  {p.evidenceIds.length > 0 && <div className="wf-ids">{p.evidenceIds.map(id => <code key={id}>{id.replace(/^.*-(E\d+)$/, '$1')}</code>)}</div>}
                </article>
              )}

              <details className="wf-trace">
              <summary>Detailed trace: timeline, handoffs and every stage</summary>
              <h3 className="wf-h">Execution timeline</h3>
              <Timeline x={x} />

              <h3 className="wf-h">Handoffs</h3>
              <ol className="wf-handoffs">
                {x.handoffs.map((h, i) => <li key={i}><code>{h.from}</code> → {h.to.map(t => <code key={t}>{t}</code>)} <small>{h.message}</small></li>)}
              </ol>

              {x.recoveryActions.length > 0 && (
                <>
                  <h3 className="wf-h">Recovery actions</h3>
                  <ul className="wf-findings">{x.recoveryActions.map((r, i) => <li key={i}>{r}</li>)}</ul>
                </>
              )}
              {x.error && <p className="note">{x.error}</p>}

              <h3 className="wf-h">Stage details</h3>
              {x.steps.map(s => <StepDetail key={s.sequence} s={s} />)}
              </details>
              <p className="note">Traces show summaries, counts and decisions only: no prompts, hidden reasoning or credentials.</p>
            </>
          )}
        </>
      )}
      <EvalProof s={s} />
    </div>
  )
}

/**
 * The agent workflow's published output on a card or in WHY?. AI wording appears only when the workflow verified it
 * (verifier approval plus deterministic re-validation); otherwise only a short note explains why nothing was published.
 */
export function AgentInsight({ agent, s, compact }: { agent?: PublishedInsight; s: Strings; compact?: boolean }) {
  if (!agent) return null
  if (!agent.verified) {
    // OFFLINE_TEMPLATE runs are expected to publish nothing; only flag genuine rejections and fallbacks.
    if (agent.outcome === 'DETERMINISTIC_ONLY') return null
    return <p className="agent-note">{s.agentNotPublished}: {agent.outcome}. {agent.notice}</p>
  }
  return (
    <div className="agent-verified">
      <span className="agent-badge">{s.agentVerified} · {agent.outcome} · {MODE_LABEL[agent.executionMode]}</span>
      {agent.summary && <p className="card-sub">{agent.summary}</p>}
      {!compact && agent.inference && <p className="card-sub"><b>{s.inference} (AI) </b>{agent.inference}</p>}
      {!compact && agent.possibleExplanation && <p className="card-sub"><b>{s.possibleExplanation} (AI) </b>{agent.possibleExplanation}</p>}
    </div>
  )
}
