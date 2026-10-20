import type { SpeechDetail } from './prefs'
import type { CueClass, Priority } from './speech'

// How much MatchShift says at each playback speed. Speech is never sped up to keep pace with the picture: at higher
// speeds it says FEWER, CURRENT things (and drops anything that is no longer current), so the timeline never waits.

export interface NarrationPolicy {
  /** Cues below this priority are not spoken at this speed. */
  minPriority: Priority
  /** The most detail spoken at this speed (the viewer's own setting is capped, never raised). */
  detailCap: SpeechDetail
}

/**
 * Normal match playback.
 *  - 1×: the viewer's chosen detail; everything that passes it may be spoken.
 *  - 2× (default): concise - minute, type, team and headline only ("major" detail, about 4 s of speech instead of about
 *    12 s with the fact sentence, measured in Chrome); stale cues are dropped.
 *  - 5×: key events and meaningful insights only (normal priority and above, major detail).
 *  - 10×: critical moments only (goals, dangerous moments, full-time/half-time).
 */
export function matchNarrationPolicy(speed: number): NarrationPolicy {
  if (speed <= 1) return { minPriority: 0, detailCap: 'detailed' }
  if (speed <= 2) return { minPriority: 0, detailCap: 'major' }
  if (speed <= 5) return { minPriority: 1, detailCap: 'major' }
  return { minPriority: 2, detailCap: 'major' }
}

/**
 * Whether match cues may start. A paused match starts none, but a finished match no longer moves, so its final cues (a
 * goal on the whistle, full time) are still current and may speak.
 */
export const matchTimelineLive = (m: { isPlaying: boolean; finished: boolean }) => m.isPlaying || m.finished

/**
 * Evidence Replay. At normal speed (or slower) every described event may be spoken; faster, only supporting evidence,
 * goals and BEFORE/AFTER transitions. Routine movement is never narrated as a backlog.
 */
export function replayNarrationPolicy(speed: number): Pick<NarrationPolicy, 'minPriority'> {
  return { minPriority: speed <= 1 ? 0 : 1 }
}

/**
 * Semantic class of an insight cue, from its type and verification only (never AI judgement): a goal is a goal; a
 * high-severity insight the Verification Agent accepted is a major insight; anything else ranks by its priority.
 */
export function insightCueClass(card: { type: string; severity: string; agent?: { verified: boolean } }): CueClass | undefined {
  if (card.type === 'GOAL') return 'goal'
  if (card.severity === 'High' && card.agent?.verified) return 'major'
  return undefined
}

const DETAIL_ORDER: SpeechDetail[] = ['major', 'important', 'detailed']

/** The viewer's detail level, capped by the speed policy (never more detailed than they asked for). */
export const capDetail = (user: SpeechDetail, cap: SpeechDetail): SpeechDetail =>
  DETAIL_ORDER[Math.min(DETAIL_ORDER.indexOf(user), DETAIL_ORDER.indexOf(cap))]

/** Replay cue priority from what the event is: goals and clip transitions are critical, supporting evidence is normal. */
export function replayCuePriority(e: { label: string; isEvidence: boolean }): Priority {
  if (e.label === 'GOAL') return 2
  return e.isEvidence ? 1 : 0
}
