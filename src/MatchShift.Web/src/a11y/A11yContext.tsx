import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import type { A11yPrefs } from './prefs'
import { documentAttributes, effectiveReducedMotion, loadPrefs, savePrefs, speechEnabled } from './prefs'
import type { SpeechMessage, SpeechState } from './speech'
import { SpeechQueue } from './speech'
import type { Cue } from './sounds'
import { playCue } from './sounds'
import { localeOf } from '../languages'

interface A11yContextValue {
  prefs: A11yPrefs
  setPrefs: (next: A11yPrefs | ((p: A11yPrefs) => A11yPrefs)) => void
  update: (patch: Partial<A11yPrefs>) => void
  /** prefers-reduced-motion from the operating system. */
  systemReducedMotion: boolean
  reducedMotion: boolean
  speech: SpeechQueue
  speechState: SpeechState
  /** Queue speech (also recorded in the transcript). Ignored when no speech feature is enabled. */
  say: (message: SpeechMessage, options?: { allowRepeat?: boolean }) => boolean
  /** Screen-reader announcement through a live region (polite by default; assertive for critical news). */
  announce: (text: string, assertive?: boolean) => void
  setLanguage: (language: string) => void
  cue: (cue: Cue) => void
}

const Ctx = createContext<A11yContextValue | null>(null)

const ATTRIBUTES = ['data-contrast', 'data-cvd', 'data-text', 'data-reduced-motion', 'data-reduce-flash']

// One queue for the whole page: only one voice ever speaks at a time.
let sharedQueue: SpeechQueue | null = null
const queue = () => (sharedQueue ??= new SpeechQueue())

export function A11yProvider({ children }: { children: ReactNode }) {
  const [prefs, setPrefsState] = useState<A11yPrefs>(() => loadPrefs())
  const [systemReducedMotion, setSystemReduced] = useState(() =>
    typeof window !== 'undefined' && !!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches)
  const speech = queue()
  const [speechState, setSpeechState] = useState<SpeechState>(speech.getState())

  const setPrefs = useCallback((next: A11yPrefs | ((p: A11yPrefs) => A11yPrefs)) => {
    setPrefsState(prev => {
      const value = typeof next === 'function' ? next(prev) : next
      savePrefs(value)
      return value
    })
  }, [])
  const update = useCallback((patch: Partial<A11yPrefs>) => setPrefs(p => ({ ...p, ...patch })), [setPrefs])

  useEffect(() => {
    const mq = window.matchMedia?.('(prefers-reduced-motion: reduce)')
    if (!mq) return
    const on = () => setSystemReduced(mq.matches)
    mq.addEventListener?.('change', on)
    return () => mq.removeEventListener?.('change', on)
  }, [])

  // Visual adaptations are data attributes on <html>; with default prefs none are set.
  useEffect(() => {
    const root = document.documentElement
    const attrs = documentAttributes(prefs, systemReducedMotion)
    for (const a of ATTRIBUTES) {
      if (attrs[a] === undefined) root.removeAttribute(a)
      else root.setAttribute(a, attrs[a])
    }
  }, [prefs, systemReducedMotion])

  useEffect(() => speech.subscribe(setSpeechState), [speech])

  const anySpeech = speechEnabled(prefs)
  useEffect(() => {
    speech.rate = prefs.speechRate
    speech.volume = prefs.speechVolume
  }, [speech, prefs.speechRate, prefs.speechVolume])
  useEffect(() => { speech.setPreferredVoices(prefs.voicePreferences) }, [speech, prefs.voicePreferences])
  useEffect(() => { if (!anySpeech) speech.stopAll() }, [speech, anySpeech])
  useEffect(() => { if (!prefs.replayAudioDescription) speech.clearKind('replay') }, [speech, prefs.replayAudioDescription])

  const say = useCallback((message: SpeechMessage, options?: { allowRepeat?: boolean }) =>
    anySpeech ? speech.say(message, options) : false, [speech, anySpeech])

  // Live regions: two visually hidden regions. Identical text within a short window is not repeated.
  const [polite, setPolite] = useState('')
  const [assertive, setAssertive] = useState('')
  const lastAnnounced = useRef<{ text: string; at: number }>({ text: '', at: 0 })
  const announcementTimers = useRef(new Set<number>())
  const clearAnnouncements = useCallback(() => {
    for (const timer of announcementTimers.current) window.clearTimeout(timer)
    announcementTimers.current.clear()
  }, [])
  useEffect(() => clearAnnouncements, [clearAnnouncements])
  const language = useRef<string | null>(null)
  const setLanguage = useCallback((next: string) => {
    const locale = localeOf(next)
    if (language.current === locale) return
    language.current = locale
    speech.setLanguage(locale)
    document.documentElement.lang = locale
    clearAnnouncements()
    lastAnnounced.current = { text: '', at: 0 }
    setPolite('')
    setAssertive('')
  }, [speech, clearAnnouncements])
  const announce = useCallback((text: string, isAssertive = false) => {
    const now = Date.now()
    if (text === lastAnnounced.current.text && now - lastAnnounced.current.at < 5000) return
    lastAnnounced.current = { text, at: now }
    const set = isAssertive ? setAssertive : setPolite
    // Clear first so the same region reliably announces the new text.
    set('')
    const timer = window.setTimeout(() => { announcementTimers.current.delete(timer); set(text) }, 60)
    announcementTimers.current.add(timer)
  }, [])

  const cue = useCallback((c: Cue) => { if (prefs.soundCues) playCue(c, prefs.speechVolume) }, [prefs.soundCues, prefs.speechVolume])

  const value = useMemo<A11yContextValue>(() => ({
    prefs, setPrefs, update, systemReducedMotion,
    reducedMotion: effectiveReducedMotion(prefs, systemReducedMotion),
    speech, speechState, say, announce, setLanguage, cue,
  }), [prefs, setPrefs, update, systemReducedMotion, speech, speechState, say, announce, setLanguage, cue])

  return (
    <Ctx.Provider value={value}>
      {children}
      <div className="sr-only" role="status" aria-live="polite" aria-atomic="true" data-testid="live-polite">{polite}</div>
      <div className="sr-only" role="alert" aria-live="assertive" aria-atomic="true" data-testid="live-assertive">{assertive}</div>
    </Ctx.Provider>
  )
}

export function useA11y(): A11yContextValue {
  const v = useContext(Ctx)
  if (!v) throw new Error('useA11y must be used inside <A11yProvider>')
  return v
}

/** Like useA11y, but returns null outside the provider (for components also used standalone, e.g. the broadcast view). */
export const useOptionalA11y = () => useContext(Ctx)

/**
 * Dialog behaviour: focus the first control (or `initial`), trap Tab inside, close on Escape, and return focus to
 * whatever was focused before the dialog opened.
 */
export function useDialog(ref: React.RefObject<HTMLElement | null>, onClose: () => void) {
  const close = useRef(onClose)
  close.current = onClose
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null
    const el = ref.current
    const focusables = () => Array.from(el?.querySelectorAll<HTMLElement>(
      'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea, [tabindex]:not([tabindex="-1"])') ?? [])
      .filter(f => f.offsetParent !== null || f === document.activeElement)
    const initial = el?.querySelector<HTMLElement>('[data-autofocus]') ?? focusables()[0]
    initial?.focus()
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') { e.stopPropagation(); close.current(); return }
      if (e.key !== 'Tab') return
      const list = focusables()
      if (list.length === 0) return
      const first = list[0]
      const last = list[list.length - 1]
      if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus() }
      else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus() }
    }
    el?.addEventListener('keydown', onKey)
    return () => {
      el?.removeEventListener('keydown', onKey)
      if (previous && document.contains(previous)) previous.focus()
    }
  }, [ref])
}

/** True when a keyboard event comes from a text field or a control that handles its own keys. */
export function isTypingTarget(target: EventTarget | null): boolean {
  const el = target as HTMLElement | null
  if (!el || !el.tagName) return false
  const tag = el.tagName.toLowerCase()
  return tag === 'input' || tag === 'textarea' || tag === 'select' || el.isContentEditable
}
