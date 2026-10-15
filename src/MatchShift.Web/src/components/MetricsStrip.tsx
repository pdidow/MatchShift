import type { Snapshot } from '../api'
import type { Strings } from '../i18n'
import { AWAY_COLOR, HOME_COLOR } from './Pitch'

const pct = (v: number) => `${Math.round(v * 100)}%`

export function MetricsStrip({ snap, s }: { snap: Snapshot; s: Strings }) {
  const m = snap.metrics
  const home = Math.round(m.homeControl)
  return (
    <div className="metrics">
      <section className="panel" aria-label={s.matchControl}>
        <h2 className="panel-title">{s.matchControl}<small>{s.lastTen}</small></h2>
        <div className="metric-row" style={{ marginTop: 0 }}>
          <span className="big-number">{home}</span>
          <span className="big-number">{100 - home}</span>
        </div>
        <div className="control-bar" role="meter" aria-valuenow={home} aria-valuemin={0} aria-valuemax={100} aria-label={`${snap.home.shortName} ${home}, ${snap.away.shortName} ${100 - home}`}>
          <div style={{ width: `${home}%`, background: HOME_COLOR }} />
          <div style={{ width: `${100 - home}%`, background: AWAY_COLOR }} />
        </div>
        <div className="metric-row">
          <span>{snap.home.shortName}</span>
          <span>{s.possession} {pct(m.home.possession)} · {pct(m.away.possession)}</span>
          <span>{snap.away.shortName}</span>
        </div>
        <div className="sides">
          {[snap.home, snap.away].map((team, i) => {
            const t = i === 0 ? m.home : m.away
            const c = i === 0 ? HOME_COLOR : AWAY_COLOR
            return (
              <FragmentRow key={team.id} label={`${team.code} ${s.attackSides}`}>
                <div className="sides-bar" title={`${s.left} ${pct(t.leftShare)} · ${s.centre} ${pct(t.centreShare)} · ${s.right} ${pct(t.rightShare)}`}>
                  {t.attacks === 0 ? <div style={{ width: '100%', background: 'var(--line)' }} /> : (
                    <>
                      <div style={{ width: `${t.leftShare * 100}%`, background: c, opacity: 0.55 }} />
                      <div style={{ width: `${t.centreShare * 100}%`, background: c, opacity: 0.8 }} />
                      <div style={{ width: `${t.rightShare * 100}%`, background: c }} />
                    </>
                  )}
                </div>
              </FragmentRow>
            )
          })}
        </div>
      </section>

      <section className="panel" aria-label={s.pressure}>
        <h2 className="panel-title">{s.pressure}<small>{s.lastFive}</small></h2>
        <div className="pair">
          {[snap.home, snap.away].map((team, i) => {
            const t = i === 0 ? m.home : m.away
            return (
              <div className="pair-item" key={team.id}>
                <span className="label"><i style={{ width: 8, height: 8, borderRadius: '50%', background: i === 0 ? HOME_COLOR : AWAY_COLOR, display: 'inline-block' }} />{team.shortName}</span>
                <span className="big-number">{Math.round(t.pressure)}</span>
                <div className="mini-bar"><div style={{ width: `${t.pressure}%`, background: i === 0 ? HOME_COLOR : AWAY_COLOR }} /></div>
                <span className="label">{s.shots} {t.shots} · xQ {t.chanceQuality.toFixed(2)}</span>
              </div>
            )
          })}
        </div>
      </section>

      <section className="panel" aria-label={s.chaos}>
        <h2 className="panel-title">{s.chaos}<small>{s.lastTen}</small></h2>
        <div className="chaos-state">
          <span>{s.chaosStates[m.chaosState]}</span>
          <span className="big-number" style={{ fontSize: 22, color: 'var(--ink-secondary)' }}>{Math.round(m.chaos)}</span>
        </div>
        <div className="chaos-meter" aria-hidden>
          <div className="needle" style={{ left: `${Math.min(100, m.chaos)}%` }} />
        </div>
        <div className="metric-row">
          <span>{s.chaosStates.CONTROLLED}</span><span>{s.chaosStates.BALANCED}</span><span>{s.chaosStates.CHAOTIC}</span>
        </div>
      </section>
    </div>
  )
}

function FragmentRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <>
      <span>{label}</span>
      {children}
    </>
  )
}
