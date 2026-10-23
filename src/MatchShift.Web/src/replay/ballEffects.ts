import type { ReplayClip, ReplaySequence } from './types'

/** Only supporting source events drive the visual cue; context is never treated as evidence. */
export function keyEventTimes(seq: ReplaySequence, clip: ReplayClip): number[] {
  const evidence = new Set(seq.evidenceEventIds)
  return clip.events.filter(event => event.isEvidence && evidence.has(event.eventId)).map(event => event.time)
}

export function keyEventPulse(times: readonly number[], local: number): number {
  let pulse = 0
  for (const time of times) {
    const age = local - time
    if (age >= 0 && age < 0.55) pulse = Math.max(pulse, Math.sin(Math.PI * age / 0.55) * (1 - age / 0.8))
  }
  return pulse
}
