import { useEffect, useState } from 'react'
import type { AgentRuntimeStatus, Profile, PublishedInsight, StageStatus, TraceStep, WorkflowExecution } from '../api'
import type { Strings } from '../i18n'
import { AgentRefusal, api } from '../api'
import { EVALUATION, chipLabels, modeLabel, publishedNotice, runtimeChips, verdict } from '../guide/guide'
import { t } from '../i18n'

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

/** Which stages are AI agents (dashed) and which are deterministic code. Titles and roles come from the UI strings. */
const AGENT_STAGES = new Set<string>([STAGES.tactical, STAGES.audience, STAGES.accessibility, STAGES.verifier, STAGES.repair])

const nodeLabel = (s: Strings, stage: string) => {
  const [title, role] = (s.agentsUi.nodes as Record<string, readonly string[]>)[stage] ?? [stage, '']
  return { title, role, agent: AGENT_STAGES.has(stage) }
}

const OK: StageStatus[] = ['COMPLETED']
const NEUTRAL: StageStatus[] = ['TEMPLATE', 'SKIPPED', 'RUNNING']

function statusClass(s: StageStatus) {
  return OK.includes(s) ? 'ok' : NEUTRAL.includes(s) ? 'neutral' : s === 'DEGRADED' || s === 'FALLBACK' ? 'warn' : 'bad'
}

const ms = (v?: number) => (v === undefined ? '…' : v >= 1000 ? `${(v / 1000).toFixed(1)} s` : `${v} ms`)

/** All invocations of a stage (the repair loop can run validation and verification twice). */
const stepsFor = (x: WorkflowExecution | null, stage: string) => x?.steps.filter(s => s.stage === stage) ?? []

function Node({ x, stage, s }: { x: WorkflowExecution | null; stage: string; s: Strings }) {
  const runs = stepsFor(x, stage)
  const step = runs[runs.length - 1]
  const label = nodeLabel(s, stage)
  const af = step?.executionType === 'MICROSOFT_AGENT_FRAMEWORK'
  const status = step?.status
  return (
    <div className={`wf-node ${label.agent ? 'wf-agent' : 'wf-det'} ${af ? 'wf-live' : ''} ${status ? `wf-${statusClass(status)}` : 'wf-pending'}`}
      aria-label={`${label.title}: ${status ?? s.agentsUi.waiting}`}>
      <div className="wf-node-top">
        <span className="wf-node-title">{label.title}{runs.length > 1 && <small> ×{runs.length}</small>}</span>
        {status && <span className={`wf-pill wf-${statusClass(status)}`}>{status}</span>}
      </div>
      <div className="wf-role">{label.role}</div>
      <div className="wf-kind">
        {step ? (af ? `MICROSOFT AGENT FRAMEWORK · ${step.modelDeployment ?? ''}` : s.agentsUi.deterministic) : label.agent ? s.agentsUi.agentWaiting : s.agentsUi.detWaiting}
      </div>
      {step && (
        <div className="wf-meta">
          <span>{ms(step.durationMs)}</span>
          {step.superstep !== undefined && <span>superstep {step.superstep}</span>}
          {step.modelCalls > 0 && <span>{step.modelCalls} {s.agentsUi.calls}</span>}
          {step.retryCount > 0 && <span className="wf-retry">{step.retryCount} {s.agentsUi.retries}</span>}
          {step.evidenceCount > 0 && <span>{step.evidenceCount} {s.agentsUi.evidenceShort}</span>}
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
function Timeline({ x, s }: { x: WorkflowExecution; s: Strings }) {
  const start = Date.parse(x.startedAt)
  const end = Math.max(...x.steps.map(st => Date.parse(st.completedAt ?? new Date().toISOString())), start + 1)
  const span = end - start
  const c = x.concurrency
  return (
    <div className="wf-timeline">
      <p className="sr-only">
        {c ? `${nodeLabel(s, STAGES.audience).title} / ${nodeLabel(s, STAGES.accessibility).title}: ${c.sameSuperstep ? s.agentsUi.sameStep : s.agentsUi.diffStep} · ${c.overlapped ? s.agentsUi.concurrent.replace('{ms}', ms(c.overlapMs)) : s.agentsUi.noOverlap}.` : ''}
      </p>
      {x.steps.map(step => {
        const left = ((Date.parse(step.startedAt) - start) / span) * 100
        const width = Math.max(0.8, ((Date.parse(step.completedAt ?? new Date(end).toISOString()) - Date.parse(step.startedAt)) / span) * 100)
        return (
          <div className="wf-row" key={step.sequence}>
            <span className="wf-row-label">{nodeLabel(s, step.stage).title}</span>
            <span className="wf-track" aria-hidden>
              <i className={step.executionType === 'MICROSOFT_AGENT_FRAMEWORK' ? 'af' : 'det'} style={{ left: `${left}%`, width: `${width}%` }} />
            </span>
            <span className="wf-row-ms">{ms(step.durationMs)}</span>
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
        if (!cancelled) setError(e instanceof AgentRefusal ? e.message : `${s.agentsUi.unavailable}: ${(e as Error).message}`)
      }
    }
    void load()
    return () => { cancelled = true; window.clearTimeout(timer) }
  }, [insightId, profile.language, profile.mode, profile.focusTeamId, profile.focusPlayerId, accessible])

  const u = s.agentsUi
  const mode = x?.executionMode ?? status?.runtime.executionMode
  const p = x?.published
  const c = x?.concurrency
  return (
    <div className="wf">
      <h2 className="panel-title">{u.title}</h2>
      <div className="wf-chips" aria-label={u.status}>
        {runtimeChips(status, x, chipLabels(s)).map(c => <span key={c.key} className={`wf-chip wf-chip-${c.tone}`}>{c.label}</span>)}
      </div>
      {status && (
        <p className="wf-runtime">
          {status.runtime.orchestrator}
          {status.runtime.modelDeployment && <> · {u.model} <b>{status.runtime.modelDeployment}</b></>}
          {status.runtime.projectEndpointHost && <> · {status.runtime.projectEndpointHost}</>}
          {status.runtime.credential && <> · Entra ID ({status.runtime.credential})</>}
          {Object.keys(status.runtime.packages).length > 0 && <> · {Object.entries(status.runtime.packages).filter(([k]) => k.startsWith('Microsoft.Agents')).map(([k, v]) => `${k} ${v}`).join(', ')}</>}
          {status.runtime.configurationNote && <><br />{status.runtime.provider === 'recorded' ? u.staticNote : status.runtime.configurationNote}</>}
        </p>
      )}
      {x?.recordedFrom && <p className="wf-banner">{u.recorded.replace('{from}', x.recordedFrom)}</p>}
      {mode !== 'RECORDED_DEMO' && <label className="wf-toggle">
        <input type="checkbox" checked={accessible} onChange={e => setAccessible(e.target.checked)} />
        {u.toggle}
      </label>}

      {!insightId && <p className="empty">{u.select}</p>}
      {error && <p className="empty">{error}</p>}

      {insightId && !error && (
        <>
          <VerdictBanner x={x} s={s} />
          <div className="wf-legend" aria-hidden>
            <span><i className="det" />{u.legendDet}</span>
            <span><i className="agent" />{u.legendAgent}</span>
            <span><i className="live" />{u.legendLive}</span>
          </div>
          <div className="wf-graph" role="group" aria-label={u.topology}>
            <Node x={x} stage={STAGES.grounding} s={s} />
            <Arrow label="InsightContext → shared workflow state" />
            <Node x={x} stage={STAGES.tactical} s={s} />
            <Arrow label="fan-out" />
            <div className="wf-branches">
              <Node x={x} stage={STAGES.audience} s={s} />
              <Node x={x} stage={STAGES.accessibility} s={s} />
            </div>
            {c && <div className={`wf-concurrency ${c.overlapped ? 'yes' : ''}`}>
              {c.sameSuperstep ? u.sameStep : u.diffStep} · {c.overlapped ? u.concurrent.replace('{ms}', ms(c.overlapMs)) : u.noOverlap}
            </div>}
            <Arrow label="fan-in" />
            <Node x={x} stage={STAGES.fanIn} s={s} />
            <Arrow />
            <Node x={x} stage={STAGES.validation} s={s} />
            <Arrow />
            <Node x={x} stage={STAGES.verifier} s={s} />
            <Arrow />
            <Node x={x} stage={STAGES.final} s={s} />
            {stepsFor(x, STAGES.repair).length > 0 && <><div className="wf-loop">↺ {u.loop}</div><Node x={x} stage={STAGES.repair} s={s} /></>}
            <Arrow />
            <div className={`wf-decision ${x?.outcome ?? 'pending'}`}>
              {x?.status === 'RUNNING' ? u.running : x?.outcome ?? x?.status ?? u.waiting}
              {x && x.status !== 'RUNNING' && <small>{x.verified ? u.verifiedNote : u.notVerifiedNote}</small>}
            </div>
          </div>

          {x && (
            <>
              <div className="wf-facts">
                <span>{x.frameworkSupersteps} supersteps · {x.frameworkExecutorInvocations} {u.executorRuns}</span>
                <span>{x.modelCalls}/{x.modelCallLimit} {u.modelCalls} · {x.inputTokens}+{x.outputTokens} {u.tokens}</span>
                <span>{ms(x.durationMs)}</span>
                <span className={x.authoritativeFactsUnchanged ? 'ok' : ''}>
                  {x.authoritativeFingerprintAfter ? (x.authoritativeFactsUnchanged ? `✓ ${u.factsUnchanged} (${x.authoritativeFingerprintBefore})` : `✗ ${u.factsChanged}`) : u.factsPending}
                </span>
              </div>

              {p && (
                <article className={`card wf-published ${p.verified ? '' : 'unverified'}`}>
                  <div className="card-top"><span className="card-kicker">{p.verified ? `${u.publishedVerified} · ${p.outcome}` : `${u.published}: ${p.outcome}`}</span></div>
                  {p.verified ? (
                    <>
                      <h3 className="card-headline">{p.headline}</h3>
                      <p className="card-fact">{p.summary}</p>
                      <p className="card-sub"><b>{u.inferenceAi}: </b>{p.inference}</p>
                      {p.possibleExplanation && <p className="card-sub"><b>{u.explanationAi}: </b>{p.possibleExplanation}</p>}
                      {p.confidenceQualification && <p className="card-sub"><b>{u.confidence}: </b>{p.confidenceQualification}</p>}
                      {p.spokenSummary && <p className="card-sub"><b>{u.spoken}: </b>{p.spokenSummary}</p>}
                      {p.visualDescription && <p className="card-sub"><b>{u.visual}: </b>{p.visualDescription}</p>}
                      {p.limitations.length > 0 && <p className="card-sub"><b>{u.limitations}: </b>{p.limitations.join(' ')}</p>}
                    </>
                  ) : null}
                  <p className="note">{publishedNotice(s, p.outcome, p.notice, s === t('en'))}</p>
                  {p.evidenceIds.length > 0 && <div className="wf-ids">{p.evidenceIds.map(id => <code key={id}>{id.replace(/^.*-(E\d+)$/, '$1')}</code>)}</div>}
                </article>
              )}

              <details className="wf-trace">
              <summary>{u.trace}</summary>
              <h3 className="wf-h">{u.timeline}</h3>
              <Timeline x={x} s={s} />

              <h3 className="wf-h">{u.handoffs}</h3>
              <ol className="wf-handoffs">
                {x.handoffs.map((h, i) => <li key={i}><code>{h.from}</code> → {h.to.map(t => <code key={t}>{t}</code>)} <small>{h.message}</small></li>)}
              </ol>

              {x.recoveryActions.length > 0 && (
                <>
                  <h3 className="wf-h">{u.recovery}</h3>
                  <ul className="wf-findings">{x.recoveryActions.map((r, i) => <li key={i}>{r}</li>)}</ul>
                </>
              )}
              {x.error && <p className="note">{x.error}</p>}

              <h3 className="wf-h">{u.stages}</h3>
              {x.steps.map(step => <StepDetail key={step.sequence} s={step} />)}
              </details>
              <p className="note">{u.traceNote}</p>
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
    return <p className="agent-note">{s.agentNotPublished}: {agent.outcome}. {publishedNotice(s, agent.outcome, agent.notice, s === t('en'))}</p>
  }
  return (
    <div className="agent-verified">
      <span className="agent-badge">{s.agentVerified} · {agent.outcome} · {modeLabel(s, agent.executionMode)}</span>
      {agent.summary && <p className="card-sub">{agent.summary}</p>}
      {!compact && agent.inference && <p className="card-sub"><b>{s.inference} (AI) </b>{agent.inference}</p>}
      {!compact && agent.possibleExplanation && <p className="card-sub"><b>{s.possibleExplanation} (AI) </b>{agent.possibleExplanation}</p>}
    </div>
  )
}
