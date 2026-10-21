import type { ExecutionMode, InsightCard, StageStatus, TraceStep, WorkflowExecution } from '../api'

/**
 * Plain-language presentation of a real workflow execution for the Agents tab. Everything here is derived from the
 * execution trace and the insight card; nothing is assumed, so a missing or failed stage is shown as such.
 */

/** Executor IDs of the Microsoft Agent Framework graph (MatchShift.Agents/Workflow/Stages). */
export const STAGES = {
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

/** The stages that are AI agents; every other stage is deterministic C#. */
export const AI_STAGES: ReadonlySet<string> = new Set([STAGES.tactical, STAGES.audience, STAGES.accessibility, STAGES.verifier, STAGES.repair])

/** The execution-mode badge. Identifiers, not prose: identical in every language. */
export const MODE_BADGE: Record<ExecutionMode, string> = {
  LIVE_FOUNDRY: 'LIVE MICROSOFT FOUNDRY',
  OFFLINE_TEMPLATE: 'OFFLINE TEMPLATE',
  RECORDED_DEMO: 'RECORDED DEMO',
}

/** All invocations of a stage (the repair loop can run validation and verification twice). */
export const stepsFor = (x: WorkflowExecution | null, stage: string) => x?.steps.filter(s => s.stage === stage) ?? []
export const lastStep = (x: WorkflowExecution | null, stage: string): TraceStep | undefined => stepsFor(x, stage).at(-1)

export type StepState = 'done' | 'template' | 'skipped' | 'offline' | 'fallback' | 'failed' | 'cancelled' | 'running' | 'waiting'
export type Tone = 'ok' | 'neutral' | 'warn' | 'bad'

const STATE_OF: Record<StageStatus, StepState> = {
  COMPLETED: 'done', TEMPLATE: 'template', SKIPPED: 'skipped', DEGRADED: 'fallback', FALLBACK: 'fallback',
  FAILED: 'failed', CANCELLED: 'cancelled', RUNNING: 'running',
}

/** A stage skipped because no model is configured is "not run (OFFLINE TEMPLATE)", never just "skipped". */
export const stepState = (step?: TraceStep): StepState =>
  !step ? 'waiting' : step.status === 'SKIPPED' && step.executionMode === 'OFFLINE_TEMPLATE' ? 'offline' : STATE_OF[step.status]

export const stateTone = (state: StepState): Tone =>
  state === 'done' ? 'ok' : state === 'fallback' ? 'warn' : state === 'failed' || state === 'cancelled' ? 'bad' : 'neutral'

/** A shape for every state, so status never depends on colour alone. */
export const STATE_ICON: Record<StepState, string> = {
  done: '✓', template: '○', skipped: '–', offline: '–', fallback: '!', failed: '✗', cancelled: '✗', running: '…', waiting: '·',
}

// ---------------------------------------------------------------- verification checks

export type CheckKey = 'numbers' | 'evidence' | 'facts'
export type CheckState = 'pass' | 'fixed' | 'fail' | 'notRun'
export interface Check { key: CheckKey; state: CheckState }

export const CHECK_ICON: Record<CheckState, string> = { pass: '✓', fixed: '✎', fail: '✗', notRun: '–' }

// Issue codes written by GroundingValidator (MatchShift.Agents/Workflow/GroundingValidator.cs) at the start of each finding.
const NUMERIC = ['UNSUPPORTED_NUMBER', 'WRONG_SCORE', 'UNSUPPORTED_TIME', 'FUTURE_TIME', 'SAMPLE_OVERCLAIM', 'CONFIDENCE_MISMATCH']
const EVIDENCE = ['UNKNOWN_EVIDENCE_ID', 'FUTURE_EVIDENCE', 'NOT_SUPPORTING_EVIDENCE', 'MISSING_CITATION']
const FORMAT = ['EMPTY_FIELD', 'TOO_LONG']

const codeOf = (finding: string) => finding.split(/[\s:]/, 1)[0]
const category = (finding: string): CheckKey | null => {
  const code = codeOf(finding)
  if (NUMERIC.includes(code)) return 'numbers'
  if (EVIDENCE.includes(code)) return 'evidence'
  if (FORMAT.includes(code)) return null
  return 'facts' // players, teams, intent, certainty, causal wording, and anything unrecognised: never hidden
}

/**
 * The deterministic (code) checks, from the real findings of the grounding check and the final check. They say nothing
 * about the Verification Agent, which is reported separately by verifierState. A problem found in a result that was still
 * verified (removed or corrected, then re-checked by code) is "fixed"; in an unverified result "fail"; a check whose
 * stage never completed is "notRun".
 */
export function deterministicChecks(x: WorkflowExecution | null): Check[] {
  const validation = stepsFor(x, STAGES.validation)
  const final = lastStep(x, STAGES.final)
  const ran = validation.some(s => s.status === 'COMPLETED')
  const findings = [...validation.flatMap(s => s.findings), ...(final?.findings ?? [])]
  const state = (key: CheckKey): CheckState => {
    if (!ran) return 'notRun'
    if (!findings.some(f => category(f) === key)) return 'pass'
    return x?.verified ? 'fixed' : 'fail'
  }
  return (['numbers', 'evidence', 'facts'] as const).map(key => ({ key, state: state(key) }))
}

export type VerifierState = 'approved' | 'approvedWithChanges' | 'rejected' | 'failed' | 'offline' | 'skipped'

/**
 * What the Verification Agent did. "approved" requires that the agent actually executed (COMPLETED with a decision) AND
 * that the workflow verified the result; an OFFLINE_TEMPLATE run is always "offline" (the agent never runs there).
 */
export function verifierState(x: WorkflowExecution | null): VerifierState {
  const v = lastStep(x, STAGES.verifier)
  if (x?.executionMode === 'OFFLINE_TEMPLATE' || v?.executionMode === 'OFFLINE_TEMPLATE') return 'offline'
  if (!v || v.status === 'SKIPPED' || v.status === 'TEMPLATE') return 'skipped'
  if (v.status !== 'COMPLETED' || !v.verificationDecision) return 'failed'
  if (v.verificationDecision === 'APPROVED') return x?.verified ? 'approved' : 'rejected'
  if (v.verificationDecision === 'APPROVED_WITH_CHANGES') return x?.verified ? 'approvedWithChanges' : 'rejected'
  return 'rejected'
}

export const VERIFIER_TONE: Record<VerifierState, Tone> = {
  approved: 'ok', approvedWithChanges: 'warn', rejected: 'bad', failed: 'bad', offline: 'neutral', skipped: 'neutral',
}
export const VERIFIER_ICON: Record<VerifierState, string> = {
  approved: '✓', approvedWithChanges: '✎', rejected: '✗', failed: '✗', offline: '–', skipped: '–',
}

// ---------------------------------------------------------------- what the agents were given

export interface GivenFacts {
  minute: number
  supporting: number
  context: number
  metric?: { label: string; baseline?: string; recent: string }
  importance: number
  confidence: string
}

/** The grounded, structured facts the workflow starts from (the same ones WHY? and SHOW ME show). */
export function givenFacts(card: InsightCard): GivenFacts {
  const primary = card.narrative.evidence.find(e => e.isPrimary) ?? card.narrative.evidence[0]
  return {
    minute: card.minute,
    supporting: card.supportingEventCount,
    context: card.contextEventIds?.length ?? 0,
    metric: primary ? { label: primary.label, baseline: primary.baseline, recent: primary.recent } : undefined,
    importance: card.importance,
    confidence: card.narrative.confidenceLabel,
  }
}

/** "1 context event" / "2 context events" from a [singular, plural] pair. */
export const countLabel = (forms: readonly string[], n: number) => (n === 1 ? forms[0] : forms[1] ?? forms[0]).replace('{c}', String(n))
