// Text descriptions generated from STRUCTURED data only (insight evidence, event coordinates, metrics).
// No numbers are invented here: every figure comes from the deterministic analytics, as everywhere else in MatchShift.
import type { InsightCard, InsightEvents, PitchEvent, Snapshot } from '../api'
import type { ReplayEvent, ReplayPlayer } from '../replay/types'
import type { A11yStrings } from './strings'
import { fill } from './strings'
import type { Priority } from './speech'
import type { PeriodicRecap, SpeechDetail } from './prefs'

export type Channel = 'Left' | 'Centre' | 'Right'

const MAJOR_TYPES = new Set(['GOAL', 'DANGEROUS_TURNOVER', 'BIG_CHANCE'])
export const SHIFT_TYPES = new Set(['ATTACK_CHANNEL_SHIFT', 'POST_SUBSTITUTION_CHANGE', 'CONTROL_SHIFT', 'TERRITORIAL_SHIFT'])

/** Whether an insight is spoken at a given detail level. */
export function shouldSpeak(card: Pick<InsightCard, 'type' | 'severity'>, detail: SpeechDetail): boolean {
  const major = MAJOR_TYPES.has(card.type) || (SHIFT_TYPES.has(card.type) && card.severity === 'High')
  if (detail === 'major') return major
  if (detail === 'important') return major || card.severity !== 'Low'
  return true
}

/** Goals and dangerous moments are critical (they may interrupt); low-severity insights are low priority. */
export function priorityOf(card: Pick<InsightCard, 'type' | 'severity'>): Priority {
  if (card.type === 'GOAL' || card.type === 'DANGEROUS_TURNOVER') return 2
  return card.severity === 'Low' ? 0 : 1
}

/** Channel named by the primary evidence metric (e.g. "RightAttackShare" → Right). */
export function channelOf(card: Pick<InsightCard, 'narrative'>): Channel | null {
  const metric = (card.narrative.evidence.find(e => e.isPrimary) ?? card.narrative.evidence[0])?.metric ?? ''
  if (metric.startsWith('Left')) return 'Left'
  if (metric.startsWith('Right')) return 'Right'
  if (metric.startsWith('Centre') || metric.startsWith('Center')) return 'Centre'
  return null
}

const sentence = (t?: string) => {
  const v = (t ?? '').trim()
  return v === '' ? '' : /[.!?…]$/.test(v) ? v : `${v}.`
}
const join = (...parts: (string | undefined)[]) => parts.map(sentence).filter(Boolean).join(' ')

/** Plain-language one-liner for Simplified Insights. */
export function simplifiedText(card: InsightCard, s: A11yStrings, teamName: (id?: string) => string, opponentOf: (id?: string) => string): string {
  const template = s.simple[card.type] ?? s.simple.default
  const channel = channelOf(card)
  return fill(template, {
    team: teamName(card.teamId) || '—',
    opponent: opponentOf(card.teamId) || '—',
    channel: channel ? s.channels[channel] : '',
  })
}

/** What is spoken for a new insight at the chosen detail level. */
export function insightSpeech(card: InsightCard, detail: SpeechDetail, simplified: boolean, s: A11yStrings,
  teamName: (id?: string) => string, opponentOf: (id?: string) => string): string {
  const n = card.narrative
  const team = teamName(card.teamId)
  const intro = `${card.minute}'. ${n.typeLabel}${team ? `, ${team}` : ''}.`
  const agent = verifiedAgent(card)
  if (simplified) return join(intro, agent?.simplifiedExplanation ?? simplifiedText(card, s, teamName, opponentOf))
  if (detail === 'major') return join(intro, n.headline)
  if (detail === 'important') return join(intro, n.headline, n.fact)
  // The Accessibility Narrator's spoken summary replaces the visual card text only once the workflow verified it.
  if (agent?.spokenSummary) return join(intro, agent.spokenSummary)
  return join(intro, n.headline, n.fact, n.inference, n.explanation)
}

/** Agent output that may be read aloud: only when the workflow verified it. */
export const verifiedAgent = (card: Pick<InsightCard, 'agent'>) => (card.agent?.verified ? card.agent : undefined)

/** READ EXPLANATION (WHY?): fact, inference, explanation, confidence, supporting evidence. */
export function whyNarration(card: InsightCard, s: A11yStrings): string {
  const n = card.narrative
  const evidence = n.evidence.slice(0, 3).map(e => {
    const windows = e.baselineWindow && e.recentWindow ? ` (${e.baselineWindow} → ${e.recentWindow})` : ''
    return e.baseline ? `${e.label}: ${e.baseline} → ${e.recent}${windows}` : `${e.label}: ${e.recent}`
  })
  return join(
    n.headline,
    `${s.why.fact}: ${n.fact}`,
    `${s.why.inference}: ${n.inference}`,
    n.explanation ? `${s.why.explanation}: ${n.explanation}` : '',
    `${s.why.confidence}: ${n.confidenceLabel}`,
    evidence.length ? `${s.why.evidence}: ${evidence.join('; ')}` : '',
    verifiedAgent(card)?.spokenSummary ?? '',
    verifiedAgent(card)?.visualDescription ?? '',
  )
}

/** READ WHAT CHANGED: before → after of the primary evidence. Null when the insight has no before/after (e.g. a goal). */
export function whatChanged(card: InsightCard, s: A11yStrings): string | null {
  const primary = card.narrative.evidence.find(e => e.isPrimary) ?? card.narrative.evidence[0]
  if (!primary?.baseline) return null
  const win = (w?: string) => {
    const m = w?.match(/^(\d+)'?\s*-\s*(\d+)'?$/)
    return m ? ` ${fill(s.why.between, { from: `${m[1]}'`, to: `${m[2]}'` })}` : ''
  }
  return fill(s.why.changed, {
    minute: card.minute,
    metric: primary.label,
    before: primary.baseline,
    after: primary.recent,
    beforeWindow: win(primary.baselineWindow),
    afterWindow: win(primary.recentWindow),
  })
}

/** Audio Match Mode status: time, score, who has control, where their attacks go. */
export function matchStatus(snap: Snapshot, s: A11yStrings): string {
  const st = s.status_
  const { home, away } = snap.score
  const parts = [fill(st.minutes, { minute: snap.clock.minute })]
  if (home === away) parts.push(fill(st.level, { home: snap.home.shortName, hs: home, away: snap.away.shortName, as: away }))
  else {
    const homeLeads = home > away
    parts.push(fill(st.lead, {
      leader: homeLeads ? snap.home.shortName : snap.away.shortName,
      ls: Math.max(home, away), ts: Math.min(home, away),
    }))
  }
  const control = snap.metrics.homeControl
  if (control >= 58 || control <= 42) {
    const homeOnTop = control >= 58
    const team = homeOnTop ? snap.home : snap.away
    const m = homeOnTop ? snap.metrics.home : snap.metrics.away
    parts.push(fill(st.control, { team: team.shortName }))
    if (m.attacks >= 3) {
      const shares: [Channel, number][] = [['Left', m.leftShare], ['Centre', m.centreShare], ['Right', m.rightShare]]
      const [channel, share] = shares.reduce((a, b) => (b[1] > a[1] ? b : a))
      if (share >= 0.45) parts.push(fill(st.attacks, { channel: s.channels[channel] }))
    }
  } else parts.push(st.balanced)
  return parts.join(' ')
}

export function goalAnnouncement(snap: Snapshot, scorerTeamId: string, s: A11yStrings): string {
  const team = scorerTeamId === snap.home.id ? snap.home.shortName : snap.away.shortName
  return fill(s.status_.goal, { team, home: snap.home.shortName, hs: snap.score.home, away: snap.away.shortName, as: snap.score.away })
}

/** Minutes at which a periodic recap is due, given the previous and current match minute. */
export function periodicDue(choice: PeriodicRecap, previousMinute: number, minute: number): boolean {
  if (minute <= previousMinute) return false
  if (choice === '5' || choice === '10') {
    const every = Number(choice)
    return Math.floor(minute / every) > Math.floor(previousMinute / every)
  }
  if (choice === 'halftime') return previousMinute < 45 && minute >= 45
  return false
}

// ------------------------------------------------------------------ spatial language

/** Team-frame coordinates: the team attacks towards X = 100; Y = 0 is the team's left. */
export const teamFrame = (x: number, y: number, isHome: boolean) => (isHome ? { x, y } : { x: 100 - x, y: 100 - y })

export function sideOf(teamY: number): 'left' | 'centre' | 'right' {
  return teamY < 33.3 ? 'left' : teamY > 66.7 ? 'right' : 'centre'
}

export function zoneOf(teamX: number, teamY: number): keyof A11yStrings['zones'] {
  if (teamX >= 83 && teamY >= 21 && teamY <= 79) return 'box'
  if (teamX >= 66.7) return 'attacking'
  if (teamX >= 33.3) return 'middle'
  return 'defensive'
}

/** Distance to the goal being attacked, in metres (105 × 68 pitch). */
export const metresFromGoal = (teamX: number, teamY: number) =>
  Math.round(Math.hypot((100 - teamX) * 1.05, (teamY - 50) * 0.68))

export function directionOf(dx: number, dy: number): keyof A11yStrings['dir'] {
  // dx, dy in team frame metres-ish units; forward = towards the opponent's goal.
  const ax = Math.abs(dx * 1.05)
  const ay = Math.abs(dy * 0.68)
  if (ax >= ay * 0.8) return dx >= 0 ? 'forward' : 'back'
  if (ax < 4 && ay >= 4) return 'across'
  return dy < 0 ? 'left' : 'right'
}

const where = (teamX: number, teamY: number, s: A11yStrings) => `${s.side[sideOf(teamY)]}, ${s.zones[zoneOf(teamX, teamY)]}`

/** Audio description for one replay event, from its coordinates, type, outcome and players. */
export function describeReplayEvent(e: ReplayEvent, players: ReplayPlayer[], homeTeamId: string, s: A11yStrings): string | null {
  const ad = s.ad
  const byId = (id?: string) => players.find(p => p.id === id)
  const name = (p?: ReplayPlayer) => (p ? `${p.shortName} (${fill(ad.number, { n: p.shirtNumber })})` : '')
  const actor = name(byId(e.playerId)) || (e.teamId === homeTeamId ? ad.home : ad.away)
  const target = name(byId(e.relatedPlayerId))
  const isHome = e.teamId === homeTeamId
  if (e.x === undefined || e.y === undefined) {
    if (e.type === 'Foul') return fill(ad.foul, { actor })
    return null
  }
  const start = teamFrame(e.x, e.y, isHome)
  const end = e.endX !== undefined && e.endY !== undefined ? teamFrame(e.endX, e.endY, isHome) : null
  const dir = end ? s.dir[directionOf(end.x - start.x, end.y - start.y)] : ''
  const at = where(start.x, start.y, s)
  const endWhere = end ? where(end.x, end.y, s) : at

  switch (e.label) {
    case 'KICK_OFF': return null
    case 'PASS_LOST': return fill(ad.passLost, { actor, dir, where: endWhere })
    case 'CROSS': return fill(ad.cross, { actor, target: target || '…' })
    case 'CUTBACK': return fill(ad.cutback, { actor, target: target || '…' })
    case 'LONG_BALL': return fill(ad.longBall, { actor, dir, target: target || '…' })
    case 'PASS':
    case 'PROGRESSIVE_PASS': return target ? fill(ad.pass, { actor, dir, target, where: endWhere }) : fill(ad.passLost, { actor, dir, where: endWhere })
    case 'CARRY': return end ? fill(ad.carry, { actor, dir, zone: s.zones[zoneOf(end.x, end.y)] }) : null
    case 'GOAL': return `${fill(ad.shot, { actor, metres: metresFromGoal(start.x, start.y) })} ${fill(ad.goal, { actor })}`
    case 'SHOT_ON_TARGET': return `${fill(ad.shot, { actor, metres: metresFromGoal(start.x, start.y) })} ${ad.saved}`
    case 'SHOT_BLOCKED': return `${fill(ad.shot, { actor, metres: metresFromGoal(start.x, start.y) })} ${ad.blocked}`
    case 'SHOT_OFF_TARGET': return `${fill(ad.shot, { actor, metres: metresFromGoal(start.x, start.y) })} ${ad.wide}`
    case 'HIGH_PRESS_INTERCEPTION': return fill(ad.pressInterception, { actor, where: at })
    case 'INTERCEPTION': return fill(ad.interception, { actor, where: at })
    case 'HIGH_PRESSURE_RECOVERY':
    case 'RECOVERY': return fill(ad.recovery, { actor, where: at })
    case 'TACKLE': return fill(ad.tackle, { actor, target: target || '…' })
    case 'CLEARANCE': return fill(ad.clearance, { actor })
    case 'SAVE': return fill(ad.save, { actor })
    case 'CORNER': return fill(ad.corner, { team: isHome ? 'Home' : 'Away' })
    case 'FOUL': return fill(ad.foul, { actor })
    default: return null
  }
}

/** DESCRIBE THIS for SHOW ME: counts and the dominant side, straight from the plotted events. */
export function describeFocus(focus: InsightEvents, teamId: string | undefined, homeTeamId: string,
  teamName: (id?: string) => string, s: A11yStrings): string {
  // Count the authoritative supporting evidence when the server provides it; context-only events are never counted.
  const shownIds = new Set(focus.supportingEventIds ?? focus.layers.filter(l => l.emphasised).flatMap(l => l.eventIds))
  const shown = focus.events.filter(e => (shownIds.size === 0 || shownIds.has(e.id)) && (!teamId || e.teamId === teamId))
  const team = teamId ?? shown[0]?.teamId
  if (shown.length === 0) return fill(s.show.intro, { count: 0, team: teamName(team), range: '' })
  const minutes = shown.map(e => Math.floor(e.second / 60))
  const from = Math.min(...minutes)
  const to = Math.max(...minutes)
  const range = from === to ? '' : fill(s.show.range, { from, to })
  const located = shown.filter((e): e is PitchEvent & { x: number; y: number } => e.x !== undefined && e.y !== undefined)
  const frame = (e: { x: number; y: number; teamId: string }) => teamFrame(e.x, e.y, e.teamId === homeTeamId)
  const passes = shown.filter(e => e.type === 'Pass' || e.type === 'Carry')
  const entries = passes.filter(e => e.endX !== undefined && teamFrame(e.endX, e.endY ?? 50, e.teamId === homeTeamId).x >= 66.7)
  const shots = shown.filter(e => e.type === 'Shot')
  const turnovers = shown.filter(e => ['Interception', 'BallRecovery', 'Turnover', 'Tackle'].includes(e.type) || (e.type === 'Pass' && e.outcome !== 'Complete'))
  const parts = [fill(s.show.intro, { count: shown.length, team: teamName(team), range })]
  if (passes.length) parts.push(fill(s.show.passes, { n: passes.length }))
  if (entries.length) parts.push(fill(s.show.entries, { n: entries.length }))
  if (shots.length) parts.push(fill(s.show.shots, { n: shots.length }))
  if (turnovers.length) parts.push(fill(s.show.turnovers, { n: turnovers.length }))
  if (located.length >= 3) {
    const counts = { left: 0, centre: 0, right: 0 }
    for (const e of located) {
      const p = e.endX !== undefined && e.endY !== undefined ? frame({ x: e.endX, y: e.endY, teamId: e.teamId }) : frame(e)
      counts[sideOf(p.y)]++
    }
    const top = (Object.entries(counts) as ['left' | 'centre' | 'right', number][]).reduce((a, b) => (b[1] > a[1] ? b : a))
    if (top[1] / located.length >= 0.5) parts.push(fill(s.show.channel, { side: s.side[top[0]] }))
  }
  return parts.join(' ')
}
