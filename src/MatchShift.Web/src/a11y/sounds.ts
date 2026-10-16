// Optional, subtle non-speech sound cues (Web Audio, synthesised - no audio files). Off by default.
// Sounds only ever accompany information that is also shown visually and in text.

export type Cue = 'insight' | 'shift' | 'turnover' | 'goal'

const PATTERNS: Record<Cue, { freq: number; at: number; dur: number }[]> = {
  insight: [{ freq: 660, at: 0, dur: 0.12 }],
  shift: [{ freq: 523, at: 0, dur: 0.1 }, { freq: 784, at: 0.11, dur: 0.14 }],
  turnover: [{ freq: 392, at: 0, dur: 0.12 }, { freq: 294, at: 0.13, dur: 0.18 }],
  goal: [{ freq: 523, at: 0, dur: 0.12 }, { freq: 659, at: 0.12, dur: 0.12 }, { freq: 784, at: 0.24, dur: 0.24 }],
}

let context: AudioContext | null = null

export function cueFor(type: string): Cue {
  if (type === 'GOAL') return 'goal'
  if (type === 'DANGEROUS_TURNOVER' || type === 'TURNOVER_SPIKE') return 'turnover'
  if (['ATTACK_CHANNEL_SHIFT', 'CONTROL_SHIFT', 'POST_SUBSTITUTION_CHANGE', 'TERRITORIAL_SHIFT', 'PASSING_STYLE_SHIFT', 'TEMPO_SHIFT', 'POSSESSION_SHIFT'].includes(type)) return 'shift'
  return 'insight'
}

export function playCue(cue: Cue, volume: number) {
  try {
    const Ctx = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
    if (!Ctx) return
    context ??= new Ctx()
    if (context.state === 'suspended') void context.resume()
    const start = context.currentTime + 0.02
    for (const note of PATTERNS[cue]) {
      const osc = context.createOscillator()
      const gain = context.createGain()
      osc.type = 'sine'
      osc.frequency.value = note.freq
      const peak = 0.12 * Math.max(0, Math.min(1, volume))
      gain.gain.setValueAtTime(0, start + note.at)
      gain.gain.linearRampToValueAtTime(peak, start + note.at + 0.015)
      gain.gain.exponentialRampToValueAtTime(0.0001, start + note.at + note.dur)
      osc.connect(gain).connect(context.destination)
      osc.start(start + note.at)
      osc.stop(start + note.at + note.dur + 0.02)
    }
  } catch { /* audio unavailable: cues are optional */ }
}
