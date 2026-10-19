import type { AgentRuntimeStatus, ExecutionMode, InsightCard, WorkflowExecution } from '../api'

/**
 * The guided judge demo: a fixed path through the REAL product (real match state, real insight, real workflow).
 * Each step describes what is on screen and offers the next real action; nothing is simulated.
 */
export const GUIDE_STEPS = ['intro', 'insight', 'why', 'show', 'replay', 'agents', 'trust'] as const
export type GuideStep = (typeof GUIDE_STEPS)[number]

export const nextStep = (s: GuideStep): GuideStep | null => GUIDE_STEPS[GUIDE_STEPS.indexOf(s) + 1] ?? null
export const prevStep = (s: GuideStep): GuideStep | null => GUIDE_STEPS[GUIDE_STEPS.indexOf(s) - 1] ?? null

/**
 * Recommended demo path: flagship synthetic match, Westhaven's switch of attack to the right channel at 63' after a
 * substitution (31% -> 90%, z 3.29, 30 supporting events). Chosen because it has the clearest metric change, before/after
 * replay clips, and verified live Agent Framework results in every Foundry evaluation run.
 */
export const FLAGSHIP = { matchId: 'MS-FLAGSHIP-001', type: 'ATTACK_CHANNEL_SHIFT', minute: 63, seekMinute: 64 } as const

/** The flagship insight in the current snapshot, if the flagship match has reached it. */
export function findFlagship(matchId: string, insights: InsightCard[]): InsightCard | undefined {
  if (matchId !== FLAGSHIP.matchId) return undefined
  return insights.find(i => i.type === FLAGSHIP.type && Math.abs(i.minute - FLAGSHIP.minute) <= 2)
}

/** URL entry point for judges: ?demo=judge (or ?demo=1) starts the guided demo. */
export const wantsGuideFromUrl = (search: string) => ['judge', '1', 'true'].includes(new URLSearchParams(search).get('demo') ?? '')

// ---------------------------------------------------------------- runtime and verification presentation

export interface RuntimeChip { key: string; label: string; tone: 'live' | 'neutral' | 'warn' | 'ok' | 'bad' }

const CREDENTIAL_LABEL: Record<string, string> = {
  ManagedIdentity: 'Managed identity',
  AzureCli: 'Entra ID (Azure CLI)',
  Default: 'Entra ID',
}

/**
 * Status chips built ONLY from runtime metadata and the execution trace. "LIVE", the model and the credential appear only
 * when the execution (or, before one exists, the runtime) is actually LIVE_FOUNDRY.
 */
export function runtimeChips(status: AgentRuntimeStatus | null, x: WorkflowExecution | null): RuntimeChip[] {
  const mode: ExecutionMode | undefined = x?.executionMode ?? status?.runtime.executionMode
  if (!mode) return []
  const chips: RuntimeChip[] = []
  if (mode === 'LIVE_FOUNDRY') chips.push({ key: 'mode', label: 'LIVE · Microsoft Foundry', tone: 'live' })
  else if (mode === 'OFFLINE_TEMPLATE') chips.push({ key: 'mode', label: 'OFFLINE TEMPLATE · no model calls', tone: 'neutral' })
  else chips.push({ key: 'mode', label: 'RECORDED DEMO · not live', tone: 'warn' })
  chips.push({ key: 'af', label: 'Microsoft Agent Framework', tone: 'neutral' })
  if (mode === 'LIVE_FOUNDRY') {
    const model = x?.modelDeployment ?? status?.runtime.modelDeployment
    if (model) chips.push({ key: 'model', label: model, tone: 'neutral' })
    const cred = status?.runtime.credential
    if (cred) chips.push({ key: 'cred', label: CREDENTIAL_LABEL[cred] ?? `Entra ID (${cred})`, tone: 'neutral' })
  }
  const v = verdict(x)
  if (v) chips.push({ key: 'verdict', label: `Verification: ${v.short}`, tone: v.tone })
  return chips
}

export type VerdictKind = 'verified' | 'verifiedWithChanges' | 'rejected' | 'unverified' | 'deterministic' | 'running'

export interface Verdict { kind: VerdictKind; short: string; tone: RuntimeChip['tone'] }

/**
 * The final verification outcome of an execution. "Verified" requires the workflow's own verified flag (verifier
 * approval AND deterministic re-validation) on a LIVE or recorded-live run; template output is never verified.
 */
export function verdict(x: WorkflowExecution | null): Verdict | null {
  if (!x) return null
  if (x.status === 'RUNNING') return { kind: 'running', short: 'running', tone: 'neutral' }
  if (x.executionMode === 'OFFLINE_TEMPLATE' || x.outcome === 'DETERMINISTIC_ONLY')
    return { kind: 'deterministic', short: 'not applicable (no AI output)', tone: 'neutral' }
  if (x.verified && x.outcome === 'APPROVED') return { kind: 'verified', short: 'APPROVED', tone: 'ok' }
  if (x.verified && x.outcome === 'APPROVED_WITH_CHANGES') return { kind: 'verifiedWithChanges', short: 'APPROVED WITH CHANGES', tone: 'ok' }
  if (x.outcome === 'REJECTED') return { kind: 'rejected', short: 'REJECTED', tone: 'bad' }
  return { kind: 'unverified', short: x.outcome ?? x.status, tone: 'warn' }
}

// ---------------------------------------------------------------- Foundry Evaluations proof

/**
 * Results of the latest real Microsoft Foundry cloud evaluation run (docs/evaluations/latest.md). Shown verbatim;
 * update only from a new real run.
 */
export const EVALUATION = {
  runId: 'evalrun_72ca538217034d0bb108a4bc5d2958a9',
  date: '2026-10-19',
  publishedPassing: 28,
  publishedTotal: 28,
  adversarialTotal: 11,
  deterministicCaught: 11,
  verifierCaught: 11,
  foundryGroundednessCaught: 5,
  docsUrl: 'https://github.com/pdidow/MatchShift/blob/main/docs/EVALUATION.md',
} as const
