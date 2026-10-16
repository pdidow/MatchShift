import type { ReplaySequence } from './replay/types'

// Typed client for the MatchShift API. All data served is synthetic.

export type ViewerMode = 'TeachMe' | 'WhatMatters' | 'Analyst' | 'MyTeam' | 'Studio'
export type RecapStyle = 'Casual' | 'Analyst' | 'Broadcast' | 'Social'
export type Lang = 'en' | 'fr' | 'es'

export interface Profile {
  mode: ViewerMode
  language: Lang
  focusTeamId?: string | null
  focusPlayerId?: string | null
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
  events: PitchEvent[]
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

export interface AgentStep {
  agent: string
  responsibility: string
  input: string
  output: string
  status: 'Completed' | 'Approved' | 'Rejected' | 'Fallback' | 'Skipped'
  durationMs: number
}

export interface AgentTrace {
  insightId: string
  provider: string
  steps: AgentStep[]
  narrative: Narrative
}

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
  return q.toString()
}

export const api = {
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
  agentTrace: (id: string, p: Profile) => fetch(`/api/demo/insights/${id}/agents?${profileQuery(p)}`).then(json<AgentTrace>),
  streamUrl: (p: Profile) => `/api/demo/stream?${profileQuery(p)}`,
}
