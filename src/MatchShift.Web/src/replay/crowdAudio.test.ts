import audioSource from './crowdAudio.ts?raw'
import viewSource from './ReplayView.tsx?raw'
import goalWav from '../../public/audio/crowd-goal.wav?inline'
import chanceWav from '../../public/audio/crowd-chance.wav?inline'
import murmurWav from '../../public/audio/crowd-murmur.wav?inline'
import { describe, expect, it } from 'vitest'
import {
  CROWD_LEVEL, CROWD_SOUNDS, CrowdSoundPlayer, crowdClipUrl, DESCRIPTION_DUCK, NARRATION_DUCK, crowdSoundAllowed, crowdSoundStep, duckFactor,
} from './crowdAudio'
import type { AudioLike, GainLike, SourceLike } from './crowdAudio'
import { SpeechQueue } from '../a11y/speech'
import type { SynthLike, UtteranceLike } from '../a11y/speech'
import { t } from '../i18n'
import { LANGUAGE_CODES } from '../languages'

// Crowd sound is decorative atmosphere: it follows the replay clock, ducks under narration, never touches speech or evidence.
// Everything runs against a fake Web Audio context - no audio device, no network, no Azure.

const EVENTS = [
  { label: 'PASS', at: 4 }, { label: 'PASS_LOST', at: 6 }, { label: 'HIGH_PRESS_INTERCEPTION', at: 7 }, { label: 'CARRY', at: 10 },
  { label: 'SHOT_OFF_TARGET', at: 12 }, { label: 'GOAL', at: 14 },
]
const pos = (time: number, playing = true, clip = 0, jumps = 0) => ({ time, clip, playing, introActive: false, jumps })

/** Feed the replay clock through crowdSoundStep like the view does, and record what plays and stops. */
function drive(times: (number | { time: number; playing?: boolean; clip?: number; jumps?: number })[]) {
  let prev = { time: -1, clip: -1, jumps: 0 }
  const played: string[] = []
  let stops = 0
  for (const x of times) {
    const now = typeof x === 'number' ? pos(x, true, 0, prev.jumps) : pos(x.time, x.playing ?? true, x.clip ?? 0, x.jumps ?? prev.jumps)
    const step = crowdSoundStep(prev, now, EVENTS)
    prev = { time: now.time, clip: now.clip, jumps: now.jumps }
    if (step.stop) stops++
    if (step.play) played.push(`${step.play.label}:${step.play.clip}@${step.play.gain}`)
  }
  return { played, stops }
}
const along = (from: number, to: number, step = 0.1) => Array.from({ length: Math.round((to - from) / step) + 1 }, (_, i) => +(from + i * step).toFixed(3))

class FakeAudio implements AudioLike {
  currentTime = 0
  state = 'running'
  destination = {}
  gains: (GainLike & { targets: number[] })[] = []
  sources: (SourceLike & { started: boolean; stopped: boolean })[] = []
  decodes = 0
  async resume() {}
  createGain() {
    const g = { targets: [] as number[], gain: { value: 1, setTargetAtTime(v: number) { g.targets.push(v); g.gain.value = v } }, connect: () => undefined }
    this.gains.push(g)
    return g
  }
  createBufferSource() {
    const s = { buffer: null as unknown, started: false, stopped: false, onended: null as (() => void) | null, connect: () => undefined, start() { s.started = true }, stop() { s.stopped = true } }
    this.sources.push(s)
    return s
  }
  async decodeAudioData() { this.decodes++; return { decoded: true } }
}
const player = () => {
  const ctx = new FakeAudio()
  const loads: string[] = []
  const p = new CrowdSoundPlayer({ makeContext: () => ctx, load: async f => { loads.push(f); return new ArrayBuffer(8) } })
  return { ctx, p, loads }
}

describe('what plays, and when', () => {
  it('1, 2, 3: a goal plays the full roar; a shot a weaker "ooh"; routine passes and carries nothing', () => {
    expect(drive(along(13.5, 14.5)).played).toEqual(['GOAL:goal@1'])
    expect(drive(along(11.5, 12.5)).played).toEqual(['SHOT_OFF_TARGET:chance@0.5'])
    expect(drive(along(3.5, 4.5)).played).toEqual([])
    expect(drive(along(9.5, 10.5)).played).toEqual([])
    for (const l of ['SHOT_ON_TARGET', 'SHOT_OFF_TARGET', 'SHOT_BLOCKED']) { expect(CROWD_SOUNDS[l].gain).toBeGreaterThanOrEqual(0.4); expect(CROWD_SOUNDS[l].gain).toBeLessThanOrEqual(0.6) }
    for (const l of ['HIGH_PRESS_INTERCEPTION', 'HIGH_PRESSURE_RECOVERY']) expect(CROWD_SOUNDS[l].gain).toBeLessThanOrEqual(0.25)
    for (const l of ['PASS', 'PROGRESSIVE_PASS', 'CARRY', 'CROSS', 'CUTBACK', 'PASS_LOST', 'KICK_OFF', 'FOUL', 'CARD', 'OFFSIDE']) expect(CROWD_SOUNDS[l]).toBeUndefined()
  })

  it('4: each event sounds once per pass through it', () => {
    const { played } = drive(along(0, 19, 0.05))
    expect(played).toEqual(['HIGH_PRESS_INTERCEPTION:murmur@0.2', 'SHOT_OFF_TARGET:chance@0.5', 'GOAL:goal@1'])
  })

  it('5: a seek stops the current sound and never plays the events it skipped', () => {
    const forward = drive([13.9, 14.0, 14.1, 18])                     // goal plays, then a jump forward
    expect(forward.played).toEqual(['GOAL:goal@1'])
    expect(crowdSoundStep({ time: 14.1, clip: 0, jumps: 0 }, pos(18), EVENTS)).toEqual({ stop: true, play: null })
    expect(drive([1, 1.1, 16]).played).toEqual([])                    // skipping over the goal never plays it
    expect(crowdSoundStep({ time: 15, clip: 0, jumps: 0 }, pos(5), EVENTS).stop).toBe(true)   // seeking back stops it too
    // A short step forward ("Next event", the event list) is a seek too: it never plays what it skipped or landed on.
    expect(drive([6, { time: 7, jumps: 1 }, 7.1]).played).toEqual([])
    expect(drive([13.5, { time: 14, jumps: 1 }, 14.1, 14.2]).played).toEqual([])
  })

  it('6: after seeking back before an event, or restarting, it sounds again', () => {
    expect(drive([13.9, 14.1, 13.5, 13.6, 13.9, 14.1]).played).toEqual(['GOAL:goal@1', 'GOAL:goal@1'])
    expect(drive([...along(13.8, 14.2), { time: 0, jumps: 1 }, ...along(0.1, 1), { time: 13.8, jumps: 2 }, ...along(13.9, 14.2)]).played)
      .toEqual(['GOAL:goal@1', 'GOAL:goal@1'])
  })

  it('pause starts nothing new and stops the current sound; a clip change stops a stale sound', () => {
    expect(drive([13.9, { time: 14.1, playing: false }]).played).toEqual([])
    expect(crowdSoundStep({ time: 14.1, clip: 0, jumps: 0 }, pos(14.1, false), EVENTS).stop).toBe(true)
    expect(crowdSoundStep({ time: 8.9, clip: 0, jumps: 0 }, pos(9, true, 1), EVENTS).stop).toBe(true)
  })
})

describe('narration is primary', () => {
  it('7: narration ducks the crowd to a fifth; audio description on but quiet keeps it lower', () => {
    expect(duckFactor(true, false)).toBe(NARRATION_DUCK)
    expect(duckFactor(true, true)).toBe(NARRATION_DUCK)
    expect(NARRATION_DUCK).toBeGreaterThanOrEqual(0.15); expect(NARRATION_DUCK).toBeLessThanOrEqual(0.25)
    expect(duckFactor(false, true)).toBe(DESCRIPTION_DUCK)
    expect(duckFactor(false, false)).toBe(1)
    const { ctx, p } = player()
    void p.prepare()
    p.setDuck(duckFactor(true, false))
    expect(p.level).toBeCloseTo(CROWD_LEVEL * NARRATION_DUCK, 6)
    expect(ctx.gains[0].targets.at(-1)).toBeCloseTo(CROWD_LEVEL * NARRATION_DUCK, 6)
    expect(CROWD_LEVEL).toBeLessThanOrEqual(0.5)                        // a goal never sits louder than speech
    expect(viewSource).toContain('speech.subscribe(state => crowdPlayer.current?.setDuck(duckFactor(state.speaking, audioDescription)))')
  })

  it('8: crowd audio never interrupts, delays or cancels narration', async () => {
    let cancelled = 0
    const spoken: UtteranceLike[] = []
    const synth: SynthLike = { speak: u => { spoken.push(u) }, cancel: () => { cancelled++ }, pause() {}, resume() {}, getVoices: () => [{ name: 'en', lang: 'en-GB' }] }
    const speech = new SpeechQueue({ synth, now: () => 0, makeUtterance: text => ({ text, lang: '', rate: 1, volume: 1, voice: null, onend: null, onerror: null }) })
    speech.setTimeline('replay', 14, true)
    speech.say({ text: 'Blackwood scores!', kind: 'replay', source: 'replay', priority: 2, at: 14, lang: 'en-GB' })
    const { p } = player()
    await p.prepare()
    p.play('goal', 1); p.setDuck(NARRATION_DUCK); p.stop(); p.play('chance', 0.5)
    expect(cancelled).toBe(0)
    expect(spoken.map(u => u.text)).toEqual(['Blackwood scores!'])
    expect(speech.getState().speaking).toBe(true)
    expect(audioSource).not.toMatch(/speechSynthesis|SpeechQueue|\.say\(|clearKind|cancel\(/)
  })
})

describe('the viewer stays in control', () => {
  it('9, 10: Crowd sound Off, or the crowd itself Off, plays nothing', () => {
    expect(crowdSoundAllowed(true, true)).toBe(true)
    expect(crowdSoundAllowed(true, false)).toBe(false)
    expect(crowdSoundAllowed(false, true)).toBe(false)
    expect(viewSource).toContain("const soundAllowed = crowdSoundAllowed(crowdTier !== 'off', crowdSound)")
    expect(viewSource).toContain('if (step.play && soundAllowed) crowdPlayer.current?.play(step.play.clip, step.play.gain)')
    expect(viewSource).toContain('else crowdPlayer.current?.stop()')                                 // switching off stops at once
    expect(viewSource).toMatch(/aria-pressed=\{crowdSound\} onClick=\{toggleCrowdSound\}/)              // a native, keyboard-usable button
  })

  it('11: reduced motion does not mute the crowd', () => {
    expect(crowdSoundAllowed.length).toBe(2)
    const block = viewSource.slice(viewSource.indexOf('// ---------------------------------------------------------------- crowd sound'), viewSource.indexOf('const toggleCrowdSound'))
    expect(block).not.toMatch(/reducedMotion/)
  })

  it('12: the control is labelled in all six languages', () => {
    for (const lang of LANGUAGE_CODES) {
      const r = t(lang).replay
      expect(r.crowdSound.length).toBeGreaterThan(3)
      expect(r.crowdSoundNeedsCrowd.length).toBeGreaterThan(5)
      if (lang !== 'en') { expect(r.crowdSound).not.toBe(t('en').replay.crowdSound); expect(r.crowdSoundNeedsCrowd).not.toBe(t('en').replay.crowdSoundNeedsCrowd) }
    }
  })
})

describe('cheap, isolated and safe', () => {
  it('loads the three clips once and reuses them: no new buffers or gains per event', async () => {
    const { ctx, p, loads } = player()
    await p.prepare(); await p.prepare()
    for (let i = 0; i < 20; i++) p.play(i % 2 ? 'goal' : 'chance', 1)
    expect(loads.sort()).toEqual(['crowd-chance.wav', 'crowd-goal.wav', 'crowd-murmur.wav'])
    expect(ctx.decodes).toBe(3)
    expect(ctx.gains).toHaveLength(3)                                   // master + two voices, made once
    expect(ctx.sources.filter(s => s.started && !s.stopped)).toHaveLength(1)   // never more than one clip at full level
  })

  it('13: never alters evidence, context or counts', () => {
    const events = Object.freeze(EVENTS.map(e => Object.freeze({ ...e })))
    const before = JSON.stringify(events)
    let prev = { time: -1, clip: -1, jumps: 0 }
    for (const time of along(0, 19, 0.05)) { crowdSoundStep(prev, pos(time), events); prev = { time, clip: 0, jumps: 0 } }
    expect(JSON.stringify(events)).toBe(before)
    expect(audioSource).not.toMatch(/evidenceEventIds|contextEventIds|supportingEventIds|isEvidence|isContext/)
  })

  it('14: makes no Azure, Foundry or API call - it only loads its own three files', () => {
    expect(audioSource).not.toMatch(/azure|foundry|\/api\/|fetch\(/i)
    const block = viewSource.slice(viewSource.indexOf('// ---------------------------------------------------------------- crowd sound'), viewSource.indexOf('const toggleCrowdSound'))
    expect(block.match(/fetch\(/g)).toHaveLength(1)
    expect(block).toContain('fetch(crowdClipUrl(import.meta.env.BASE_URL, file))')
  })

  it('loads each clip from a content-versioned URL, so a replaced clip is never served from an old browser cache', async () => {
    const sha = async (dataUrl: string) => {
      const bytes = Uint8Array.from(atob(dataUrl.split(',')[1]), c => c.charCodeAt(0))
      return Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes)), b => b.toString(16).padStart(2, '0')).join('').slice(0, 12)
    }
    expect(crowdClipUrl('/', 'crowd-goal.wav')).toBe(`/audio/crowd-goal.wav?v=${await sha(goalWav)}`)
    expect(crowdClipUrl('/', 'crowd-chance.wav')).toBe(`/audio/crowd-chance.wav?v=${await sha(chanceWav)}`)
    expect(crowdClipUrl('/MatchShift/', 'crowd-murmur.wav')).toBe(`/MatchShift/audio/crowd-murmur.wav?v=${await sha(murmurWav)}`)
    expect(crowdClipUrl('/', 'crowd-goal.wav', { 'crowd-goal.wav': 'aaa' })).not.toBe(crowdClipUrl('/', 'crowd-goal.wav', { 'crowd-goal.wav': 'bbb' }))
    expect(crowdClipUrl('/', 'crowd-goal.wav', {})).toBe('/audio/crowd-goal.wav')        // no version known: plain URL still works
  })

  it('the clips are short, small browser-ready WAVs (mono or stereo) that start and end in silence', () => {
    const wav = (url: string) => {
      const bin = atob(url.split(',')[1])
      const bytes = Uint8Array.from(bin, c => c.charCodeAt(0))
      const v = new DataView(bytes.buffer)
      // Walk the RIFF chunks: files may carry extra chunks (LIST, etc.) before 'data'.
      const tag = (at: number) => String.fromCharCode(...bytes.slice(at, at + 4))
      let channels = 0, rate = 0, bits = 0, dataAt = -1, dataSize = 0
      for (let at = 12; at + 8 <= bytes.length;) {
        const id = tag(at), size = v.getUint32(at + 4, true)
        if (id === 'fmt ') { channels = v.getUint16(at + 10, true); rate = v.getUint32(at + 12, true); bits = v.getUint16(at + 22, true) }
        if (id === 'data') { dataAt = at + 8; dataSize = size; break }
        at += 8 + size + (size % 2)
      }
      expect(dataAt).toBeGreaterThan(0)
      const samples = new Int16Array(bytes.buffer.slice(dataAt, dataAt + dataSize - (dataSize % 2)))
      const edge = 20 * channels
      const abs = (from: number, to: number) => { let m = 0; for (let i = from; i < to; i++) m = Math.max(m, Math.abs(samples[i]) / 32768); return m }
      return { channels, rate, bits, seconds: samples.length / channels / rate, kb: bytes.length / 1024, start: abs(0, edge), end: abs(samples.length - edge, samples.length), peak: abs(0, samples.length) }
    }
    const goal = wav(goalWav), chance = wav(chanceWav), murmur = wav(murmurWav)
    for (const c of [goal, chance, murmur]) {
      expect([1, 2]).toContain(c.channels)                               // mono or stereo
      expect([22050, 44100, 48000]).toContain(c.rate)
      expect(c.bits).toBe(16)                                            // plain 16-bit PCM, playable everywhere
      expect(c.start).toBeLessThan(0.01); expect(c.end).toBeLessThan(0.01)   // faded in and out: no clicks
      expect(c.peak).toBeLessThan(0.95)                                  // headroom, no clipping
      expect(c.kb).toBeLessThan(600)                                     // small enough to preload when the replay opens
    }
    expect(goal.seconds).toBeGreaterThanOrEqual(2.5); expect(goal.seconds).toBeLessThanOrEqual(3.0)
    expect(chance.seconds).toBeLessThan(goal.seconds); expect(murmur.seconds).toBeLessThan(chance.seconds)
  })
})
