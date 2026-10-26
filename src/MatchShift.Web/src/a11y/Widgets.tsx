import { useEffect, useRef, useState } from 'react'
import type { Lang } from '../api'
import { useA11y, useDialog } from './A11yContext'
import { activeFeatures, speechEnabled } from './prefs'
import { a11yStrings } from './strings'
import { languageOf } from '../languages'

/** Topbar button + a subtle status line that appears only when at least one option is on. */
export function A11yButton({ lang, onOpen, highlight = false }: { lang: Lang; onOpen: () => void; highlight?: boolean }) {
  const { prefs, systemReducedMotion } = useA11y()
  const s = a11yStrings(lang)
  const on = activeFeatures(prefs, systemReducedMotion)
  const labels = on.map(k => s.status[k as keyof typeof s.status] ?? k)
  return (
    <button className={`chip a11y-open ${on.length ? 'has-active' : ''} ${highlight ? 'guide-target' : ''}`} onClick={onOpen} aria-haspopup="dialog"
      aria-label={on.length ? `${s.open}: ${labels.join(', ')}` : s.open} title={on.length ? labels.join(' · ') : s.open}>
      <svg width="14" height="14" viewBox="0 0 24 24" aria-hidden><circle cx="12" cy="4" r="2.2" fill="currentColor" />
        <path d="M4 8.5 L12 10 L20 8.5 M12 10 V15 M12 15 L8 22 M12 15 L16 22" stroke="currentColor" strokeWidth="2.2" fill="none" strokeLinecap="round" /></svg>
      {s.open}
      {on.length > 0 && <span className="a11y-count" aria-hidden>{on.length}</span>}
    </button>
  )
}

/** Speech controls and captions. Rendered only when a speech feature or captions are switched on. */
export function SpeechBar({ lang, overReplay, onStatus }: { lang: Lang; overReplay?: boolean; onStatus?: () => void }) {
  const { prefs, speech, speechState } = useA11y()
  const s = a11yStrings(lang)
  const [showTranscript, setShowTranscript] = useState(false)
  const speaking = speechEnabled(prefs)
  if (!speaking && !prefs.transcript) return null
  const st = speechState
  const entries = st.transcript.filter(e => languageOf(e.lang) === lang)
  const lastEntry = entries.at(-1)
  const caption = st.current && languageOf(st.current.lang) === lang ? st.current : lastEntry
  const canRepeat = speech.canRepeat

  return (
    <>
      <div className={`speech-bar ${overReplay ? 'over-replay' : ''}`} role="region" aria-label={s.speech.transcript}>
        {speaking && (
          <div className="speech-controls">
            <span className={`speech-dot ${st.speaking && !st.paused ? 'on' : ''}`} aria-hidden />
            <span className="sr-only">{st.speaking ? s.speech.speaking : s.speech.idle}</span>
            <button className="chip" aria-pressed={st.muted} onClick={() => speech.setMuted(!st.muted)}>{st.muted ? s.speech.unmute : s.speech.mute}</button>
            {(st.speaking || st.paused) && !st.muted && (
              <button className="chip" aria-pressed={st.paused} onClick={() => (st.paused ? speech.resume() : speech.pause())}>
                {st.paused ? s.speech.resume : s.speech.pause}
              </button>
            )}
            {canRepeat && !st.muted && <button className="chip" onClick={() => speech.repeatLast()}>{s.speech.repeat}</button>}
            {onStatus && !st.muted && <button className="chip" onClick={onStatus}>{s.speech.statusNow}</button>}
            {prefs.transcript && (
              <button className="chip" aria-expanded={showTranscript} onClick={() => setShowTranscript(v => !v)}>
                {showTranscript ? s.speech.hide : s.speech.transcript}
              </button>
            )}
          </div>
        )}
        {!speaking && prefs.transcript && (
          <div className="speech-controls">
            <button className="chip" aria-expanded={showTranscript} onClick={() => setShowTranscript(v => !v)}>
              {showTranscript ? s.speech.hide : s.speech.transcript}
            </button>
          </div>
        )}
        {prefs.transcript && caption && (
          // Captions are visual only: screen readers get the live region / their own speech instead of a duplicate.
          <p className="speech-caption" aria-hidden>
            {caption.clock && <span className="speech-clock">{caption.clock}</span>}{caption.text}
          </p>
        )}
        {(!st.supported || st.missingVoice || st.error) && speaking && <p className="speech-note" role="status">{st.missingVoice ? s.speech.noVoice : st.error ? s.speech.failed : s.speech.unsupported}</p>}
      </div>
      {showTranscript && prefs.transcript && <TranscriptPanel lang={lang} onClose={() => setShowTranscript(false)} />}
    </>
  )
}

function TranscriptPanel({ lang, onClose }: { lang: Lang; onClose: () => void }) {
  const { speech, speechState } = useA11y()
  const s = a11yStrings(lang)
  const listRef = useRef<HTMLOListElement>(null)
  const entries = speechState.transcript
  useEffect(() => { listRef.current?.lastElementChild?.scrollIntoView?.({ block: 'nearest' }) }, [entries.length])
  return (
    <aside className="transcript-panel" aria-label={s.speech.transcript}>
      <div className="card-top">
        <h2 className="panel-title" style={{ margin: 0 }}>{s.speech.transcript}</h2>
        <span style={{ display: 'flex', gap: 6 }}>
          <button className="chip" onClick={() => speech.clearTranscript()} disabled={entries.length === 0}>{s.speech.clear}</button>
          <button className="ghost" onClick={onClose} aria-label={s.speech.hide}>✕</button>
        </span>
      </div>
      {entries.length === 0 ? <p className="note">{s.speech.empty}</p> : (
        <ol className="transcript-list" ref={listRef}>
          {entries.map(e => (
            <li key={e.id} lang={e.lang}>
              <button className="transcript-entry" onClick={() => speech.replay(e)} title={s.speech.replayEntry} disabled={languageOf(e.lang) !== lang}>
                <span className="transcript-meta">
                  <time>{e.clock ?? new Date(e.at).toLocaleTimeString(lang, { hour: '2-digit', minute: '2-digit', second: '2-digit' })}</time>
                  <span className="transcript-kind">{e.label ?? e.kind}</span>
                </span>
                <span className="transcript-text">{e.text}</span>
                <span className="sr-only"> — {s.speech.replayEntry}</span>
              </button>
            </li>
          ))}
        </ol>
      )}
    </aside>
  )
}

export function KeyboardHelp({ lang, onClose }: { lang: Lang; onClose: () => void }) {
  const s = a11yStrings(lang)
  const ref = useRef<HTMLDivElement>(null)
  useDialog(ref, onClose)
  const table = (caption: string, rows: string[][]) => (
    <table className="evidence keys-table">
      <caption>{caption}</caption>
      <tbody>{rows.map(([k, v]) => <tr key={k}><th scope="row"><kbd>{k}</kbd></th><td>{v}</td></tr>)}</tbody>
    </table>
  )
  return (
    <div className="modal-backdrop" onClick={onClose} style={{ zIndex: 60 }}>
      <div className="modal" role="dialog" aria-modal="true" aria-labelledby="keys-title" ref={ref} onClick={(e) => e.stopPropagation()}>
        <div className="card-top">
          <h2 id="keys-title">{s.keys.title}</h2>
          <button className="ghost" onClick={onClose} aria-label={s.close}>✕</button>
        </div>
        {table(s.keys.global, s.keys.list)}
        {table(s.keys.replay, s.keys.replayList)}
      </div>
    </div>
  )
}
