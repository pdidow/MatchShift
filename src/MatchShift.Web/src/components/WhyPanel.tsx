import { useRef } from 'react'
import type { ReactNode } from 'react'
import type { InsightCard } from '../api'
import type { Strings } from '../i18n'
import { useDialog } from '../a11y/A11yContext'
import { Confidence } from './InsightCard'

interface Props {
  card: InsightCard
  s: Strings
  onClose: () => void
  onShow: () => void
  onReplay: () => void
  /** Optional accessibility actions (READ EXPLANATION / READ WHAT CHANGED), shown only when speech is enabled. */
  extraActions?: ReactNode
}

/** WHY? - the deterministic evidence behind an insight, before any wording. */
export function WhyPanel({ card, s, onClose, onShow, onReplay, extraActions }: Props) {
  const n = card.narrative
  const primary = n.evidence.find((e) => e.isPrimary) ?? n.evidence[0]
  const ref = useRef<HTMLDivElement>(null)
  // Focus moves into the dialog, Tab stays inside, Escape closes, focus returns to the card.
  useDialog(ref, onClose)

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal" role="dialog" aria-modal="true" aria-labelledby="why-title" ref={ref} onClick={(e) => e.stopPropagation()}>
        <div className="card-top">
          <span className="card-kicker">{s.why} · {n.typeLabel} · {card.minute}'</span>
          <button className="ghost" onClick={onClose} aria-label={s.close}>✕</button>
        </div>
        <h2 id="why-title">{n.headline}</h2>
        <p className="card-fact">{n.fact}</p>

        {primary && (
          <div className="why-hero">
            <div>
              <div className="k">{primary.label}</div>
              <div className="k">{primary.baselineWindow ?? ''}</div>
              <div className="v">{primary.baseline ?? '—'}</div>
            </div>
            <div className="v" style={{ color: 'var(--ink-muted)' }}>→</div>
            <div>
              <div className="k">&nbsp;</div>
              <div className="k">{primary.recentWindow ?? ''}</div>
              <div className="v">{primary.recent}</div>
            </div>
            <div>
              <div className="k">{s.change}</div>
              <div className="v" style={{ color: 'var(--accent)' }}>{primary.change ?? '—'}</div>
            </div>
          </div>
        )}

        <div className="why-checks">
          <div className="why-check"><div className="k">{s.threshold}</div><div className="v">{primary?.threshold ?? '—'}</div></div>
          <div className="why-check">
            <div className="k">{s.sample}</div>
            <div className="v">
              {primary ? `${primary.sampleSize}${primary.minimumSample ? ` / ${primary.minimumSample}` : ''} · ${primary.sampleRequirementMet ? s.passed : s.notMet}` : '—'}
            </div>
          </div>
          <div className="why-check"><div className="k">{s.confidence}</div><div className="v"><Confidence label={n.confidenceLabel} level={card.confidence} /></div></div>
          <div className="why-check"><div className="k">{s.evidence}</div><div className="v">{card.supportingEventCount} {s.supportingEvents}</div></div>
        </div>

        <table className="evidence">
          <caption className="sr-only">{s.evidence}</caption>
          <thead>
            <tr><th scope="col">{s.metric}</th><th scope="col">{s.before}</th><th scope="col">{s.now}</th><th scope="col">{s.change}</th><th scope="col">{s.threshold}</th><th scope="col">{s.sample}</th></tr>
          </thead>
          <tbody>
            {n.evidence.map((e) => (
              <tr key={e.metric} className={e.isPrimary ? 'primary' : ''}>
                <td>{e.label}{e.recentWindow && <div className="note" style={{ margin: 0 }}>{e.baselineWindow ? `${e.baselineWindow} → ` : ''}{e.recentWindow}</div>}</td>
                <td>{e.baseline ?? '—'}</td>
                <td>{e.recent}</td>
                <td>{e.change ?? '—'}</td>
                <td>{e.threshold ?? '—'}</td>
                <td>{e.sampleSize || '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>

        <p className="card-sub" style={{ marginTop: 14 }}><b>{s.inference}</b>{n.inference}</p>
        {n.explanation && <p className="card-sub"><b>{s.possibleExplanation}</b>{n.explanation}</p>}
        <p className="note">
          {primary?.comparison ? `${primary.comparison} · ` : ''}{s.derived}
          {n.verificationNote ? ` · ${n.verificationNote}` : ''} · {n.source}
        </p>
        <div className="card-actions">
          <button className="primary" onClick={onShow}>{s.showMe}</button>
          <button className="chip replay-btn" onClick={onReplay}>{s.replay.button}</button>
          {extraActions}
          <button onClick={onClose}>{s.close}</button>
        </div>
      </div>
    </div>
  )
}
