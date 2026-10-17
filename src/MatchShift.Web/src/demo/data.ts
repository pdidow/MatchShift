import type { HighlightCard, InsightCard, MatchDetail, MatchSummary, Meta, Narrative, PitchEvent, PlayerPerformance, Profile, Snapshot } from '../api'

export interface DemoIndex { schemaVersion: number; defaultMatchId: string; meta: Meta; matches: MatchSummary[] }
export interface DemoFrame { second: number; score: Snapshot['score']; metrics: Snapshot['metrics']; highlights: string[] }
export interface DemoCore {
  detail: MatchDetail
  frames: DemoFrame[]
  events: PitchEvent[]
  timeline: Snapshot['timeline']
  profiles: Record<string, string[]>
  players: Record<string, PlayerPerformance>
  insightEvents: Record<string, string[]>
  highlightEvents: Record<string, string[]>
}
export interface DemoLanguage {
  cards: InsightCard[]
  highlights: Record<string, HighlightCard>
  focusText: Record<string, Record<string, Pick<Narrative, 'perspective' | 'playerLine'>>>
}
export interface Playback { matchId: string; second: number; speed: number; playing: boolean; epoch: number }

export function frameAt(frames: DemoFrame[], second: number): DemoFrame {
  let low = 0, high = frames.length - 1
  while (low < high) {
    const mid = Math.ceil((low + high) / 2)
    if (frames[mid].second <= second) low = mid
    else high = mid - 1
  }
  return frames[low]
}

export function playerAt(player: PlayerPerformance, second: number): PlayerPerformance {
  const events = player.events.filter(e => e.matchSecond <= second)
  const seen = new Set(events.map(e => e.id))
  const ids = (list: string[]) => list.filter(id => seen.has(id))
  const passIds = ids(player.passIds), completedIds = ids(player.completedIds), progressiveIds = ids(player.progressiveIds)
  const shotIds = ids(player.shotIds), recoveryIds = ids(player.recoveryIds), turnoverIds = ids(player.turnoverIds)
  return { playerId: player.playerId, events, passIds, completedIds, progressiveIds, shotIds, recoveryIds, turnoverIds,
    passes: passIds.length, completed: completedIds.length, progressive: progressiveIds.length,
    shots: shotIds.length, recoveries: recoveryIds.length, turnovers: turnoverIds.length }
}

export function snapshotAt(core: DemoCore, words: DemoLanguage, playback: Playback, profile: Profile, sessionId: string): Snapshot {
  const second = Math.max(0, Math.min(5400, Math.floor(playback.second)))
  const frame = frameAt(core.frames, second)
  const available = new Map(words.cards.filter(c => c.second <= second).map(c => [c.id, c]))
  const focus = `${profile.focusTeamId ?? ''}|${profile.focusPlayerId ?? ''}`
  const order = core.profiles[`${profile.mode}|${focus}`] ?? core.profiles[`${profile.mode}||`] ?? []
  const insights = order.flatMap(id => {
    const card = available.get(id)
    if (!card) return []
    const personalized = profile.mode === 'MyTeam' ? words.focusText[focus]?.[id] : undefined
    return [{ ...card, narrative: { ...card.narrative, mode: profile.mode,
      teaching: profile.mode === 'TeachMe' ? card.narrative.teaching : undefined,
      perspective: personalized?.perspective, playerLine: personalized?.playerLine } }]
  })
  const overlay = insights.filter(i => second < i.second + 120).sort((a, b) => b.importance - a.importance || b.second - a.second)[0]
  const player = profile.focusPlayerId ? core.players[profile.focusPlayerId] : undefined
  return {
    sessionId, epoch: playback.epoch, matchId: playback.matchId,
    isPlaying: playback.playing && second < 5400, speed: playback.speed, finished: second >= 5400,
    clock: { second, minute: Math.min(90, Math.floor(second / 60) + 1), period: second < 2700 ? 1 : 2,
      label: `${String(Math.floor(second / 60)).padStart(2, '0')}:${String(second % 60).padStart(2, '0')}` },
    score: frame.score, home: core.detail.summary.home, away: core.detail.summary.away,
    recentEvents: core.events.filter(e => e.second <= second).slice(-30), metrics: frame.metrics,
    timeline: core.timeline.filter(p => p.minute * 60 <= second), insights,
    highlights: frame.highlights.map(id => words.highlights[id]).filter(Boolean), profile,
    focusedPlayer: player ? playerAt(player, second) : undefined,
    overlay: overlay ? { insightId: overlay.id, text: overlay.narrative.studioLine, source: 'template', language: profile.language,
      validFromSecond: overlay.second, validUntilSecond: overlay.second + 120,
      evidenceReferences: [...new Set(overlay.visuals.flatMap(v => v.eventIds))] } : undefined,
  }
}
