import { useState } from 'react'
import type { Team, TimelinePoint } from '../api'
import type { Strings } from '../i18n'
import { AWAY_COLOR, HOME_COLOR } from './Pitch'

const VW = 600
const VH = 96
const PAD = { l: 26, r: 30, t: 6, b: 16 }
const x = (minute: number) => PAD.l + ((VW - PAD.l - PAD.r) * minute) / 90
const y = (v: number) => PAD.t + ((VH - PAD.t - PAD.b) * (100 - v)) / 100

function useHover(points: TimelinePoint[]) {
  const [hover, setHover] = useState<TimelinePoint | null>(null)
  const onMove = (e: React.MouseEvent<SVGSVGElement>) => {
    const rect = e.currentTarget.getBoundingClientRect()
    const vx = ((e.clientX - rect.left) / rect.width) * VW
    const minute = Math.round(((vx - PAD.l) / (VW - PAD.l - PAD.r)) * 90)
    setHover(points.find((p) => p.minute === Math.max(1, Math.min(90, minute))) ?? null)
  }
  return { hover, onMove, onLeave: () => setHover(null) }
}

function Axes() {
  return (
    <g>
      {[0, 50, 100].map((v) => (
        <g key={v}>
          <line x1={PAD.l} x2={VW - PAD.r} y1={y(v)} y2={y(v)} stroke="var(--grid)" strokeWidth={1} />
          <text x={PAD.l - 6} y={y(v) + 3} fontSize="9" textAnchor="end" fill="var(--ink-muted)">{v}</text>
        </g>
      ))}
      {[0, 15, 30, 45, 60, 75, 90].map((m) => (
        <text key={m} x={x(m)} y={VH - 3} fontSize="9" textAnchor="middle" fill="var(--ink-muted)">{m}'</text>
      ))}
      <line x1={x(45)} x2={x(45)} y1={PAD.t} y2={VH - PAD.b} stroke="var(--line-strong)" strokeWidth={1} />
    </g>
  )
}

function Tooltip({ point, children }: { point: TimelinePoint; children: React.ReactNode }) {
  return (
    <div className="tooltip" style={{ left: `${(x(point.minute) / VW) * 100}%`, top: 0 }}>
      {children}
    </div>
  )
}

/** Match Control over time: one series around a 50 midline; the wash shows who is on top. */
/** A round end-marker that stays circular even though the chart's viewBox is stretched. */
function EndDot({ cx, cy, color }: { cx: number; cy: number; color: string }) {
  return (
    <g>
      <line x1={cx} y1={cy} x2={cx} y2={cy} stroke="var(--surface-sunken)" strokeWidth={12} strokeLinecap="round" vectorEffect="non-scaling-stroke" />
      <line x1={cx} y1={cy} x2={cx} y2={cy} stroke={color} strokeWidth={8} strokeLinecap="round" vectorEffect="non-scaling-stroke" />
    </g>
  )
}

export function MomentumChart({ points, home, away, s }: { points: TimelinePoint[]; home: Team; away: Team; s: Strings }) {
  const { hover, onMove, onLeave } = useHover(points)
  const line = points.map((p, i) => `${i === 0 ? 'M' : 'L'}${x(p.minute)},${y(p.homeControl)}`).join(' ')
  const above = points.length ? `M${x(points[0].minute)},${y(50)} ` + points.map((p) => `L${x(p.minute)},${y(Math.max(50, p.homeControl))}`).join(' ') + ` L${x(points[points.length - 1].minute)},${y(50)} Z` : ''
  const below = points.length ? `M${x(points[0].minute)},${y(50)} ` + points.map((p) => `L${x(p.minute)},${y(Math.min(50, p.homeControl))}`).join(' ') + ` L${x(points[points.length - 1].minute)},${y(50)} Z` : ''
  const last = points[points.length - 1]

  return (
    <div className="chart" style={{ position: 'relative' }}>
      <p className="chart-title">
        <span>{s.matchControl} · {s.lastTen}</span>
        <span className="chart-legend">
          <span className="legend-key"><i style={{ background: HOME_COLOR, opacity: 0.6 }} />{home.shortName}</span>
          <span className="legend-key"><i style={{ background: AWAY_COLOR, opacity: 0.6 }} />{away.shortName}</span>
        </span>
      </p>
      <svg viewBox={`0 0 ${VW} ${VH}`} preserveAspectRatio="none" onMouseMove={onMove} onMouseLeave={onLeave} role="img"
        aria-label={`${s.matchControl}: ${home.shortName} ${last ? Math.round(last.homeControl) : 50}`}>
        <Axes />
        <path d={above} fill={HOME_COLOR} opacity={0.28} />
        <path d={below} fill={AWAY_COLOR} opacity={0.28} />
        <path d={line} fill="none" stroke="var(--ink-secondary)" strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" vectorEffect="non-scaling-stroke" />
        {last && <EndDot cx={x(last.minute)} cy={y(last.homeControl)} color="var(--ink)" />}
        {hover && <line x1={x(hover.minute)} x2={x(hover.minute)} y1={PAD.t} y2={VH - PAD.b} stroke="var(--ink-muted)" strokeWidth={1} />}
      </svg>
      {hover && (
        <Tooltip point={hover}>
          {hover.minute}' · {home.shortName} {Math.round(hover.homeControl)} – {Math.round(100 - hover.homeControl)} {away.shortName}
        </Tooltip>
      )}
    </div>
  )
}

/** Pressure Index, both teams, one shared 0-100 axis. */
export function PressureChart({ points, home, away, s }: { points: TimelinePoint[]; home: Team; away: Team; s: Strings }) {
  const { hover, onMove, onLeave } = useHover(points)
  const path = (pick: (p: TimelinePoint) => number) => points.map((p, i) => `${i === 0 ? 'M' : 'L'}${x(p.minute)},${y(pick(p))}`).join(' ')
  const last = points[points.length - 1]
  return (
    <div className="chart" style={{ position: 'relative' }}>
      <p className="chart-title">
        <span>{s.pressure} · {s.lastFive}</span>
        <span className="chart-legend">
          <span className="legend-key"><i style={{ background: HOME_COLOR }} />{home.shortName}</span>
          <span className="legend-key"><i style={{ background: AWAY_COLOR }} />{away.shortName}</span>
        </span>
      </p>
      <svg viewBox={`0 0 ${VW} ${VH}`} preserveAspectRatio="none" onMouseMove={onMove} onMouseLeave={onLeave} role="img" aria-label={s.pressure}>
        <Axes />
        <path d={path((p) => p.homePressure)} fill="none" stroke={HOME_COLOR} strokeWidth={2} strokeLinejoin="round" vectorEffect="non-scaling-stroke" />
        <path d={path((p) => p.awayPressure)} fill="none" stroke={AWAY_COLOR} strokeWidth={2} strokeLinejoin="round" vectorEffect="non-scaling-stroke" />
        {last && (
          <>
            <EndDot cx={x(last.minute)} cy={y(last.homePressure)} color={HOME_COLOR} />
            <EndDot cx={x(last.minute)} cy={y(last.awayPressure)} color={AWAY_COLOR} />
          </>
        )}
        {hover && <line x1={x(hover.minute)} x2={x(hover.minute)} y1={PAD.t} y2={VH - PAD.b} stroke="var(--ink-muted)" strokeWidth={1} />}
      </svg>
      {hover && (
        <Tooltip point={hover}>
          {hover.minute}' · {home.shortName} {Math.round(hover.homePressure)} · {away.shortName} {Math.round(hover.awayPressure)}
        </Tooltip>
      )}
    </div>
  )
}
