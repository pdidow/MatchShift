// The single MatchShift speech coordinator: browser voices first, bounded server Speech fallback.
//  - one voice at a time, highest priority first
//  - TIMELINE-AWARE: a cue names the second it describes on its own clock (match or replay); once that clock has moved
//    past the cue's relevance window the cue is dropped, never spoken late. The timeline never waits for speech.
//  - BOUNDED: at most one cue waiting per source (plus the one speaking); a newer or more important cue replaces it
//  - AUDIO FOCUS: Evidence Replay takes exclusive focus; match narration is silenced and cleared until focus is released
//  - de-duplicates by key (the same insight is never spoken twice)
//  - critical messages (and the viewer's own requests) interrupt lower-ranked speech; interrupted speech is not resumed
//  - goals and match boundaries (half-time, full-time) are never silently lost: they outrank every other cue and wait
//    a short grace period behind a sentence that is already speaking
//  - mute / pause / resume / repeat last; the speech rate is the viewer's setting and is never scaled by playback speed
//  - every message is also kept as text (captions + transcript), even when muted, silenced or no voice exists

import { browserCloudSpeech, type CloudSpeech } from './cloudSpeech'
import { languageOf, localeOf as speechLocale } from '../languages'

export type Priority = 0 | 1 | 2 // 0 = low (detail, replay narration), 1 = normal, 2 = critical (goals, dangerous moments)

/** Who a cue belongs to. "ui" is something the viewer asked for (read aloud, test voice) and is never timeline-bound. */
export type SpeechSource = 'match' | 'replay' | 'ui'

/** Which timeline owns MatchShift narration. Evidence Replay is exclusive while it is open. */
export type AudioFocus = 'match' | 'replay'

/** Seconds of its own timeline a cue stays relevant, by source and priority (match: match seconds; replay: replay seconds). */
export const RELEVANCE: Record<'match' | 'replay', Record<Priority, number>> = {
  // At the default 2x one real second is one match minute: 45 / 90 / 180 match seconds are 0.75 / 1.5 / 3 s of viewing.
  match: { 0: 45, 1: 90, 2: 180 },
  replay: { 0: 1.5, 1: 3, 2: 4 },
}

/**
 * What a cue is, when that matters more than its priority. Decided from match event semantics, never by AI:
 *   goal / boundary (half-time, full-time)  >  major (a verified, high-severity insight)  >  other critical  >  normal  >  low
 */
export type CueClass = 'goal' | 'boundary' | 'major'

/** Rank used for interruption and for which cue waits. Unclassified cues rank by priority (0-2). */
export const cueRank = (q: { priority?: Priority; cueClass?: CueClass }): number =>
  q.cueClass === 'goal' || q.cueClass === 'boundary' ? 4 : q.cueClass === 'major' ? 3 : (q.priority ?? 1)

/**
 * Goals and boundaries are sticky: real milliseconds they may still wait behind a sentence that is already speaking once
 * their timeline window has passed (long enough for the longest single sentence, about 12.5 s detailed at 1x).
 */
export const STICKY_GRACE_MS = 15_000

const isSticky = (q: { cueClass?: CueClass }) => q.cueClass === 'goal' || q.cueClass === 'boundary'

/** Is a cue at timeline second `at` still current when the clock reads `clock`? A cue "from the future" (after a seek back) is not. */
export function isCurrent(at: number | undefined, clock: number | undefined, validFor: number): boolean {
  if (at === undefined || clock === undefined) return true
  const age = clock - at
  return age >= -1 && age <= validFor
}

export interface SpeechMessage {
  text: string
  priority?: Priority
  /** Category, e.g. "insight", "status", "replay", "why", "describe". */
  kind: string
  /** Shown in the transcript, e.g. an insight type label. */
  label?: string
  /** De-duplication key; a key is spoken at most once. */
  key?: string
  /** Match clock for the transcript, e.g. "63:10". */
  clock?: string
  lang: string
  /** Override the default time-to-live (a wall-clock backstop only). */
  ttlMs?: number
  /** Owner. Defaults: "replay" for kind "replay", "ui" for the viewer's own repeatable requests, otherwise "match". */
  source?: SpeechSource
  /** The timeline second this cue describes, on its source's clock. */
  at?: number
  /** Override the relevance window (timeline seconds). */
  validFor?: number
  /** Semantic class (goal, match boundary, major insight); see cueRank. */
  cueClass?: CueClass
}

export interface TranscriptEntry {
  id: number
  at: number
  clock?: string
  kind: string
  label?: string
  text: string
  lang: string
  spoken: boolean
}

export interface SpeechState {
  supported: boolean
  speaking: boolean
  paused: boolean
  muted: boolean
  current: TranscriptEntry | null
  transcript: TranscriptEntry[]
  /** Missing local voice in environments without a cloud transport. */
  missingVoice: string | null
  /** All available narration paths failed; the transcript remains available. */
  error: string | null
}

// Minimal structural types so tests can provide a fake synthesiser.
export interface VoiceLike { lang: string; name: string; default?: boolean }
export interface UtteranceLike {
  text: string
  lang: string
  rate: number
  volume: number
  voice: VoiceLike | null
  onstart?: (() => void) | null
  onend: (() => void) | null
  onerror: ((event?: { error?: string }) => void) | null
}
export interface SynthLike {
  speak(u: UtteranceLike): void
  cancel(): void
  pause(): void
  resume(): void
  getVoices(): VoiceLike[]
  addEventListener?(type: 'voiceschanged', listener: () => void): void
  removeEventListener?(type: 'voiceschanged', listener: () => void): void
}

const baseOf = (lang: string) => lang.toLowerCase().replace('_', '-').split('-')[0]
const localeOf = (lang: string) => lang.toLowerCase().replace('_', '-')

/**
 * Picks the voice for a BCP-47 locale: an exact locale match, then any voice of the same base language (the platform
 * default first, then a local voice), else null. The queue must not speak without a compatible voice: browsers can
 * ignore utterance.lang and fall back to an English default. Voices are the viewer's browser/OS voices.
 */
export function chooseVoice(voices: VoiceLike[], lang: string, excluded: readonly string[] = [], preferredName?: string): VoiceLike | null {
  const usable = voices.filter(v => !excluded.includes(`${localeOf(v.lang)}:${v.name}`))
  const french = baseOf(lang) === 'fr'
  const preferredLocale = french ? (usable.some(v => localeOf(v.lang) === 'fr-ca') ? 'fr-ca' : 'fr-fr') : localeOf(lang)
  const exact = usable.filter(v => localeOf(v.lang) === preferredLocale)
  const same = exact.length > 0 ? exact : usable.filter(v => baseOf(v.lang) === baseOf(lang))
  if (same.length === 0) return null
  return same.find(v => v.name === preferredName) ?? same.find(v => v.default) ?? same.find(v => (v as { localService?: boolean }).localService) ?? same[0]
}

interface Queued extends SpeechMessage { priority: Priority; source: SpeechSource; createdAt: number; entry: TranscriptEntry; merged?: TranscriptEntry[]; attemptedVoices?: string[] }

const DEFAULT_TTL: Record<Priority, number> = { 0: 15_000, 1: 40_000, 2: 120_000 }
const MAX_TRANSCRIPT = 200

export class SpeechQueue {
  private readonly synth: SynthLike | null
  private readonly makeUtterance: (text: string) => UtteranceLike
  private readonly now: () => number
  private queue: Queued[] = []
  private currentItem: Queued | null = null
  private cloud: CloudSpeech | null
  private cloudAbort: AbortController | null = null
  private startTimer: ReturnType<typeof setTimeout> | undefined
  private voiceTimer: ReturnType<typeof setTimeout> | undefined
  private voicesWaited = false
  private token = 0
  private spokenKeys = new Set<string>()
  private lastSpoken: Queued | null = null
  private listeners = new Set<(s: SpeechState) => void>()
  private nextId = 1
  private state: SpeechState

  rate = 1
  volume = 1

  private focus: AudioFocus = 'match'
  private readonly clocks: Record<'match' | 'replay', { time?: number; playing: boolean }> = {
    match: { playing: true },
    replay: { playing: true },
  }

  constructor(options?: { synth?: SynthLike | null; makeUtterance?: (text: string) => UtteranceLike; now?: () => number; cloud?: CloudSpeech | null }) {
    const browserSynth = typeof window !== 'undefined' && 'speechSynthesis' in window ? (window.speechSynthesis as unknown as SynthLike) : null
    this.synth = options?.synth !== undefined ? options.synth : browserSynth
    this.makeUtterance = options?.makeUtterance ?? ((text) => new SpeechSynthesisUtterance(text) as unknown as UtteranceLike)
    this.cloud = options?.cloud !== undefined ? options.cloud : (!options && typeof window !== 'undefined' && 'AudioContext' in window ? browserCloudSpeech() : null)
    this.now = options?.now ?? (() => Date.now())
    this.state = { supported: !!this.synth || !!this.cloud, speaking: false, paused: false, muted: false, current: null, transcript: [], missingVoice: null, error: null }
    // Voices often load after the page (getVoices() can be empty at first). One listener refreshes the cached list and
    // clears a "missing voice" notice that was only due to voices not having loaded yet.
    this.synth?.addEventListener?.('voiceschanged', this.onVoicesChanged)
  }

  private failedVoices = new Set<string>()
  private voices: VoiceLike[] = []
  private language: string | null = null
  private preferredVoices: Record<string, string> = {}

  private readonly onVoicesChanged = () => {
    this.voices = this.synth?.getVoices() ?? []
    if (this.state.missingVoice && chooseVoice(this.voices, this.state.missingVoice)) this.update({ missingVoice: null })
    else this.update({})
    this.pump() // Late voices may speak only cues that are still relevant to the current timeline.
  }

  /** Browser-local voice preference; only compatible voices can ever be selected. */
  setPreferredVoices(voices: Record<string, string>) { this.preferredVoices = voices }

  compatibleVoices(lang: string): VoiceLike[] {
    this.voices = this.synth?.getVoices() ?? []
    const exact = this.voices.filter(v => localeOf(v.lang) === localeOf(lang))
    return exact.length ? exact : this.voices.filter(v => baseOf(v.lang) === baseOf(lang))
  }

  /** Detach from the browser synthesiser (tests, or if the queue is ever replaced). */
  dispose() {
    this.synth?.removeEventListener?.('voiceschanged', this.onVoicesChanged)
    this.stopAll()
  }

  /**
   * The viewer's language changed: drop queued speech in any other language and stop it if it is playing,
   * so narration in the old language never continues after a switch. Messages in the new language are kept.
   */
  setLanguage(lang: string) {
    if (!languageOf(lang)) return
    lang = speechLocale(lang)
    if (this.language === lang) return
    this.language = lang
    const base = baseOf(lang)
    this.queue = this.queue.filter(q => baseOf(q.lang) === base)
    if (this.currentItem && baseOf(this.currentItem.lang) !== base) this.cancelCurrent()
    this.lastSpoken = null
    if (this.state.missingVoice && baseOf(this.state.missingVoice) !== base) this.update({ missingVoice: null })
    if (this.state.error) this.update({ error: null })
  }

  subscribe(listener: (s: SpeechState) => void) {
    this.listeners.add(listener)
    listener(this.state)
    return () => { this.listeners.delete(listener) }
  }

  getState() { return this.state }

  getFocus() { return this.focus }

  /** Cues waiting (not counting the one speaking). Bounded: at most one per source. */
  get pending() { return this.queue.length }

  get canRepeat() { return this.lastSpoken !== null }

  /**
   * The current position of a timeline. Waiting cues of that source that are no longer current are dropped at once. A
   * sentence already speaking is allowed to finish (cutting every sentence mid-word would be worse); a seek or restart
   * makes it stale, and the caller then clears it with clearSource / clearKind. While a timeline is paused none of its
   * waiting cues start.
   */
  setTimeline(source: 'match' | 'replay', time: number, playing: boolean) {
    const clock = this.clocks[source]
    const wasPlaying = clock.playing
    clock.time = time
    clock.playing = playing
    this.queue = this.queue.filter(q => q.source !== source || this.isRelevant(q))
    if (playing && !wasPlaying) this.pump()
  }

  /** Drop everything waiting for a source and stop it if it is speaking (a seek, reset or new match made it stale). */
  clearSource(source: SpeechSource) {
    this.queue = this.queue.filter(q => q.source !== source)
    if (this.currentItem?.source === source) this.cancelCurrent()
  }

  /**
   * Evidence Replay opens and becomes the only MatchShift narration owner: match narration speaking now is stopped and
   * everything queued for the match (or for the page) is discarded - it describes a moment the viewer has left.
   */
  acquireReplayFocus() {
    this.focus = 'replay'
    this.queue = this.queue.filter(q => q.source === 'replay')
    if (this.currentItem && this.currentItem.source !== 'replay') this.cancelCurrent()
    this.clocks.replay = { playing: true }
  }

  /** Evidence Replay closes: replay speech stops; earlier match narration is NOT resumed - only new match cues speak. */
  releaseReplayFocus() {
    this.focus = 'match'
    this.queue = this.queue.filter(q => q.source !== 'replay')
    if (this.currentItem?.source === 'replay') this.cancelCurrent()
    this.clocks.replay = { playing: true }
  }

  private isRelevant(q: Pick<Queued, 'source' | 'at' | 'validFor' | 'priority' | 'cueClass' | 'createdAt'>, arriving = false) {
    if (q.source === 'ui') return true
    const clock = this.clocks[q.source].time
    if (isCurrent(q.at, clock, q.validFor ?? RELEVANCE[q.source][q.priority])) return true
    // A goal or boundary that was current when raised may still be spoken for a short, bounded grace period (it waited
    // behind a sentence already speaking). Never one from the future, and ordinary cues get no grace.
    return !arriving && isSticky(q) && this.now() - q.createdAt <= STICKY_GRACE_MS && isCurrent(q.at, clock, Number.MAX_SAFE_INTEGER)
  }

  /** Queue a message. Returns false if it was a duplicate. */
  say(message: SpeechMessage, options?: { allowRepeat?: boolean }): boolean {
    // Never relabel stale English text as French, including async callbacks and transcript replay.
    if (!languageOf(message.lang) || (this.language && baseOf(message.lang) !== baseOf(this.language))) return false
    message = { ...message, lang: this.language ?? speechLocale(message.lang) }
    const source: SpeechSource = message.source ?? (message.kind === 'replay' ? 'replay' : options?.allowRepeat ? 'ui' : 'match')
    if (message.key && !options?.allowRepeat) {
      if (this.spokenKeys.has(message.key)) return false
      this.spokenKeys.add(message.key)
    }
    const priority = message.priority ?? 1
    const entry: TranscriptEntry = {
      id: this.nextId++, at: this.now(), clock: message.clock, kind: message.kind, label: message.label,
      text: message.text, lang: message.lang, spoken: false,
    }
    this.pushTranscript(entry)
    if ((!this.synth && !this.cloud) || this.state.muted) { this.emit(); return true }
    // Audio focus: while Evidence Replay is open, match narration is text only and never queues behind the replay.
    if (source === 'match' && this.focus === 'replay') { this.emit(); return true }

    // Missing or still-loading voices are text-only until a matching voice is available. Never use the OS default.
    const voice = this.voiceFor(message.lang)
    if (!voice && !this.cloud) this.update({ missingVoice: message.lang })
    else if (this.state.missingVoice === message.lang) this.update({ missingVoice: null })

    const item: Queued = { ...message, priority, source, createdAt: this.now(), entry }
    if (!this.isRelevant(item, true)) { this.emit(); return true }          // already stale: text only, never spoken late

    // Bounded: at most one cue waiting per source. A newer cue replaces the waiting one unless that one ranks higher.
    // The one exception keeps goals and boundaries from being lost: a source may hold one waiting goal AND one waiting
    // boundary. A newer boundary replaces a waiting one (full time supersedes half-time); a newer goal is MERGED into the
    // waiting goal, so both are spoken in one cue and no goal is lost. Ordinary cues waiting behind them are dropped.
    const rank = cueRank(item)
    const waiting = this.queue.filter(q => q.source === source)
    if (isSticky(item)) {
      const waitingGoal = item.cueClass === 'goal' ? waiting.find(q => q.cueClass === 'goal' && q.lang === item.lang) : undefined
      if (waitingGoal) { item.text = `${waitingGoal.text} ${item.text}`; item.merged = [...(waitingGoal.merged ?? []), waitingGoal.entry] }
      this.queue = this.queue.filter(q => q.source !== source || (isSticky(q) && q.cueClass !== item.cueClass))
    } else {
      if (waiting.some(q => cueRank(q) > rank)) { this.emit(); return true }
      this.queue = this.queue.filter(q => q.source !== source)
    }
    // A goal interrupts anything below a goal. A boundary interrupts only non-critical speech and otherwise waits (with
    // its grace) for the sentence to finish, so half-time never cuts a goal. Other critical news interrupts lower ranks.
    const current = this.currentItem
    const interrupts = !!current && (
      (item.cueClass === 'goal' ? rank > cueRank(current)
        : item.cueClass === 'boundary' ? current.priority < 2 && rank > cueRank(current)
        : priority === 2 && rank > cueRank(current)) ||
      (source === 'ui' && current.source !== 'ui'))                       // the viewer's own request is answered now
    if (interrupts) {
      // The interrupted message is dropped (it would be stale), never resumed.
      this.queue.unshift(item)
      this.cancelCurrent()
    } else {
      this.queue.push(item)
    }
    this.pump()
    return true
  }

  /** Speak the last spoken message again (ignores de-duplication). */
  repeatLast() {
    if (!this.lastSpoken) return false
    const { entry: _entry, createdAt: _c, ...msg } = this.lastSpoken
    void _entry; void _c
    return this.say({ ...msg, key: undefined, priority: 1 }, { allowRepeat: true })
  }

  /** Speak a transcript entry again. */
  replay(entry: TranscriptEntry) {
    return this.say({ text: entry.text, kind: entry.kind, label: entry.label, clock: entry.clock, lang: entry.lang, priority: 1 }, { allowRepeat: true })
  }

  setMuted(muted: boolean) {
    if (muted) { this.queue = []; this.cancelCurrent() }
    this.update({ muted })
  }

  pause() {
    this.synth?.pause()
    this.cloud?.pause()
    this.update({ paused: true })
  }

  resume() {
    this.synth?.resume()
    this.cloud?.resume()
    this.update({ paused: false })
    this.pump()
  }

  /** Drop queued and current messages of one category (e.g. replay narration after a seek). */
  clearKind(kind: string) {
    this.queue = this.queue.filter(q => q.kind !== kind)
    if (this.currentItem?.kind === kind) this.cancelCurrent()
  }

  /** Stop everything (e.g. when all speech features are switched off). */
  stopAll() {
    this.queue = []
    this.cancelCurrent()
  }

  /** Forget de-duplication keys (e.g. after seeking back in the match). */
  resetKeys(prefix?: string) {
    if (!prefix) { this.spokenKeys.clear(); return }
    for (const k of [...this.spokenKeys]) if (k.startsWith(prefix)) this.spokenKeys.delete(k)
  }

  clearTranscript() { this.update({ transcript: [] }) }

  private cancelCurrent() {
    clearTimeout(this.startTimer)
    clearTimeout(this.voiceTimer)
    this.voiceTimer = undefined
    this.cloudAbort?.abort()
    this.cloudAbort = null
    this.token++
    this.currentItem = null
    this.synth?.cancel()
    this.update({ speaking: false, current: null })
  }

  private pump() {
    if ((!this.synth && !this.cloud) || this.currentItem || this.state.paused || this.state.muted) return
    const now = this.now()
    this.queue = this.queue.filter(q => now - q.createdAt <= (q.ttlMs ?? DEFAULT_TTL[q.priority]) && this.isRelevant(q)
      && !(q.source === 'match' && this.focus === 'replay'))
    // A paused timeline starts no new cues of its own (the viewer's own requests still speak).
    const eligible = (q: Queued) => q.source === 'ui' || this.clocks[q.source].playing
    // Highest rank first; FIFO within a rank.
    let bestIndex = -1
    for (let i = 0; i < this.queue.length; i++) {
      if (!eligible(this.queue[i])) continue
      if (bestIndex < 0 || cueRank(this.queue[i]) > cueRank(this.queue[bestIndex])) bestIndex = i
    }
    if (bestIndex < 0) return
    const item = this.queue[bestIndex]
    const voice = this.voiceFor(item.lang, item.attemptedVoices)
    if (!voice) {
      if (!this.cloud) { this.update({ missingVoice: item.lang }); return }
      if (this.synth && !this.voicesWaited) {
        if (!this.voiceTimer) this.voiceTimer = setTimeout(() => { this.voicesWaited = true; this.voiceTimer = undefined; this.pump() }, 600)
        return
      }
      this.queue.splice(bestIndex, 1)
      this.speakCloud(item)
      return
    }
    if (this.state.missingVoice) this.update({ missingVoice: null })
    this.queue.splice(bestIndex, 1)
    const u = this.makeUtterance(item.text)
    u.lang = item.lang
    u.rate = this.rate
    u.volume = this.volume
    u.voice = voice
    const token = ++this.token
    let started = false
    const markSpoken = () => {
      if (token !== this.token || started) return
      clearTimeout(this.startTimer)
      started = true
      item.entry.spoken = true
      for (const e of item.merged ?? []) e.spoken = true
      this.lastSpoken = item
      this.emit()
    }
    const done = () => {
      if (token !== this.token) return
      if (!started) markSpoken() // Some engines omit onstart but still report successful completion.
      clearTimeout(this.startTimer)
      this.token++
      this.currentItem = null
      this.update({ speaking: false, current: null })
      this.pump()
    }
    const failed = (event?: { error?: string }) => {
      if (token !== this.token) return
      this.token++
      clearTimeout(this.startTimer)
      const code = event?.error ?? 'synthesis-failed'
      if (this.cloud) {
        this.failedVoices.add(`${localeOf(voice.lang)}:${voice.name}`)
        this.synth?.cancel(); this.speakCloud(item); return
      }
      this.currentItem = null
      // Firefox and Windows can expose a voice that its engine cannot actually start. Try another voice of the SAME
      // language; never fall back to an English default for French (or another selected language).
      if (!started && ['voice-unavailable', 'language-unavailable', 'synthesis-failed'].includes(code)) {
        item.attemptedVoices = [...(item.attemptedVoices ?? []), `${localeOf(voice.lang)}:${voice.name}`]
        if (this.voiceFor(item.lang, item.attemptedVoices) && this.isRelevant(item)) {
          this.queue.unshift(item)
          this.update({ speaking: false, current: null })
          this.pump()
          return
        }
      }
      this.update({ speaking: false, current: null, error: code })
      this.pump()
    }
    u.onstart = markSpoken
    u.onend = done
    u.onerror = failed
    this.currentItem = item
    this.update({ speaking: true, current: item.entry, error: null })
    const checkStart = () => {
      if (token !== this.token || started) return
      if (this.state.paused) this.startTimer = setTimeout(checkStart, 2500)
      else failed({ error: 'synthesis-start-timeout' })
    }
    if (this.cloud) this.startTimer = setTimeout(checkStart, 2500)
    try { this.synth!.speak(u) } catch { failed({ error: 'synthesis-failed' }) }
  }

  private speakCloud(item: Queued) {
    const cloud = this.cloud
    if (!cloud) return
    const token = ++this.token
    const abort = new AbortController()
    this.cloudAbort = abort
    this.currentItem = item
    this.update({ speaking: true, current: item.entry, missingVoice: null, error: null })
    const timeout = setTimeout(() => abort.abort(), 60_000)
    cloud.play(item.text, item.lang, this.rate, this.volume, abort.signal, () => {
      if (token !== this.token) return
      item.entry.spoken = true
      for (const entry of item.merged ?? []) entry.spoken = true
      this.lastSpoken = item
      this.emit()
    }, () => token === this.token && this.isRelevant(item)).catch(error => {
      if (token !== this.token) return
      console.warn('MatchShift narration fallback failed', error instanceof Error ? error.message : 'speech-error')
      this.update({ error: 'synthesis-failed' })
    }).finally(() => {
      clearTimeout(timeout)
      if (token !== this.token) return
      if (abort.signal.aborted) this.update({ error: 'synthesis-failed' })
      this.cloudAbort = null
      this.currentItem = null
      this.update({ speaking: false, current: null })
      this.pump()
    })
  }

  /**
   * Refresh at dispatch as well as voiceschanged: some browsers change the list without delivering that event.
   */
  private voiceFor(lang: string, excluded: readonly string[] = []): VoiceLike | null {
    this.voices = this.synth?.getVoices() ?? []
    return chooseVoice(this.voices, lang, [...excluded, ...this.failedVoices], this.preferredVoices[baseOf(lang)])
  }

  private pushTranscript(entry: TranscriptEntry) {
    const transcript = [...this.state.transcript, entry]
    if (transcript.length > MAX_TRANSCRIPT) transcript.splice(0, transcript.length - MAX_TRANSCRIPT)
    this.state = { ...this.state, transcript }
  }

  private update(patch: Partial<SpeechState>) {
    this.state = { ...this.state, ...patch }
    this.emit()
  }

  private emit() {
    for (const l of this.listeners) l(this.state)
  }
}
