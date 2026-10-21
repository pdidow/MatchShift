// What a replay event IS (evidence, context or other play) and where the replay's positions come from. Pure: computed
// only from the replay sequence the server built, never from the animation, camera or crowd.
import type { Provenance, ReplayClip, ReplayEvent, ReplaySequence } from './types'

export type EventRole = 'supporting' | 'context' | 'other'

/**
 * The sequence's ID lists are authoritative (evidenceEventIds = supporting evidence only). An event is context only if
 * it is not supporting evidence, so a context event can never be shown as evidence.
 */
export function eventRole(seq: ReplaySequence, e: ReplayEvent): EventRole {
  if (seq.evidenceEventIds.includes(e.eventId)) return 'supporting'
  if (seq.contextEventIds?.includes(e.eventId) || e.isContext) return 'context'
  return 'other'
}

/** A shape per role, so the role never depends on colour alone. */
export const ROLE_MARK: Record<EventRole, string> = { supporting: '■', context: '◇', other: '' }

export interface ClipCoverage { key: ReplayClip['key']; supporting: number; context: number; other: number }

export interface Coverage {
  /** The insight's evidence (the same counts as the card, WHY?, SHOW ME and Agents). */
  supporting: number
  context: number
  /** How many distinct supporting / context events these clips actually show: a viewing window, not a new count. */
  supportingShown: number
  contextShown: number
  clips: ClipCoverage[]
}

export function coverage(seq: ReplaySequence): Coverage {
  const shown = (role: EventRole) => new Set(seq.clips.flatMap(c => c.events.filter(e => eventRole(seq, e) === role).map(e => e.eventId))).size
  return {
    supporting: seq.evidenceEventIds.length,
    context: seq.contextEventIds?.length ?? 0,
    supportingShown: shown('supporting'),
    contextShown: shown('context'),
    clips: seq.clips.map(c => {
      const roles = c.events.map(e => eventRole(seq, e))
      return { key: c.key, supporting: roles.filter(r => r === 'supporting').length, context: roles.filter(r => r === 'context').length, other: roles.filter(r => r === 'other').length }
    }),
  }
}

/** Share of all player-position samples in the replay by provenance, as the server labelled them. */
export function positionShare(seq: ReplaySequence): Record<Provenance, number> {
  const counts: Record<Provenance, number> = { ACTUAL_EVENT_POSITION: 0, INTERPOLATED_POSITION: 0, ESTIMATED_POSITION: 0 }
  for (const c of seq.clips) for (const t of c.tracks) for (const s of t.samples) counts[s.provenance]++
  const total = Object.values(counts).reduce((a, b) => a + b, 0) || 1
  return {
    ACTUAL_EVENT_POSITION: counts.ACTUAL_EVENT_POSITION / total,
    INTERPOLATED_POSITION: counts.INTERPOLATED_POSITION / total,
    ESTIMATED_POSITION: counts.ESTIMATED_POSITION / total,
  }
}

/** "91%", and "<1%" for a share that is present but rounds to zero (so a rare value is never shown as absent). */
export const percent = (share: number) => (share > 0 && share < 0.005 ? '<1%' : `${Math.round(share * 100)}%`)

/** Whether the event itself carries a recorded position (its start point). */
export const hasRecordedPosition = (e: ReplayEvent) => e.x !== undefined && e.x !== null && e.y !== undefined && e.y !== null
