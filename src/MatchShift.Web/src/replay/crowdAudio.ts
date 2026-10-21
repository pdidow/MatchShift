// Optional crowd atmosphere for the Evidence Replay. Decorative only: it carries no match facts (every event it follows is
// also on screen and, with audio description, narrated), never touches evidence, and never goes through speech - so it
// can never delay, interrupt or replace narration. It ducks under narration instead.
//
// Three short original clips (tools/crowd-audio/generate_crowd_audio.py), loaded once and reused. The replay clock
// decides when a clip plays; nothing is scheduled on independent timers.

export type CrowdClip = 'goal' | 'chance' | 'murmur'

/**
 * Which clip an event label plays and at what share of the crowd-effect level. Mirrors the visual hierarchy: a goal is the
 * full roar, a shot a smaller "ooh" (40-60%), a won-back ball high up a faint stir; everything else is silent.
 */
export const CROWD_SOUNDS: Record<string, { clip: CrowdClip; gain: number }> = {
  GOAL: { clip: 'goal', gain: 1 },
  SHOT_ON_TARGET: { clip: 'chance', gain: 0.6 },
  SHOT_OFF_TARGET: { clip: 'chance', gain: 0.5 },
  SHOT_BLOCKED: { clip: 'chance', gain: 0.45 },
  HIGH_PRESS_INTERCEPTION: { clip: 'murmur', gain: 0.2 },
  HIGH_PRESSURE_RECOVERY: { clip: 'murmur', gain: 0.18 },
}

/** The crowd-effect level at 100% (a goal). Kept well below speech so even a goal never competes with narration. */
export const CROWD_LEVEL = 0.35
/** While narration is speaking the crowd drops to 20%; with audio description on but quiet, to 60%. */
export const NARRATION_DUCK = 0.2
export const DESCRIPTION_DUCK = 0.6

export const duckFactor = (narrationSpeaking: boolean, audioDescription: boolean) =>
  narrationSpeaking ? NARRATION_DUCK : audioDescription ? DESCRIPTION_DUCK : 1

/** Crowd sound plays only when the viewer wants it AND the crowd itself is shown. Reduced motion is not a reason to mute. */
export const crowdSoundAllowed = (crowdVisible: boolean, soundOn: boolean) => crowdVisible && soundOn

/** `jumps` counts seeks and restarts (from the replay scene); any change means the viewer jumped, however small. */
export interface ReplayPosition { time: number; clip: number; playing: boolean; introActive: boolean; jumps: number }

/**
 * What the crowd audio does for one replay tick, from the previous and current replay position (pure).
 *  - Paused, intro, a seek (backwards, or a jump forward), a restart or a clip change: stop the current clip; a seek never
 *    plays the events it skipped.
 *  - Normal playback: play the strongest sound among the events the replay clock has just crossed - so each event sounds
 *    once per pass, and again only after seeking back before it or restarting.
 */
export function crowdSoundStep(prev: Pick<ReplayPosition, 'time' | 'clip' | 'jumps'>, now: ReplayPosition,
  events: readonly { label: string; at: number }[]): { stop: boolean; play: { clip: CrowdClip; gain: number; label: string } | null } {
  const jumped = now.jumps !== prev.jumps || now.time < prev.time - 1e-3 || now.time - prev.time > 1.5
  const clipChanged = prev.clip >= 0 && now.clip !== prev.clip
  if (!now.playing || now.introActive || jumped) return { stop: true, play: null }
  let play: { clip: CrowdClip; gain: number; label: string } | null = null
  for (const e of events) {
    if (e.at <= prev.time || e.at > now.time) continue
    const sound = CROWD_SOUNDS[e.label]
    if (sound && (!play || sound.gain > play.gain)) play = { ...sound, label: e.label }
  }
  return { stop: clipChanged || play !== null, play }
}

// ---------------------------------------------------------------- player

/** The few Web Audio pieces the player needs (a fake can stand in for tests). */
export interface AudioLike {
  readonly currentTime: number
  readonly state: string
  readonly destination: unknown
  resume(): Promise<void>
  createGain(): GainLike
  createBufferSource(): SourceLike
  decodeAudioData(data: ArrayBuffer): Promise<unknown>
}
export interface GainLike { gain: { value: number; setTargetAtTime(v: number, t: number, c: number): void }; connect(n: unknown): unknown }
export interface SourceLike { buffer: unknown; connect(n: unknown): unknown; start(t?: number): void; stop(t?: number): void; onended: (() => void) | null }

export const CROWD_CLIP_FILES: Record<CrowdClip, string> = { goal: 'crowd-goal.wav', chance: 'crowd-chance.wav', murmur: 'crowd-murmur.wav' }

/** Content hashes of the clips, injected at build time (vite.config.ts). */
declare const __CROWD_AUDIO_VERSIONS__: Record<string, string> | undefined

/**
 * Where a clip is loaded from: its stable file name plus a content-hash version, so a replaced clip always gets a new
 * URL and a browser can never play a cached older copy.
 */
export function crowdClipUrl(base: string, file: string, versions: Record<string, string> | undefined =
  typeof __CROWD_AUDIO_VERSIONS__ === 'undefined' ? undefined : __CROWD_AUDIO_VERSIONS__) {
  const v = versions?.[file]
  return `${base}audio/${file}${v ? `?v=${v}` : ''}`
}

/**
 * Plays crowd clips through one small graph: two reusable voice gains (so a new clip can cross-fade over the last) into
 * one master gain that carries the level and the narration duck. Clips are fetched and decoded once, before any replay
 * event can need them. A source node per play is the only per-event object (Web Audio sources are one-shot by design).
 */
export class CrowdSoundPlayer {
  private readonly makeContext: () => AudioLike | null
  private readonly load: (file: string) => Promise<ArrayBuffer>
  private ctx: AudioLike | null = null
  private master: GainLike | null = null
  private voices: GainLike[] = []
  private next = 0
  private buffers = new Map<CrowdClip, unknown>()
  private loading: Promise<void> | null = null
  private current: { source: SourceLike; voice: GainLike } | null = null
  private duck = 1
  decodes = 0

  constructor(options: { makeContext: () => AudioLike | null; load: (file: string) => Promise<ArrayBuffer> }) {
    this.makeContext = options.makeContext
    this.load = options.load
  }

  /** Create the graph and load the clips once (call when the replay opens with crowd sound allowed). */
  prepare(): Promise<void> {
    if (this.loading) return this.loading
    this.ctx ??= this.makeContext()
    const ctx = this.ctx
    if (!ctx) return (this.loading = Promise.resolve())
    if (ctx.state === 'suspended') void ctx.resume().catch(() => undefined)
    this.master = ctx.createGain()
    this.master.gain.value = CROWD_LEVEL * this.duck
    this.master.connect(ctx.destination)
    this.voices = [ctx.createGain(), ctx.createGain()]
    for (const v of this.voices) { v.gain.value = 0; v.connect(this.master) }
    this.loading = Promise.all((Object.keys(CROWD_CLIP_FILES) as CrowdClip[]).map(async clip => {
      try {
        const buffer = await ctx.decodeAudioData(await this.load(CROWD_CLIP_FILES[clip]))
        this.decodes++
        this.buffers.set(clip, buffer)
      } catch { /* a clip that cannot load simply stays silent */ }
    })).then(() => undefined)
    return this.loading
  }

  /** Play a clip at a share of the crowd level; any clip already playing fades out underneath it. */
  play(clip: CrowdClip, gain: number) {
    const ctx = this.ctx, buffer = this.buffers.get(clip)
    if (!ctx || !buffer || this.voices.length === 0) return
    this.stop()
    const voice = this.voices[this.next]
    this.next = (this.next + 1) % this.voices.length
    const source = ctx.createBufferSource()
    source.buffer = buffer
    source.connect(voice)
    voice.gain.setTargetAtTime(Math.max(0, Math.min(1, gain)), ctx.currentTime, 0.01)
    source.onended = () => { if (this.current?.source === source) this.current = null }
    source.start()
    this.current = { source, voice }
  }

  /** Fade the current clip out quickly (pause, seek, restart, clip change, crowd or sound switched off, replay closed). */
  stop() {
    const ctx = this.ctx, cur = this.current
    if (!ctx || !cur) return
    this.current = null
    cur.voice.gain.setTargetAtTime(0, ctx.currentTime, 0.04)
    try { cur.source.stop(ctx.currentTime + 0.2) } catch { /* already stopped */ }
  }

  /** Narration state changed: duck (or restore) the whole crowd layer smoothly. Never touches speech. */
  setDuck(factor: number) {
    this.duck = factor
    if (this.ctx && this.master) this.master.gain.setTargetAtTime(CROWD_LEVEL * factor, this.ctx.currentTime, 0.08)
  }

  get playing() { return this.current !== null }
  get level() { return CROWD_LEVEL * this.duck }
}

/** Remembered Crowd sound choice; default On. Stored like the crowd's own On/Off. Storage may be unavailable. */
export const CROWD_SOUND_KEY = 'matchshift.replay.crowdSound'

export function loadCrowdSound(): boolean {
  try { return localStorage.getItem(CROWD_SOUND_KEY) !== 'off' } catch { return true }
}

export function saveCrowdSound(on: boolean) {
  try { localStorage.setItem(CROWD_SOUND_KEY, on ? 'on' : 'off') } catch { /* storage unavailable: lasts this session */ }
}

/** The browser's own Web Audio context, or null where there is none. Created on first use (after a user gesture). */
export function browserAudioContext(): AudioLike | null {
  try {
    const Ctx = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
    return Ctx ? (new Ctx() as unknown as AudioLike) : null
  } catch { return null }
}
