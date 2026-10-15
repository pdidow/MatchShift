import { useMemo } from 'react'
import type { InsightEvents, PitchEvent, Team } from '../api'

// Absolute frame from the API: X 0-100 along the pitch (home attacks towards X = 100), Y 0-100 across.
const W = 105
const H = 68
const sx = (x: number) => (x * W) / 100
const sy = (y: number) => (y * H) / 100

export const HOME_COLOR = 'var(--series-home)'
export const AWAY_COLOR = 'var(--series-away)'

interface Props {
  home: Team
  away: Team
  trail: PitchEvent[]
  focus: InsightEvents | null
  focusTeamId?: string
  ariaLabel: string
}

export function Pitch({ home, away, trail, focus, focusTeamId, ariaLabel }: Props) {
  const colorOf = (teamId: string) => (teamId === home.id ? HOME_COLOR : AWAY_COLOR)
  const byId = useMemo(() => new Map((focus?.events ?? []).map((e) => [e.id, e])), [focus])

  return (
    <svg className="pitch-svg" viewBox={`-3 -3 ${W + 6} ${H + 6}`} role="img" aria-label={ariaLabel}>
      <defs>
        {[['home', HOME_COLOR], ['away', AWAY_COLOR], ['muted', '#7c889e']].map(([k, c]) => (
          <marker key={k} id={`arrow-${k}`} viewBox="0 0 10 10" refX="8" refY="5" markerWidth="4" markerHeight="4" orient="auto-start-reverse">
            <path d="M0,0 L10,5 L0,10 z" fill={c} />
          </marker>
        ))}
        <radialGradient id="ball-glow">
          <stop offset="0%" stopColor="#fff" stopOpacity="0.9" />
          <stop offset="100%" stopColor="#fff" stopOpacity="0" />
        </radialGradient>
      </defs>

      <PitchMarkings />

      <text x={2} y={H + 2.3} fontSize="2.2" fill="var(--ink-muted)" fontFamily="var(--font-body)">
        {home.shortName} →
      </text>
      <text x={W - 2} y={-0.8} fontSize="2.2" fill="var(--ink-muted)" textAnchor="end" fontFamily="var(--font-body)">
        ← {away.shortName}
      </text>

      {focus ? (
        <FocusLayers focus={focus} byId={byId} colorOf={colorOf} home={home} focusTeamId={focusTeamId} />
      ) : (
        <Trail trail={trail} colorOf={colorOf} home={home} />
      )}
    </svg>
  )
}

function PitchMarkings() {
  const line = { stroke: 'var(--pitch-line)', strokeWidth: 0.25, fill: 'none' }
  const stripes = Array.from({ length: 10 }, (_, i) => i)
  const boxTop = (H - 40.32) / 2
  const sixTop = (H - 18.32) / 2
  return (
    <g>
      <rect x={0} y={0} width={W} height={H} fill="var(--pitch)" />
      {stripes.map((i) => (i % 2 === 0 ? <rect key={i} x={(i * W) / 10} y={0} width={W / 10} height={H} fill="var(--pitch-stripe)" /> : null))}
      <rect x={0} y={0} width={W} height={H} {...line} />
      <line x1={W / 2} y1={0} x2={W / 2} y2={H} {...line} />
      <circle cx={W / 2} cy={H / 2} r={9.15} {...line} />
      <circle cx={W / 2} cy={H / 2} r={0.35} fill="var(--pitch-line)" />
      {[0, 1].map((side) => {
        const x0 = side === 0 ? 0 : W
        const dir = side === 0 ? 1 : -1
        return (
          <g key={side}>
            <rect x={side === 0 ? 0 : W - 16.5} y={boxTop} width={16.5} height={40.32} {...line} />
            <rect x={side === 0 ? 0 : W - 5.5} y={sixTop} width={5.5} height={18.32} {...line} />
            <circle cx={x0 + dir * 11} cy={H / 2} r={0.35} fill="var(--pitch-line)" />
            <path d={`M ${x0 + dir * 16.5} ${H / 2 - 7.3} A 9.15 9.15 0 0 ${side === 0 ? 1 : 0} ${x0 + dir * 16.5} ${H / 2 + 7.3}`} {...line} />
            <rect x={side === 0 ? -1.5 : W} y={H / 2 - 3.66} width={1.5} height={7.32} {...line} />
          </g>
        )
      })}
    </g>
  )
}

function Trail({ trail, colorOf, home }: { trail: PitchEvent[]; colorOf: (t: string) => string; home: Team }) {
  const shown = trail.filter((e) => e.x !== undefined && e.y !== undefined).slice(-12)
  const last = shown[shown.length - 1]
  const ball = last ? { x: last.endX ?? last.x!, y: last.endY ?? last.y! } : null
  return (
    <g>
      {shown.map((e, i) => {
        const opacity = 0.15 + (0.85 * (i + 1)) / shown.length
        const c = colorOf(e.teamId)
        if (e.type === 'Shot') {
          const goalX = e.teamId === home.id ? 100 : 0
          return (
            <g key={e.id} opacity={opacity}>
              <line x1={sx(e.x!)} y1={sy(e.y!)} x2={sx(goalX)} y2={sy(e.endY ?? 50)} stroke={c} strokeWidth={0.45} strokeDasharray="1 0.6" />
              <circle cx={sx(e.x!)} cy={sy(e.y!)} r={1.1} fill={c} stroke="var(--pitch)" strokeWidth={0.4} />
            </g>
          )
        }
        if ((e.type === 'Pass' || e.type === 'Carry') && e.endX !== undefined) {
          return (
            <line key={e.id} opacity={opacity} x1={sx(e.x!)} y1={sy(e.y!)} x2={sx(e.endX)} y2={sy(e.endY!)}
              stroke={c} strokeWidth={e.type === 'Carry' ? 0.3 : 0.4} strokeDasharray={e.type === 'Carry' ? '0.8 0.6' : undefined}
              strokeLinecap="round" />
          )
        }
        return <circle key={e.id} opacity={opacity} cx={sx(e.x!)} cy={sy(e.y!)} r={0.7} fill={c} />
      })}
      {ball && (
        <g>
          <circle cx={sx(ball.x)} cy={sy(ball.y)} r={2.6} fill="url(#ball-glow)" />
          <circle cx={sx(ball.x)} cy={sy(ball.y)} r={0.8} fill="#fff" stroke="var(--pitch)" strokeWidth={0.3} />
        </g>
      )}
    </g>
  )
}

function FocusLayers({ focus, byId, colorOf, home, focusTeamId }: {
  focus: InsightEvents
  byId: Map<string, PitchEvent>
  colorOf: (t: string) => string
  home: Team
  focusTeamId?: string
}) {
  // Draw de-emphasised layers first so the story sits on top.
  const layers = [...focus.layers].sort((a, b) => Number(a.emphasised) - Number(b.emphasised))
  return (
    <g>
      {layers.map((layer) => {
        const events = layer.eventIds.map((id) => byId.get(id)).filter((e): e is PitchEvent => !!e && e.x !== undefined)
        if (layer.style === 'Channels') return <ChannelShade key={layer.key} events={events} home={home} colorOf={colorOf} />
        return (
          <g key={layer.key} opacity={layer.emphasised ? 1 : 0.35}>
            {events.map((e) => {
              const c = layer.emphasised ? colorOf(e.teamId) : '#7c889e'
              const marker = layer.emphasised ? (e.teamId === home.id ? 'home' : 'away') : 'muted'
              const dim = focusTeamId && e.teamId !== focusTeamId ? 0.55 : 1
              if (e.type === 'Shot' || layer.style === 'Shots') {
                const goalX = e.teamId === home.id ? 100 : 0
                return (
                  <g key={e.id} opacity={dim}>
                    <line x1={sx(e.x!)} y1={sy(e.y!)} x2={sx(goalX)} y2={sy(e.endY ?? 50)} stroke={c} strokeWidth={0.4} strokeDasharray="1 0.6" />
                    <circle cx={sx(e.x!)} cy={sy(e.y!)} r={e.outcome === 'Goal' ? 1.8 : 1.3} fill={c} stroke="var(--pitch)" strokeWidth={0.5} />
                    {e.outcome === 'Goal' && <circle cx={sx(e.x!)} cy={sy(e.y!)} r={2.8} fill="none" stroke="#fff" strokeWidth={0.3} />}
                  </g>
                )
              }
              if (layer.style === 'Routes' && e.endX !== undefined && (e.type === 'Pass' || e.type === 'Carry')) {
                return (
                  <line key={e.id} opacity={dim} x1={sx(e.x!)} y1={sy(e.y!)} x2={sx(e.endX)} y2={sy(e.endY!)} stroke={c}
                    strokeWidth={layer.emphasised ? 0.45 : 0.3} strokeLinecap="round" markerEnd={`url(#arrow-${marker})`}
                    strokeDasharray={e.type === 'Carry' ? '0.8 0.6' : undefined} />
                )
              }
              return <circle key={e.id} opacity={dim} cx={sx(e.x!)} cy={sy(e.y!)} r={1.1} fill={c} stroke="var(--pitch)" strokeWidth={0.5} />
            })}
          </g>
        )
      })}
    </g>
  )
}

/** Shade the flank (third of the pitch width) where a channel insight's attacks happened, in the attacking half. */
function ChannelShade({ events, home, colorOf }: { events: PitchEvent[]; home: Team; colorOf: (t: string) => string }) {
  if (events.length === 0) return null
  const ys = events.map((e) => e.endY ?? e.y!).sort((a, b) => a - b)
  const median = ys[Math.floor(ys.length / 2)]
  const band = median < 33.3 ? 0 : median < 66.7 ? 1 : 2
  const team = events[0].teamId
  const attacksRight = team === home.id
  return (
    <rect x={attacksRight ? sx(55) : 0} y={sy(band * 33.33)} width={sx(45)} height={sy(33.33)} fill={colorOf(team)} opacity={0.12} />
  )
}
