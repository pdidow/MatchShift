import type { ReplaySequence } from './replay/types'
import { createDemoClient } from './demo/client'
import { AgentRefusal } from './agentRefusal'

export const isStaticDemo = import.meta.env.MODE === 'pages'

// Typed client for the MatchShift API. All data served is synthetic.

export type ViewerMode = 'TeachMe' | 'WhatMatters' | 'Analyst' | 'MyTeam' | 'Studio'
export type RecapStyle = 'Casual' | 'Analyst' | 'Broadcast' | 'Social'
export type { Lang } from './languages'
import type { Lang } from './languages'

export interface Profile {
  mode: ViewerMode
  language: Lang
  focusTeamId?: string | null
  focusPlayerId?: string | null
  /** Request accessible (spoken / nonvisual) agent output for this viewer. Not persisted server-side. */
  accessible?: boolean
}

export interface Team {
  id: string
  name: string
  shortName: string
  code: string
  primaryColor: string
  secondaryColor: string
  formation: string
}

export interface PitchEvent {
  id: string
  seq: number
  second: number
  clock: string
  type: string
  outcome: string
  teamId: string
  playerId?: string
  playerName?: string
  x?: number
  y?: number
  endX?: number
  endY?: number
  qualifier?: string
}

export interface EvidenceLine {
  metric: string
  label: string
  baseline?: string
  recent: string
  change?: string
  threshold?: string
  baselineWindow?: string
  recentWindow?: string
  sampleSize: number
  minimumSample?: number
  sampleRequirementMet: boolean
  isPrimary: boolean
  comparison?: string
}

export interface Narrative {
  insightId: string
  language: string
  mode: ViewerMode
  typeLabel: string
  headline: string
  fact: string
  inference: string
  explanation?: string
  teaching?: string
  ticker: string
  studioLine: string
  perspective?: string
  playerLine?: string
  confidenceLabel: string
  severityLabel: string
  evidence: EvidenceLine[]
  source: string
  verificationNote?: string
}

export type VisualStyle = 'Routes' | 'Points' | 'Shots' | 'Channels'

export interface VisualLayer {
  key: string
  style: VisualStyle
  eventIds: string[]
  emphasised: boolean
}

export interface InsightCard {
  id: string
  type: string
  minute: number
  second: number
  teamId?: string
  severity: 'Low' | 'Medium' | 'High'
  confidence: 'Low' | 'Medium' | 'High'
  confidenceScore: number
  importance: number
  keyPlayerId?: string
  span: { fromSecond: number; toSecond: number }
  visuals: VisualLayer[]
  supportingEventCount: number
  narrative: Narrative
  /** Outcome of the agent workflow for this insight, once one has finished. AI text only when agent.verified. */
  agent?: PublishedInsight
  /** Events drawn only as context by SHOW ME; not supporting evidence. */
  contextEventIds?: string[]
}

export interface HighlightCard {
  id: string
  kind: string
  minute: number
  second: number
  teamId?: string
  importance: number
  insightId?: string
  headline: string
  whyItMatters: string
  relevantEventCount: number
}

export interface TimelinePoint {
  minute: number
  homeControl: number
  homePressure: number
  awayPressure: number
  chaos: number
  chaosState: ChaosState
  homeGoals: number
  awayGoals: number
}

export type ChaosState = 'CONTROLLED' | 'BALANCED' | 'CHAOTIC'

export interface TeamLiveMetrics {
  teamId: string
  possession: number
  pressure: number
  shots: number
  shotsOnTarget: number
  chanceQuality: number
  attacks: number
  leftShare: number
  centreShare: number
  rightShare: number
  turnovers: number
  highRecoveries: number
  passes: number
  completedPasses: number
  progressivePasses: number
  longBalls: number
}

export interface IndexComponent {
  key: string
  label: string
  rawValue: number
  normalized: number
  weight: number
  contribution: number
}

export interface PlayerPerformance {
  playerId: string; passes: number; completed: number; progressive: number; shots: number; recoveries: number; turnovers: number
  events: { id: string; matchSecond: number; type: string; outcome: string; playerId: string }[]
  passIds: string[]; completedIds: string[]; progressiveIds: string[]; shotIds: string[]; recoveryIds: string[]; turnoverIds: string[]
}
export interface Snapshot {
  overlay?: { insightId: string; text: string; source: string; language: string; validFromSecond: number; validUntilSecond: number; evidenceReferences: string[] }
  sessionId: string
  epoch: number
  focusedPlayer?: PlayerPerformance
  matchId: string
  isPlaying: boolean
  speed: number
  finished: boolean
  clock: { second: number; minute: number; period: number; label: string }
  score: { home: number; away: number }
  home: Team
  away: Team
  recentEvents: PitchEvent[]
  metrics: {
    window: string
    home: TeamLiveMetrics
    away: TeamLiveMetrics
    homeControl: number
    chaos: number
    chaosState: ChaosState
    controlComponents: IndexComponent[]
    chaosComponents: IndexComponent[]
  }
  timeline: TimelinePoint[]
  insights: InsightCard[]
  highlights: HighlightCard[]
  profile: Profile
}

export interface Meta {
  languages: { code: Lang; name: string }[]
  modes: ViewerMode[]
  recapStyles: RecapStyle[]
  speeds: number[]
  aiProvider: string
  aiAvailable: boolean
  dataNotice: string
  agentWorkflow?: { executionMode: ExecutionMode; provider: string; modelDeployment?: string; configurationNote?: string }
}

export interface MatchSummary {
  id: string
  scenario: string
  seed: number
  home: Team
  away: Team
  finalScore: { home: number; away: number }
  eventCount: number
}

export interface PlayerInfo {
  id: string
  name: string
  shortName: string
  shirtNumber: number
  position: string
  teamId: string
  isStarter: boolean
}

export interface MatchDetail {
  summary: MatchSummary
  venue: string
  competition: string
  homeSquad: PlayerInfo[]
  awaySquad: PlayerInfo[]
}

export interface InsightEvents {
  insightId: string
  layers: VisualLayer[]
  /** Every drawn event: supporting evidence plus any context-only events. */
  events: PitchEvent[]
  /** The authoritative evidence (equals the card's supportingEventCount). */
  supportingEventIds?: string[]
  /** Drawn grey for comparison; never counted as supporting evidence. */
  contextEventIds?: string[]
}

export interface RecapResponse {
  narrative: {
    title: string
    style: RecapStyle
    language: string
    sections: { heading: string; paragraphs: string[] }[]
    source: string
    verificationNote?: string
  }
}

// ---- Microsoft Agent Framework insight workflow (see docs/AGENTS.md) ----

export type ExecutionMode = 'LIVE_FOUNDRY' | 'OFFLINE_TEMPLATE' | 'RECORDED_DEMO'
export type ExecutionType = 'DETERMINISTIC' | 'MICROSOFT_AGENT_FRAMEWORK'
export type StageStatus = 'RUNNING' | 'COMPLETED' | 'TEMPLATE' | 'SKIPPED' | 'DEGRADED' | 'FALLBACK' | 'FAILED' | 'CANCELLED'
export type WorkflowOutcome = 'APPROVED' | 'APPROVED_WITH_CHANGES' | 'REJECTED' | 'FALLBACK_UNVERIFIED' | 'DETERMINISTIC_ONLY' | 'CANCELLED'

export interface TraceStep {
  sequence: number
  stage: string
  agentName: string
  responsibility: string
  executionType: ExecutionType
  executionMode: ExecutionMode
  provider: string
  modelDeployment?: string
  startedAt: string
  completedAt?: string
  durationMs?: number
  superstep?: number
  status: StageStatus
  handoffFrom: string[]
  handoffTo: string[]
  evidenceIds: string[]
  evidenceCount: number
  inputSummary: string
  outputSummary: string
  modelCalls: number
  retryCount: number
  attemptFailures: string[]
  recoveryAction?: string
  verificationDecision?: string
  inputTokens: number
  outputTokens: number
  findings: string[]
}

/** The viewer-facing result. AI wording is present only when verified is true. */
export interface PublishedInsight {
  workflowExecutionId: string
  insightId: string
  executionMode: ExecutionMode
  outcome: WorkflowOutcome
  verified: boolean
  source: string
  headline?: string
  summary?: string
  inference?: string
  possibleExplanation?: string
  tacticalSignificance?: string
  confidenceQualification?: string
  limitations: string[]
  spokenSummary?: string
  visualDescription?: string
  simplifiedExplanation?: string
  replayAudioDescription?: string
  evidenceIds: string[]
  modelDeployment?: string
  notice: string
}

export interface WorkflowExecution {
  workflowExecutionId: string
  insightId: string
  matchId: string
  executionMode: ExecutionMode
  provider: string
  modelDeployment?: string
  orchestrator: string
  status: 'RUNNING' | 'COMPLETED' | 'CANCELLED' | 'FAILED'
  startedAt: string
  completedAt?: string
  durationMs?: number
  steps: TraceStep[]
  handoffs: { from: string; to: string[]; message: string; at: string }[]
  concurrency?: { branchA: string; branchB: string; sameSuperstep: boolean; overlapped: boolean; overlapMs: number }
  frameworkSupersteps: number
  frameworkExecutorInvocations: number
  modelCalls: number
  modelCallLimit: number
  inputTokens: number
  outputTokens: number
  outcome?: WorkflowOutcome
  verificationDecision?: string
  verified: boolean
  authoritativeFingerprintBefore?: string
  authoritativeFingerprintAfter?: string
  authoritativeFactsUnchanged: boolean
  recoveryActions: string[]
  published?: PublishedInsight
  error?: string
  /** RECORDED_DEMO only: when and how the trace was originally produced. */
  recordedFrom?: string
}

export interface AgentRuntimeStatus {
  runtime: {
    executionMode: ExecutionMode
    provider: string
    modelDeployment?: string
    projectEndpointHost?: string
    credential?: string
    orchestrator: string
    packages: Record<string, string>
    configurationNote?: string
  }
  warmUp: string
  budget?: { runsLastHour: number; maxRunsPerHour: number; sessionAutoRunBudget: number; maxModelCallsPerWorkflow: number }
  autoRun?: { enabled: boolean; meaningfulImportance: number }
  agents: { role: string; name: string; responsibility: string }[]
}

export { AgentRefusal } from './agentRefusal'

export const sessionId = new URLSearchParams(location.search).get('session') ?? (() => {
  try {
    const id = sessionStorage.getItem('matchshift.session') ?? crypto.randomUUID()
    sessionStorage.setItem('matchshift.session', id)
    return id
  } catch { return crypto.randomUUID() }
})()
const sessionUrl = (path: string) => `${path}${path.includes('?') ? '&' : '?'}session=${encodeURIComponent(sessionId)}`

const json = async <T,>(res: Response): Promise<T> => {
  if (!res.ok) throw new Error(`${res.status} ${await res.text()}`)
  return res.json() as Promise<T>
}

const post = (path: string, body?: unknown) =>
  fetch(sessionUrl(path), {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })

export const profileQuery = (p: Profile) => {
  const q = new URLSearchParams({ lang: p.language, mode: p.mode, session: sessionId })
  if (p.focusTeamId) q.set('team', p.focusTeamId)
  if (p.focusPlayerId) q.set('player', p.focusPlayerId)
  if (p.accessible) q.set('a11y', 'true')
  return q.toString()
}

const serverApi = {
  meta: () => fetch('/api/meta').then(json<Meta>),
  matches: () => fetch('/api/matches').then(json<MatchSummary[]>),
  match: (id: string) => fetch(`/api/matches/${id}`).then(json<MatchDetail>),
  play: () => post('/api/demo/play'),
  pause: () => post('/api/demo/pause'),
  reset: (matchId?: string) => post('/api/demo/reset', { matchId: matchId ?? null }),
  speed: (speed: number) => post('/api/demo/speed', { speed }),
  seek: (minute: number) => post('/api/demo/seek', { minute }),
  insightEvents: (id: string) => fetch(sessionUrl(`/api/demo/insights/${id}/events`)).then(json<InsightEvents>),
  highlightEvents: (id: string) => fetch(sessionUrl(`/api/demo/highlights/${id}/events`)).then(json<InsightEvents>),
  replay: (id: string) => fetch(sessionUrl(`/api/demo/insights/${id}/replay`)).then(json<ReplaySequence>),
  recap: (period: 'halftime' | 'fulltime', style: RecapStyle, p: Profile) =>
    fetch(`/api/demo/recap/${period}?style=${style}&${profileQuery(p)}`).then(async (r) =>
      r.status === 409 ? null : json<RecapResponse>(r),
    ),
  agentTrace: async (id: string, p: Profile, run = true): Promise<WorkflowExecution | null> => {
    const res = await fetch(`/api/demo/insights/${id}/agents?${profileQuery(p)}${run ? '' : '&run=false'}`)
    if (res.status === 204) return null
    if (res.status === 429) throw new AgentRefusal(((await res.json()) as { message: string }).message)
    return json<WorkflowExecution>(res)
  },
  agentStatus: () => fetch('/api/agents/status').then(json<AgentRuntimeStatus>),
  streamUrl: (p: Profile) => `/api/demo/stream?${profileQuery(p)}`,
}

const demo = isStaticDemo ? createDemoClient(sessionId) : undefined
export const api = demo ?? serverApi
export const subscribeDemo = demo?.subscribe
