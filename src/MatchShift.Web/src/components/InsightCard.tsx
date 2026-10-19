import { useState } from 'react'
import type { InsightCard as Card, ViewerMode } from '../api'
import type { Strings } from '../i18n'
import { AgentInsight } from './AgentWorkflow'

interface Props {
  card: Card
  mode: ViewerMode
  s: Strings
  selected: boolean
  teamName: (id?: string) => string
  onWhy: () => void
  onShow: () => void
  onReplay: () => void
  onSelect: () => void
  /** Simplified Insights (optional): a plain-language sentence shown instead of the full card until MORE DETAIL. */
  simplified?: { text: string; more: string; less: string } | null
  /** Guided demo: this card is the flagship insight being explained. */
  guideTarget?: boolean
  /** Guided demo: the next evidence action to point at on this card. */
  guideNext?: 'why' | 'show' | 'replay' | null
}

const SHIFT_TYPES = new Set(['ATTACK_CHANNEL_SHIFT', 'POST_SUBSTITUTION_CHANGE', 'CONTROL_SHIFT'])

export function InsightCardView({ card, mode, s, selected, teamName, onWhy, onShow, onReplay, onSelect, simplified, guideTarget, guideNext }: Props) {
  const n = card.narrative
  const primary = n.evidence.find((e) => e.isPrimary)
  const compact = mode === 'WhatMatters'
  const studio = mode === 'Studio'
  const [expanded, setExpanded] = useState(false)
  const simple = !!simplified && !expanded
  const headingId = `card-h-${card.id}`

  return (
    <article
      id={`card-${card.id}`}
      className={`card ${SHIFT_TYPES.has(card.type) ? 'shift' : ''} ${selected ? 'selected' : ''} ${simple ? 'simple' : ''} ${guideTarget ? 'guide-card' : ''}`}
      onClick={onSelect}
      // Keyboard: the card itself is focusable; Enter or Space selects it (buttons inside keep their own keys).
      tabIndex={0}
      aria-labelledby={headingId}
      aria-current={selected ? 'true' : undefined}
      onKeyDown={(e) => {
        if (e.target !== e.currentTarget) return
        if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onSelect() }
      }}
    >
      <div className="card-top">
        <span className="card-kicker">
          {n.typeLabel}
          {card.teamId && <span style={{ color: 'var(--ink-muted)' }}>· {teamName(card.teamId)}</span>}
        </span>
        <span className="card-minute">{card.minute}'</span>
      </div>

      <small className="narrative-source" title={n.verificationNote}>{n.source === 'mock' ? 'Mock — no model call' : n.source === 'microsoft-foundry' ? 'Microsoft Foundry — verified selection' : 'Deterministic MatchShift wording'}</small>
      <h3 className="card-headline" id={headingId}>{n.headline}</h3>

      {simple ? <p className="card-simple">{simplified!.text}</p> : <>
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

      <AgentInsight agent={card.agent} s={s} compact={compact || studio} />
      {n.teaching && <div className="card-teach"><strong>{s.football101}: </strong>{n.teaching}</div>}
      {n.perspective && <p className="card-perspective">{n.perspective}</p>}
      {n.playerLine && <p className="card-sub">{n.playerLine}</p>}
      </>}
      {simplified && (
        <button className="ghost more-detail" aria-expanded={expanded} onClick={(e) => { e.stopPropagation(); setExpanded(v => !v) }}>
          {expanded ? simplified.less : simplified.more}
        </button>
      )}

      <div className="card-actions">
        {/* The evidence chain: each step goes one level closer to the raw match events. */}
        <div className="evidence-chain" role="group" aria-label={s.chain}>
          <button className={`chip ${guideNext === 'why' ? 'guide-target' : ''}`} onClick={(e) => { e.stopPropagation(); onWhy() }}>{s.why}</button>
          <span className="chain-sep" aria-hidden>›</span>
          <button className={`chip ${guideNext === 'show' ? 'guide-target' : ''}`} onClick={(e) => { e.stopPropagation(); onShow() }}>{s.showMe}</button>
          <span className="chain-sep" aria-hidden>›</span>
          <button className={`chip replay-btn ${guideNext === 'replay' ? 'guide-target' : ''}`} onClick={(e) => { e.stopPropagation(); onReplay() }}>{s.replay.button}</button>
        </div>
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
