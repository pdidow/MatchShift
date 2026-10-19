import { useEffect, useState } from 'react'
import type { HighlightCard, Profile, RecapResponse, RecapStyle, Snapshot } from '../api'
import { api } from '../api'
import type { Strings } from '../i18n'

export function HighlightsPanel({ snap, s, onShow, onReplay }: { snap: Snapshot; s: Strings; onShow: (h: HighlightCard) => void; onReplay: (insightId: string) => void }) {
  if (snap.highlights.length === 0) return <p className="empty">{s.waiting}</p>
  const teamName = (id?: string) => (id === snap.home.id ? snap.home.shortName : id === snap.away.id ? snap.away.shortName : '')
  return (
    <>
      {snap.highlights.map((h) => (
        <article key={h.id} className={`card ${h.kind === 'Goal' ? '' : 'shift'}`} style={h.kind === 'Goal' ? { ['--card-accent' as string]: 'var(--ink)' } : undefined}>
          <div className="card-top">
            <span className="card-kicker">{h.kind.replace(/([a-z])([A-Z])/g, '$1 $2')} {h.teamId && <span style={{ color: 'var(--ink-muted)' }}>· {teamName(h.teamId)}</span>}</span>
            <span className="card-minute">{h.minute}' · {Math.round(h.importance)}</span>
          </div>
          <h3 className="card-headline" style={{ fontSize: 18 }}>{h.headline}</h3>
          <p className="card-sub">{h.whyItMatters}</p>
          <div className="card-actions">
            <button className="chip" onClick={() => onShow(h)}>{s.showMe}</button>
            {h.insightId && <button className="chip replay-btn" onClick={() => onReplay(h.insightId!)}>{s.replay.button}</button>}
          </div>
        </article>
      ))}
    </>
  )
}

export function RecapPanel({ snap, s, profile, styles }: { snap: Snapshot; s: Strings; profile: Profile; styles: RecapStyle[] }) {
  const [period, setPeriod] = useState<'halftime' | 'fulltime'>('halftime')
  const [style, setStyle] = useState<RecapStyle>('Casual')
  const [recap, setRecap] = useState<RecapResponse | null | 'loading'>(null)
  const available = period === 'halftime' ? snap.clock.second >= 2699 : snap.clock.second >= 5399

  useEffect(() => {
    if (!available) { setRecap(null); return }
    let cancelled = false
    setRecap('loading')
    api.recap(period, style, profile).then((r) => { if (!cancelled) setRecap(r) }).catch(() => { if (!cancelled) setRecap(null) })
    return () => { cancelled = true }
  }, [period, style, profile.language, profile.focusTeamId, snap.matchId, available])

  return (
    <div className="recap">
      <div className="recap-controls">
        <button className={`chip ${period === 'halftime' ? 'active' : ''}`} onClick={() => setPeriod('halftime')}>{s.halftime}</button>
        <button className={`chip ${period === 'fulltime' ? 'active' : ''}`} onClick={() => setPeriod('fulltime')}>{s.fulltime}</button>
        <span style={{ width: 10 }} />
        {styles.map((st) => (
          <button key={st} className={`chip ${style === st ? 'active' : ''}`} onClick={() => setStyle(st)}>{st}</button>
        ))}
      </div>
      {!available && <p className="empty">{s.recapLocked}</p>}
      {available && recap === 'loading' && <p className="empty">…</p>}
      {available && recap && recap !== 'loading' && (
        <>
          <h3>{recap.narrative.title}</h3>
          {recap.narrative.sections.map((sec, i) => (
            <section key={i}>
              {sec.heading && <h4>{sec.heading}</h4>}
              {sec.paragraphs.map((p, j) => <p key={j}>{p}</p>)}
            </section>
          ))}
          <p className="note">{recap.narrative.source}{recap.narrative.verificationNote ? ` · ${recap.narrative.verificationNote}` : ''}</p>
        </>
      )}
    </div>
  )
}

export function Splash({ tagline, synthetic }: { tagline: string; synthetic: string }) {
  return (
    <div className="splash" aria-hidden>
      <div className="splash-inner">
        <Logo size={72} />
        <h1>MatchShift</h1>
        <p>{tagline}</p>
        <span className="badge-synthetic">{synthetic}</span>
      </div>
    </div>
  )
}

export function Logo({ size = 30 }: { size?: number }) {
  return (
    <svg className="brand-mark" width={size} height={size} viewBox="0 0 32 32" aria-hidden>
      <rect x="1" y="1" width="30" height="30" rx="8" fill="#0a0f18" stroke="#39e6b0" strokeWidth="2" />
      <path d="M7 21 L13 15 L17 19 L25 10" fill="none" stroke="#39e6b0" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" />
      <circle cx="25" cy="10" r="2.6" fill="#eef2f8" />
    </svg>
  )
}
