// Speech queue on top of the Web Speech API (window.speechSynthesis).
//  - one voice at a time, highest priority first
//  - de-duplicates by key (the same insight is never spoken twice)
//  - discards stale low-priority messages
//  - critical messages interrupt lower-priority speech
//  - mute / pause / resume / repeat last
//  - every message is also kept as text (captions + transcript), even when muted or no voice exists

export type Priority = 0 | 1 | 2 // 0 = low (detail, replay narration), 1 = normal, 2 = critical (goals, dangerous moments)

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
  /** Override the default time-to-live. */
  ttlMs?: number
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
  /** Language for which no installed voice was found (speech falls back to text). */
  missingVoice: string | null
}

// Minimal structural types so tests can provide a fake synthesiser.
export interface VoiceLike { lang: string; name: string; default?: boolean }
export interface UtteranceLike {
  text: string
  lang: string
  rate: number
  volume: number
  voice: VoiceLike | null
  onend: (() => void) | null
  onerror: (() => void) | null
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
 * default first, then a local voice), else null — the utterance still carries `lang`, so the browser chooses. A voice
 * of a different language is never chosen explicitly. Voices are the viewer's browser/OS voices, never the server's.
 */
export function chooseVoice(voices: VoiceLike[], lang: string): VoiceLike | null {
  const exact = voices.filter(v => localeOf(v.lang) === localeOf(lang))
  const same = exact.length > 0 ? exact : voices.filter(v => baseOf(v.lang) === baseOf(lang))
  if (same.length === 0) return null
  return same.find(v => v.default) ?? same.find(v => (v as { localService?: boolean }).localService) ?? same[0]
}

interface Queued extends SpeechMessage { priority: Priority; createdAt: number; entry: TranscriptEntry }

const DEFAULT_TTL: Record<Priority, number> = { 0: 15_000, 1: 40_000, 2: 120_000 }
const MAX_TRANSCRIPT = 200

export class SpeechQueue {
  private readonly synth: SynthLike | null
  private readonly makeUtterance: (text: string) => UtteranceLike
  private readonly now: () => number
  private queue: Queued[] = []
  private currentItem: Queued | null = null
  private token = 0
  private spokenKeys = new Set<string>()
  private lastSpoken: Queued | null = null
  private listeners = new Set<(s: SpeechState) => void>()
  private nextId = 1
  private state: SpeechState

  rate = 1
  volume = 1

  constructor(options?: { synth?: SynthLike | null; makeUtterance?: (text: string) => UtteranceLike; now?: () => number }) {
    const browserSynth = typeof window !== 'undefined' && 'speechSynthesis' in window ? (window.speechSynthesis as unknown as SynthLike) : null
    this.synth = options?.synth !== undefined ? options.synth : browserSynth
    this.makeUtterance = options?.makeUtterance ?? ((text) => new SpeechSynthesisUtterance(text) as unknown as UtteranceLike)
    this.now = options?.now ?? (() => Date.now())
    this.state = { supported: !!this.synth, speaking: false, paused: false, muted: false, current: null, transcript: [], missingVoice: null }
    // Voices often load after the page (getVoices() can be empty at first). One listener refreshes the cached list and
    // clears a "missing voice" notice that was only due to voices not having loaded yet.
    this.synth?.addEventListener?.('voiceschanged', this.onVoicesChanged)
  }

  private voices: VoiceLike[] = []
  private language: string | null = null

  private readonly onVoicesChanged = () => {
    this.voices = this.synth?.getVoices() ?? []
    if (this.state.missingVoice && chooseVoice(this.voices, this.state.missingVoice)) this.update({ missingVoice: null })
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
    if (this.language === lang) return
    this.language = lang
    const base = baseOf(lang)
    this.queue = this.queue.filter(q => baseOf(q.lang) === base)
    if (this.currentItem && baseOf(this.currentItem.lang) !== base) this.cancelCurrent()
    if (this.state.missingVoice && baseOf(this.state.missingVoice) !== base) this.update({ missingVoice: null })
  }

  subscribe(listener: (s: SpeechState) => void) {
    this.listeners.add(listener)
    listener(this.state)
    return () => { this.listeners.delete(listener) }
  }

  getState() { return this.state }

  /** Queue a message. Returns false if it was a duplicate. */
  say(message: SpeechMessage, options?: { allowRepeat?: boolean }): boolean {
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
    if (!this.synth || this.state.muted) { this.emit(); return true }

    // Voices are loaded but none speaks this language: still speak with utterance.lang set (the browser picks the best it
    // has) and surface a notice so the viewer knows pronunciation may suffer; the text is always in the transcript.
    const voice = this.voiceFor(message.lang)
    if (voice === undefined) this.update({ missingVoice: message.lang })
    else if (this.state.missingVoice === message.lang) this.update({ missingVoice: null })

    const item: Queued = { ...message, priority, createdAt: this.now(), entry }
    if (priority === 2 && this.currentItem && this.currentItem.priority < 2) {
      // Critical news interrupts lower-priority speech; the interrupted message is dropped (it would be stale).
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
    if (!this.synth) return
    this.synth.pause()
    this.update({ paused: true })
  }

  resume() {
    if (!this.synth) return
    this.synth.resume()
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
    this.token++
    this.currentItem = null
    this.synth?.cancel()
    this.update({ speaking: false, current: null })
  }

  private pump() {
    if (!this.synth || this.currentItem || this.state.paused || this.state.muted) return
    const now = this.now()
    this.queue = this.queue.filter(q => now - q.createdAt <= (q.ttlMs ?? DEFAULT_TTL[q.priority]))
    if (this.queue.length === 0) return
    // Highest priority first; FIFO within a priority.
    let bestIndex = 0
    for (let i = 1; i < this.queue.length; i++) if (this.queue[i].priority > this.queue[bestIndex].priority) bestIndex = i
    const item = this.queue.splice(bestIndex, 1)[0]
    const u = this.makeUtterance(item.text)
    u.lang = item.lang
    u.rate = this.rate
    u.volume = this.volume
    u.voice = this.voiceFor(item.lang) ?? null
    const token = ++this.token
    const done = () => {
      if (token !== this.token) return
      this.currentItem = null
      this.update({ speaking: false, current: null })
      this.pump()
    }
    u.onend = done
    u.onerror = done
    this.currentItem = item
    this.lastSpoken = item
    item.entry.spoken = true
    this.update({ speaking: true, current: item.entry })
    this.synth.speak(u)
  }

  /**
   * Voice for a language: a matching voice, null if voices are not available yet (let the browser choose),
   * or undefined if voices are available but none matches (fall back to text).
   */
  private voiceFor(lang: string): VoiceLike | null | undefined {
    if (this.voices.length === 0) this.voices = this.synth?.getVoices() ?? []
    if (this.voices.length === 0) return null
    return chooseVoice(this.voices, lang) ?? undefined
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
