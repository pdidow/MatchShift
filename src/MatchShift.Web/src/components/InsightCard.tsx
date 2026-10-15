import type { InsightCard as Card, ViewerMode } from '../api'
import type { Strings } from '../i18n'

interface Props {
  card: Card
  mode: ViewerMode
  s: Strings
  selected: boolean
  teamName: (id?: string) => string
  onWhy: () => void
  onShow: () => void
  onSelect: () => void
}

const SHIFT_TYPES = new Set(['ATTACK_CHANNEL_SHIFT', 'POST_SUBSTITUTION_CHANGE', 'CONTROL_SHIFT'])

export function InsightCardView({ card, mode, s, selected, teamName, onWhy, onShow, onSelect }: Props) {
  const n = card.narrative
  const primary = n.evidence.find((e) => e.isPrimary)
  const compact = mode === 'WhatMatters'
  const studio = mode === 'Studio'

  return (
    <article
      className={`card ${SHIFT_TYPES.has(card.type) ? 'shift' : ''} ${selected ? 'selected' : ''}`}
      onClick={onSelect}
      aria-label={n.headline}
    >
      <div className="card-top">
        <span className="card-kicker">
          {n.typeLabel}
          {card.teamId && <span style={{ color: 'var(--ink-muted)' }}>· {teamName(card.teamId)}</span>}
        </span>
        <span className="card-minute">{card.minute}'</span>
      </div>

      <h3 className="card-headline">{n.headline}</h3>

      {primary?.baseline && !compact && (
        <div className="key-number" aria-label={`${primary.label}: ${primary.baseline} → ${primary.recent}`}>
          <span>{primary.baseline}</span>
          <span className="arrow">→</span>
          <span>{primary.recent}</span>
          <span className="label">{primary.label}</span>
        </div>
      )}

      {studio ? (
        <>
          <p className="card-studio">{n.studioLine}</p>
          <p className="card-sub"><b>{s.talkingPoint}</b>{n.inference}</p>
        </>
      ) : compact ? (
        <p className="card-fact">{n.fact}</p>
      ) : (
        <>
          <p className="card-fact">{n.fact}</p>
          <p className="card-sub"><b>{s.inference}</b>{n.inference}</p>
          {n.explanation && <p className="card-sub"><b>{s.possibleExplanation}</b>{n.explanation}</p>}
        </>
      )}

      {n.teaching && <div className="card-teach"><strong>{s.football101}: </strong>{n.teaching}</div>}
      {n.perspective && <p className="card-perspective">{n.perspective}</p>}
      {n.playerLine && <p className="card-sub">{n.playerLine}</p>}

      <div className="card-actions">
        <button className="chip" onClick={(e) => { e.stopPropagation(); onWhy() }}>{s.why}</button>
        <button className="chip" onClick={(e) => { e.stopPropagation(); onShow() }}>{s.showMe}</button>
        <span className="spacer" />
        <Confidence label={`${s.confidence}: ${n.confidenceLabel}`} level={card.confidence} />
      </div>
    </article>
  )
}

export function Confidence({ label, level }: { label: string; level: 'Low' | 'Medium' | 'High' }) {
  const on = level === 'High' ? 3 : level === 'Medium' ? 2 : 1
  return (
    <span className="conf" title={label}>
      <span className="conf-dots" aria-hidden>{[1, 2, 3].map((i) => <i key={i} className={i <= on ? 'on' : ''} />)}</span>
      {label}
    </span>
  )
}
