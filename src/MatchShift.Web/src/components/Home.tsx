import type { Strings } from '../i18n'

/**
 * Keep the introduction beside the match and use the same labels as the real evidence controls.
 */
export function FeedIntro({ s, onTrust }: { s: Strings; onTrust?: () => void }) {
  const h = s.home
  return (
    <section className="feed-intro" aria-label={h.loopLabel}>
      <p className="feed-purpose">{h.purpose}</p>
      <ol className="feed-loop">
        {[s.why, s.showMe, s.replay.button].map((label, i) => <li key={label}><strong>{label}</strong><span>{h.loop[i]}</span></li>)}
      </ol>
      <p className="feed-trust">
        {h.trust}
        {onTrust && <> <button className="ghost trust-link" onClick={onTrust}>{h.trustLink}</button></>}
      </p>
    </section>
  )
}

/**
 * Before the first insight: say what MatchShift is watching for (never promise an insight), offer the one primary
 * action, and the optional tour.
 */
export function EmptyFeed({ s, onStart, onTour, playing, finished, started }: {
  s: Strings; onStart: () => void; onTour?: () => void; playing: boolean; finished: boolean; started: boolean
}) {
  const h = s.home
  return (
    <div className="empty feed-empty">
      <p className="feed-watching" role="status">{finished ? h.finished : playing ? h.watching : h.waiting}</p>
      {!playing && !finished && <button className="primary feed-start" onClick={onStart}><span aria-hidden>▶ </span>{started ? s.play : h.start}</button>}
      {onTour && <p className="feed-tour">{h.tour} <button className="chip guide-entry" onClick={onTour}>{s.guide.entry}</button></p>}
    </div>
  )
}
