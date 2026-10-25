import type { HighlightCard, Snapshot } from '../api'
import { api } from '../api'
import type { Strings } from '../i18n'
import { formatMinutes, spokenClock } from '../languages'

interface Props {
  snap: Snapshot
  speeds: number[]
  s: Strings
  onHighlight: (h: HighlightCard) => void
}

export function Transport({ snap, speeds, s, onHighlight }: Props) {
  const progress = Math.min(100, (snap.clock.second / 5400) * 100)

  const seekFromClick = (e: React.MouseEvent<HTMLDivElement>) => {
    const rect = e.currentTarget.getBoundingClientRect()
    const minute = Math.max(0, Math.min(90, ((e.clientX - rect.left) / rect.width) * 90))
    void api.seek(Math.round(minute * 2) / 2)
  }

  return (
    <div className="transport">
      <div className="transport-row">
        {snap.isPlaying
          ? <button className="primary" onClick={() => void api.pause()} aria-label={s.pause}>❚❚ {s.pause}</button>
          : <button className="primary" onClick={() => void api.play()} aria-label={s.play}>▶ {s.play}</button>}
        <button onClick={() => void api.reset()} aria-label={s.reset}>↺</button>
      </div>
      <div className="transport-row" role="group" aria-label={s.speed}>
        {speeds.map((sp) => (
          <button key={sp} className={`chip ${snap.speed === sp ? 'active' : ''}`} onClick={() => void api.speed(sp)} aria-pressed={snap.speed === sp}>
            {sp}x
          </button>
        ))}
      </div>
      <div style={{ width: 230 }}>
        <div className="scrubber" onClick={seekFromClick} role="slider" aria-label={s.minute} aria-valuemin={0} aria-valuemax={90}
          aria-valuenow={snap.clock.minute} aria-valuetext={spokenClock(snap.clock.second, snap.profile.language)} tabIndex={0}
          onKeyDown={(e) => {
            if (e.key === 'ArrowRight') void api.seek(Math.min(90, snap.clock.second / 60 + 1))
            if (e.key === 'ArrowLeft') void api.seek(Math.max(0, snap.clock.second / 60 - 1))
          }}>
          <div className="scrubber-track" />
          <div className="scrubber-fill" style={{ width: `${progress}%` }} />
          <div className="scrubber-ht" style={{ left: '50%' }} />
          {snap.highlights.map((h) => (
            <button
              key={h.id}
              className={`marker ${h.kind === 'Goal' ? 'goal' : h.kind === 'TacticalShift' || h.kind === 'SubstitutionImpact' ? 'shift' : ''}`}
              style={{ left: `${(h.second / 5400) * 100}%` }}
              title={`${h.minute}' ${h.headline}`}
              aria-label={`${formatMinutes(h.minute, snap.profile.language)}. ${h.headline}`}
              onClick={(e) => { e.stopPropagation(); onHighlight(h) }}
            />
          ))}
        </div>
        <div className="metric-row" style={{ marginTop: 2 }}><span>0'</span><span>45'</span><span>90'</span></div>
      </div>
    </div>
  )
}
