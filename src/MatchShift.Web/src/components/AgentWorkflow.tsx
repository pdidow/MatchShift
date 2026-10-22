import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import type { AgentRuntimeStatus, InsightCard, Profile, PublishedInsight, StageStatus, TraceStep, WorkflowExecution } from '../api'
import type { Strings } from '../i18n'
import { AgentRefusal, api } from '../api'
import { EVALUATION, chipLabels, modeLabel, publishedNotice, runtimeChips, verdict } from '../guide/guide'
import type { VerdictKind } from '../guide/guide'
import { t } from '../i18n'
import type { StepState, Tone } from './agentExplain'
import { AI_STAGES, CHECK_ICON, MODE_BADGE, STAGES, STATE_ICON, VERIFIER_ICON, VERIFIER_TONE, countLabel, deterministicChecks, givenFacts, lastStep, stateTone, stepState, stepsFor, verifierState } from './agentExplain'

// The Agents tab answers "How did MatchShift create this insight, and why should I trust it?" in plain language first
// (current insight, summary, facts vs AI, step-by-step story, verification), then proves it with the real execution trace
// under "Technical details". Every state shown comes from the workflow execution; nothing is assumed.

const OK: StageStatus[] = ['COMPLETED']
const NEUTRAL: StageStatus[] = ['TEMPLATE', 'SKIPPED', 'RUNNING']

function statusClass(s: StageStatus) {
  return OK.includes(s) ? 'ok' : NEUTRAL.includes(s) ? 'neutral' : s === 'DEGRADED' || s === 'FALLBACK' ? 'warn' : 'bad'
}

const ms = (v?: number) => (v === undefined ? '…' : v >= 1000 ? `${(v / 1000).toFixed(1)} s` : `${v} ms`)

/** Technical name of a stage, as in the workflow graph (used in the trace). */
const nodeTitle = (s: Strings, stage: string) => (s.agentsUi.nodes as Record<string, readonly string[]>)[stage]?.[0] ?? stage

const VERDICT_TONE: Record<VerdictKind, Tone> = { verified: 'ok', verifiedWithChanges: 'ok', rejected: 'bad', unverified: 'warn', deterministic: 'neutral', running: 'neutral' }
const VERDICT_ICON: Record<VerdictKind, string> = { verified: '✓', verifiedWithChanges: '✓', rejected: '✗', unverified: '!', deterministic: '○', running: '…' }

/** "Verified", "Approved with changes", … ; an offline run reads "OFFLINE TEMPLATE · Not AI-verified". */
const statusText = (s: Strings, kind: VerdictKind) =>
  kind === 'deterministic' ? `${MODE_BADGE.OFFLINE_TEMPLATE} · ${s.explain.statusWord.deterministic}` : s.explain.statusWord[kind]

/** Bars from each step's real start/end: overlapping bars are concurrent execution. */
function Timeline({ x, s }: { x: WorkflowExecution; s: Strings }) {
  const start = Date.parse(x.startedAt)
  const end = Math.max(...x.steps.map(st => Date.parse(st.completedAt ?? new Date().toISOString())), start + 1)
  const span = end - start
  const c = x.concurrency
  return (
    <div className="wf-timeline">
      <p className="sr-only">
        {c ? `${nodeTitle(s, STAGES.audience)} / ${nodeTitle(s, STAGES.accessibility)}: ${c.sameSuperstep ? s.agentsUi.sameStep : s.agentsUi.diffStep} · ${c.overlapped ? s.agentsUi.concurrent.replace('{ms}', ms(c.overlapMs)) : s.agentsUi.noOverlap}.` : ''}
      </p>
      {x.steps.map(step => {
        const left = ((Date.parse(step.startedAt) - start) / span) * 100
        const width = Math.max(0.8, ((Date.parse(step.completedAt ?? new Date(end).toISOString()) - Date.parse(step.startedAt)) / span) * 100)
        return (
          <div className="wf-row" key={step.sequence}>
            <span className="wf-row-label">{nodeTitle(s, step.stage)}</span>
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
      <summary><b>{s.sequence}. {s.agentName}</b> · <span className={`wf-pill wf-${statusClass(s.status)}`}>{s.status}</span> · {ms(s.durationMs)}{s.superstep !== undefined ? ` · superstep ${s.superstep}` : ''}</summary>
      <div className="agent-io">{s.responsibility}</div>
      <div className="agent-io"><code>stage</code> {s.stage} · <code>type</code> {s.executionType} · {s.executionMode} · {s.provider}{s.modelDeployment ? ` · ${s.modelDeployment}` : ''}</div>
      <div className="agent-io"><code>from</code> {s.handoffFrom.join(', ') || '—'} <code>to</code> {s.handoffTo.join(', ') || '—'}</div>
      <div className="agent-io"><code>in</code> {s.inputSummary}</div>
      <div className="agent-io"><code>out</code> {s.outputSummary}</div>
      {s.modelCalls > 0 && <div className="agent-io"><code>model</code> {s.modelCalls} call(s), {s.retryCount} retry, tokens in {s.inputTokens} / out {s.outputTokens}{s.attemptFailures.length ? ` · failures: ${s.attemptFailures.join(', ')}` : ''}</div>}
      {s.verificationDecision && <div className="agent-io"><code>decision</code> {s.verificationDecision}</div>}
      {s.recoveryAction && <div className="agent-io"><code>recovery</code> {s.recoveryAction}</div>}
      {s.findings.length > 0 && <ul className="wf-findings">{s.findings.map((f, i) => <li key={i}>{f}</li>)}</ul>}
      {s.evidenceIds.length > 0 && <div className="wf-ids">{s.evidenceIds.slice(0, 12).map(id => <code key={id}>{id.replace(/^.*-(E\d+)$/, '$1')}</code>)}</div>}
    </details>
  )
}

/** The final verification outcome, prominent: "Status: Verified / Approved with changes / Rejected", never "verified" for templates. */
export function VerdictBanner({ x, s }: { x: WorkflowExecution | null; s: Strings }) {
  const v = verdict(x)
  if (!v) return null
  const [, detail] = s.verdict[v.kind]
  return (
    <div id="wf-verdict" className={`wf-verdict wf-verdict-${v.tone}`} role="status">
      <span className="wf-verdict-title">
        <span className="wf-verdict-label">{s.explain.status}: </span>
        {v.kind === 'verified' || v.kind === 'verifiedWithChanges' ? '✓ ' : v.kind === 'rejected' ? '✗ ' : ''}{statusText(s, v.kind)}
      </span>
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

// ---------------------------------------------------------------- plain-language sections

/** LIVE MICROSOFT FOUNDRY / OFFLINE TEMPLATE / RECORDED DEMO, from the execution (or, before one exists, the runtime). */
export function ModeBadge({ x, status, s }: { x: WorkflowExecution | null; status: AgentRuntimeStatus | null; s: Strings }) {
  const mode = x?.executionMode ?? status?.runtime.executionMode
  if (!mode) return null
  const e = s.explain
  // Model and credential only for a live run (runtimeChips never returns them otherwise).
  const tech = mode === 'LIVE_FOUNDRY' ? runtimeChips(status, x, chipLabels(s)).filter(c => ['af', 'model', 'cred'].includes(c.key)).map(c => c.label) : []
  const note = mode === 'LIVE_FOUNDRY' ? e.mode.live : mode === 'OFFLINE_TEMPLATE' ? e.mode.offline : e.mode.recorded
  return (
    <div className={`ex-mode ex-mode-${mode}`} role="group" aria-label={e.tech.mode}>
      <span className="ex-mode-badge">{MODE_BADGE[mode]}</span>
      {tech.length > 0 && <span className="ex-mode-tech">{tech.join(' · ')}</span>}
      <span className="ex-mode-note">{note}</span>
    </div>
  )
}

export interface EvidenceActions { onWhy?: () => void; onShow?: () => void; onReplay?: () => void }

export function CurrentInsight({ card, s, actions }: { card: InsightCard; s: Strings; actions: EvidenceActions }) {
  const e = s.explain
  const g = givenFacts(card)
  return (
    <section className="ex-current" aria-labelledby="ex-current-h">
      <h3 id="ex-current-h" className="wf-h">{e.current}</h3>
      <p className="ex-current-line"><span className="ex-minute">{g.minute}'</span> <span className="ex-headline">{card.narrative.headline}</span></p>
      <p className="ex-counts">{s.supportingLegend.replace('{n}', String(g.supporting))}{g.context > 0 && <> · {countLabel(e.contextCount, g.context)}</>}</p>
      {(actions.onWhy || actions.onShow || actions.onReplay) && (
        <div className="evidence-chain" role="group" aria-label={e.inspect}>
          {actions.onWhy && <button className="chip" onClick={actions.onWhy}>{s.why}</button>}
          {actions.onShow && <><span className="chain-sep" aria-hidden>›</span><button className="chip" onClick={actions.onShow}>{s.showMe}</button></>}
          {actions.onReplay && <><span className="chain-sep" aria-hidden>›</span><button className="chip replay-btn" onClick={actions.onReplay}>{s.replay.button}</button></>}
        </div>
      )}
    </section>
  )
}

function StateLabel({ state, s, runs = 1 }: { state: StepState; s: Strings; runs?: number }) {
  return <span className={`ex-state ex-${stateTone(state)}`}><span aria-hidden>{STATE_ICON[state]} </span>{s.explain.state[state]}{runs > 1 ? ` ×${runs}` : ''}</span>
}

/** The final status first, then "How this insight was created": five plain steps, each with its real state. */
function Summary({ x, s }: { x: WorkflowExecution | null; s: Strings }) {
  const e = s.explain
  const title = (stage: string) => e.stages[stage]?.[0] ?? stage
  const branch = () => {
    const a = stepState(lastStep(x, STAGES.audience))
    const b = stepState(lastStep(x, STAGES.accessibility))
    if (a === b) return <StateLabel state={a} s={s} />
    return <>{title(STAGES.audience)}: <StateLabel state={a} s={s} /> · {title(STAGES.accessibility)}: <StateLabel state={b} s={s} /></>
  }
  const rows = [
    <StateLabel key={0} state={stepState(lastStep(x, STAGES.grounding))} s={s} />,
    <StateLabel key={1} state={stepState(lastStep(x, STAGES.tactical))} s={s} />,
    <span key={2}>{branch()}</span>,
    <StateLabel key={3} state={stepState(lastStep(x, STAGES.verifier))} s={s} runs={stepsFor(x, STAGES.verifier).length} />,
    <StateLabel key={4} state={stepState(lastStep(x, STAGES.final))} s={s} runs={stepsFor(x, STAGES.final).length} />,
  ]
  return (
    <section className="ex-summary" aria-labelledby="ex-how-h">
      <VerdictBanner x={x} s={s} />
      <h3 id="ex-how-h" className="wf-h">{e.howTitle}</h3>
      <ol className="ex-how">
        {e.how.map((text, i) => <li key={i}><span className="ex-how-text">{text}</span> <span className="ex-how-state">{rows[i]}</span></li>)}
      </ol>
    </section>
  )
}

/** The grounded facts the agents start from (the same ones WHY? and SHOW ME show), inside the Match data card. */
function GivenFacts({ card, s }: { card: InsightCard; s: Strings }) {
  const e = s.explain
  const g = givenFacts(card)
  return (
    <>
      <p className="st-caption">{e.givenTitle}</p>
      <dl className="ex-given-list">
        {g.metric && <div><dt>{g.metric.label}</dt><dd className="mono">{g.metric.baseline ? `${g.metric.baseline} → ` : ''}{g.metric.recent}</dd></div>}
        <div><dt>{s.evidence}</dt><dd>{s.supportingLegend.replace('{n}', String(g.supporting))}{g.context > 0 && <><br />{countLabel(e.contextCount, g.context)}</>}</dd></div>
        <div><dt>{e.importance}</dt><dd className="mono">{g.importance}</dd></div>
        <div><dt>{s.confidence}</dt><dd>{g.confidence}</dd></div>
      </dl>
      <p className="st-desc">{e.givenNote}</p>
    </>
  )
}

/** Technical line under an AI card ("Microsoft Agent Framework · gpt-4.1-mini · 9.0 s"); the full trace is in Technical details. */
function techLine(step: TraceStep | undefined) {
  if (step?.executionType !== 'MICROSOFT_AGENT_FRAMEWORK') return null
  return ['Microsoft Agent Framework', step.modelDeployment, ms(step.durationMs), step.retryCount > 0 ? `${step.retryCount} retry` : ''].filter(Boolean).join(' · ')
}

function StageCard({ stage, x, s, title: titleOverride, children }: { stage: string; x: WorkflowExecution | null; s: Strings; title?: string; children?: ReactNode }) {
  const e = s.explain
  const [title, question, description] = e.stages[stage] ?? [stage, '', '']
  const ai = AI_STAGES.has(stage)
  const runs = stepsFor(x, stage)
  const st = stepState(runs.at(-1))
  const tech = techLine(runs.at(-1))
  return (
    <div className={`st-card ${ai ? 'st-ai' : 'st-det'} st-tone-${stateTone(st)}`}>
      <div className="st-top">
        <span className="st-tag"><span aria-hidden>{ai ? '◆' : '■'} </span>{ai ? e.tagAi : e.tagDet}</span>
        <StateLabel state={st} s={s} runs={runs.length} />
      </div>
      <h4 className="st-title">{titleOverride ?? title}</h4>
      <p className="st-q">{question}</p>
      <p className="st-desc">{description}</p>
      {children}
      {tech && <p className="st-tech">{tech}</p>}
    </div>
  )
}

/** A handoff between stages. The label is real text (read by screen readers), the arrow is decoration. */
const Link = ({ label }: { label?: string }) => <div className="st-link">{label && <span>{label}</span>}</div>

/** The workflow as a story: match data → Tactical Analyst → two agents in parallel → checks → verification → result. */
function Story({ x, card, s }: { x: WorkflowExecution | null; card?: InsightCard; s: Strings }) {
  const e = s.explain
  const c = x?.concurrency
  const v = verdict(x)
  const repaired = stepsFor(x, STAGES.repair).length > 0
  return (
    <section className="ex-story" aria-labelledby="ex-story-h">
      <h3 id="ex-story-h" className="wf-h">{e.storyTitle}</h3>
      <p className="ex-legend"><span><span aria-hidden>■ </span>{e.factsTitle}</span> <span><span aria-hidden>◆ </span>{e.aiTitle} · Microsoft Agent Framework</span></p>
      <ol className="st-flow">
        <li><StageCard stage={STAGES.grounding} x={x} s={s} title={e.stages.data[0]}>{card && <GivenFacts card={card} s={s} />}</StageCard></li>
        <li><Link label={e.links.grounded} /><StageCard stage={STAGES.tactical} x={x} s={s} /></li>
        <li>
          <Link label={e.links.parallel} />
          <div className="st-parallel" role="group" aria-label={e.parallel}>
            <p className="st-parallel-label"><span aria-hidden>⇉ </span>{e.parallel}</p>
            <ul className="st-branches">
              <li className="st-branch"><StageCard stage={STAGES.audience} x={x} s={s} /></li>
              <li className="st-branch"><StageCard stage={STAGES.accessibility} x={x} s={s} /></li>
            </ul>
            {c && <p className={`st-concurrency ${c.overlapped ? 'yes' : ''}`}>{c.overlapped ? e.parallelYes.replace('{ms}', ms(c.overlapMs)) : e.parallelNo}</p>}
          </div>
        </li>
        <li><Link label={e.links.joined} /><StageCard stage={STAGES.validation} x={x} s={s} /></li>
        <li><Link label={e.links.validated} /><StageCard stage={STAGES.verifier} x={x} s={s} /></li>
        <li><Link label={e.links.verification} /><StageCard stage={STAGES.final} x={x} s={s} /></li>
        {repaired && <li><p className="st-loop"><span aria-hidden>↺ </span>{e.repairLoop}</p><StageCard stage={STAGES.repair} x={x} s={s} /></li>}
        <li>
          <Link label={e.links.result} />
          {x && v && v.kind !== 'running'
            ? <VerificationCard x={x} s={s} />
            : <div className="st-result st-tone-neutral">{v ? <><span aria-hidden>{VERDICT_ICON[v.kind]} </span>{statusText(s, v.kind)}</> : e.state.waiting}</div>}
        </li>
      </ol>
    </section>
  )
}

/**
 * The verification result. Code checks and the Verification Agent are separate rows: the agent is reported as approved
 * only when it actually executed, and the final status says whether it came from a live, recorded or offline run.
 */
export function VerificationCard({ x, s }: { x: WorkflowExecution; s: Strings }) {
  const e = s.explain
  const v = verdict(x)
  if (!v || v.kind === 'running') return null
  const checks = deterministicChecks(x)
  const agent = verifierState(x)
  const changed = v.kind === 'verifiedWithChanges' || (v.kind === 'verified' && checks.some(c => c.state === 'fixed'))
  const note = changed ? e.changesNote : v.kind === 'rejected' ? e.rejectedNote : null
  const provenance = x.executionMode === 'LIVE_FOUNDRY' ? e.provenance.live : x.executionMode === 'RECORDED_DEMO' ? e.provenance.recorded : e.provenance.offline
  return (
    <section className="ex-verify" aria-labelledby="ex-verify-h">
      <h3 id="ex-verify-h" className="wf-h">{e.verifyTitle}</h3>
      <ul className="ex-checks">
        {checks.map(c => (
          <li key={c.key} className={`ex-check ex-check-${c.state}`}>
            <span className="ex-check-icon" aria-hidden>{CHECK_ICON[c.state]}</span>
            <span className="ex-check-text"><span aria-hidden>■ </span>{e.checks[c.key]}</span>
            <span className="ex-check-state">{e.checkState[c.state]}</span>
          </li>
        ))}
        <li className={`ex-check ex-check-agent ex-check-${VERIFIER_TONE[agent]}`}>
          <span className="ex-check-icon" aria-hidden>{VERIFIER_ICON[agent]}</span>
          <span className="ex-check-text"><span aria-hidden>◆ </span>{e.verifierLabel}</span>
          <span className="ex-check-state">{e.verifierState[agent]}</span>
        </li>
      </ul>
      <p className="ex-final">
        <span className="ex-final-label">{e.finalStatus}</span>
        <b className={`ex-final-word ex-${VERDICT_TONE[v.kind]}`}><span aria-hidden>{VERDICT_ICON[v.kind]} </span>{statusText(s, v.kind)}</b>
        <span className="ex-final-from">{provenance}</span>
      </p>
      {note && <p className="note">{note}</p>}
    </section>
  )
}

function Published({ p, s }: { p: PublishedInsight; s: Strings }) {
  const u = s.agentsUi
  return (
    <details className={`card wf-published ${p.verified ? '' : 'unverified'}`}>
      <summary className="card-kicker">{s.explain.publishedTitle} · {p.verified ? u.publishedVerified : `${u.published}: ${p.outcome}`}</summary>
      {p.verified && (
        <>
          <h4 className="card-headline">{p.headline}</h4>
          <p className="card-fact">{p.summary}</p>
          <p className="card-sub"><b>{u.inferenceAi}: </b>{p.inference}</p>
          {p.possibleExplanation && <p className="card-sub"><b>{u.explanationAi}: </b>{p.possibleExplanation}</p>}
          {p.confidenceQualification && <p className="card-sub"><b>{u.confidence}: </b>{p.confidenceQualification}</p>}
          {p.spokenSummary && <p className="card-sub"><b>{u.spoken}: </b>{p.spokenSummary}</p>}
          {p.visualDescription && <p className="card-sub"><b>{u.visual}: </b>{p.visualDescription}</p>}
          {p.limitations.length > 0 && <p className="card-sub"><b>{u.limitations}: </b>{p.limitations.join(' ')}</p>}
        </>
      )}
      <p className="note">{publishedNotice(s, p.outcome, p.notice, s === t('en'))}</p>
      {p.evidenceIds.length > 0 && <div className="wf-ids">{p.evidenceIds.map(id => <code key={id}>{id.replace(/^.*-(E\d+)$/, '$1')}</code>)}</div>}
    </details>
  )
}

function FailureRecovery({ s }: { s: Strings }) {
  const e = s.explain
  return (
    <section className="ex-fail" aria-labelledby="ex-fail-h">
      <h3 id="ex-fail-h" className="wf-h">{e.failTitle}</h3>
      <ul>{e.failItems.map((item, i) => <li key={i}>{item}</li>)}</ul>
    </section>
  )
}

/** Proof, second: identifiers, timings, retries, handoffs and every stage, collapsed by default. */
function TechnicalDetails({ x, status, s, accessible, onAccessible }: {
  x: WorkflowExecution; status: AgentRuntimeStatus | null; s: Strings; accessible: boolean; onAccessible: (v: boolean) => void
}) {
  const u = s.agentsUi
  const e = s.explain
  const c = x.concurrency
  return (
    <details className="wf-trace">
      <summary>{e.techTitle}</summary>
      <dl className="ex-tech">
        <div><dt>{e.tech.executionId}</dt><dd><code>{x.workflowExecutionId}</code></dd></div>
        <div><dt>{e.tech.insightId}</dt><dd><code>{x.insightId}</code></dd></div>
        <div><dt>{e.tech.mode}</dt><dd><code>{x.executionMode}</code> · {modeLabel(s, x.executionMode)}</dd></div>
        <div><dt>{e.tech.provider}</dt><dd>{x.provider}</dd></div>
        {x.modelDeployment && <div><dt>{e.tech.model}</dt><dd>{x.modelDeployment}</dd></div>}
        <div><dt>{e.tech.orchestrator}</dt><dd>{x.orchestrator}</dd></div>
        <div><dt>{e.tech.outcome}</dt><dd><code>{x.outcome ?? x.status}</code>{x.verificationDecision ? <> · {u.verification}: <code>{x.verificationDecision}</code></> : null} · verified={String(x.verified)}</dd></div>
        <div><dt>{e.tech.duration}</dt><dd>{ms(x.durationMs)}</dd></div>
      </dl>
      {status && (
        <p className="wf-runtime">
          {status.runtime.orchestrator}
          {status.runtime.modelDeployment && <> · {u.model} <b>{status.runtime.modelDeployment}</b></>}
          {status.runtime.projectEndpointHost && <> · {status.runtime.projectEndpointHost}</>}
          {status.runtime.credential && <> · Entra ID ({status.runtime.credential})</>}
          {Object.keys(status.runtime.packages).length > 0 && <> · {Object.entries(status.runtime.packages).filter(([k]) => k.startsWith('Microsoft.Agents')).map(([k, v]) => `${k} ${v}`).join(', ')}</>}
          {status.runtime.configurationNote && <><br />{status.runtime.provider === 'recorded' ? e.mode.recorded : status.runtime.configurationNote}</>}
        </p>
      )}
      {x.recordedFrom && <p className="wf-banner">{e.mode.recorded} ({x.recordedFrom})</p>}
      {x.executionMode !== 'RECORDED_DEMO' && <label className="wf-toggle">
        <input type="checkbox" checked={accessible} onChange={ev => onAccessible(ev.target.checked)} />
        {u.toggle}
      </label>}
      <div className="wf-facts">
        <span>{x.frameworkSupersteps} supersteps · {x.frameworkExecutorInvocations} {u.executorRuns}</span>
        <span>{x.modelCalls}/{x.modelCallLimit} {u.modelCalls} · {x.inputTokens}+{x.outputTokens} {u.tokens}</span>
        {c && <span className={c.overlapped ? 'ok' : ''}>{nodeTitle(s, STAGES.audience)} / {nodeTitle(s, STAGES.accessibility)}: {c.sameSuperstep ? u.sameStep : u.diffStep} · {c.overlapped ? u.concurrent.replace('{ms}', ms(c.overlapMs)) : u.noOverlap}</span>}
        <span className={x.authoritativeFactsUnchanged ? 'ok' : ''}>
          {x.authoritativeFingerprintAfter ? (x.authoritativeFactsUnchanged ? `✓ ${u.factsUnchanged} (${x.authoritativeFingerprintBefore})` : `✗ ${u.factsChanged}`) : u.factsPending}
        </span>
      </div>

      <h4 className="wf-h">{u.timeline}</h4>
      <Timeline x={x} s={s} />

      <h4 className="wf-h">{u.handoffs}</h4>
      <ol className="wf-handoffs">
        {x.handoffs.map((h, i) => <li key={i}><code>{h.from}</code> → {h.to.map(to => <code key={to}>{to}</code>)} <small>{h.message}</small></li>)}
      </ol>

      {x.recoveryActions.length > 0 && (
        <>
          <h4 className="wf-h">{u.recovery}</h4>
          <ul className="wf-findings">{x.recoveryActions.map((r, i) => <li key={i}>{r}</li>)}</ul>
        </>
      )}
      {x.error && <p className="note">{x.error}</p>}

      <h4 className="wf-h">{u.stages}</h4>
      {x.steps.map(step => <StepDetail key={step.sequence} s={step} />)}
      <p className="note">{u.traceNote}</p>
    </details>
  )
}

// ---------------------------------------------------------------- the tab

export interface ExplainerProps {
  insightId: string | null
  card?: InsightCard
  x: WorkflowExecution | null
  status: AgentRuntimeStatus | null
  error?: string | null
  s: Strings
  actions?: EvidenceActions
  accessible?: boolean
  onAccessible?: (v: boolean) => void
}

/** The whole Agents tab for given data: understand first (top), prove second (technical details). */
export function AgentsExplainer({ insightId, card, x, status, error, s, actions = {}, accessible = true, onAccessible = () => undefined }: ExplainerProps) {
  const e = s.explain
  const ready = !!insightId && !error
  return (
    <div className="wf">
      <header className="ex-head">
        <h2 className="ex-title">{e.heading}</h2>
        <p className="ex-sub">{e.subheading}</p>
        <p className="ex-principle">{e.principle}</p>
      </header>
      <ModeBadge x={x} status={status} s={s} />

      {!insightId && <p className="empty">{s.agentsUi.select}</p>}
      {insightId && card && <CurrentInsight card={card} s={s} actions={actions} />}
      {insightId && error && <p className="ex-error">{error} {e.unavailable}</p>}
      {ready && <Summary x={x} s={s} />}

      {ready && (
        <>
          <Story x={x} card={card} s={s} />
          {x?.published && <Published p={x.published} s={s} />}
        </>
      )}

      <FailureRecovery s={s} />
      {ready && x && <TechnicalDetails x={x} status={status} s={s} accessible={accessible} onAccessible={onAccessible} />}
      <EvalProof s={s} />
    </div>
  )
}

export function AgentWorkflowPanel({ insightId, card, profile, defaultAccessible, s, onWhy, onShow, onReplay }: {
  insightId: string | null; card?: InsightCard; profile: Profile; defaultAccessible: boolean; s: Strings
} & EvidenceActions) {
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

  return (
    <AgentsExplainer insightId={insightId} card={card} x={x} status={status} error={error} s={s}
      actions={{ onWhy, onShow, onReplay }} accessible={accessible} onAccessible={setAccessible} />
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
