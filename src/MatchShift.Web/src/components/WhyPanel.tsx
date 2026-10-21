import { useRef } from 'react'
import type { ReactNode } from 'react'
import type { InsightCard } from '../api'
import type { Strings } from '../i18n'
import { useDialog } from '../a11y/A11yContext'
import { Confidence } from './InsightCard'
import { AgentInsight } from './AgentWorkflow'
import { basisLines, confidenceScore, evidenceCounts, primaryEvidence } from './insightBasis'
import { countLabel } from './agentExplain'

interface Props {
  card: InsightCard
  s: Strings
  onClose: () => void
  onShow: () => void
  onReplay: () => void
  /** Optional secondary trust link: opens the Agents tab for this insight. */
  onAgents?: () => void
  /** Optional accessibility actions (READ EXPLANATION / READ WHAT CHANGED), shown only when speech is enabled. */
  extraActions?: ReactNode
  /** Guided demo: point at SHOW ME as the next step. */
  guideNext?: boolean
}

/**
 * WHY? answers "Why is MatchShift telling me this?": first why it was flagged (the deterministic detection basis,
 * confidence, importance and evidence), then what it may mean (fact, inference and possible explanation, never mixed),
 * then every measurement, collapsed.
 */
export function WhyPanel({ card, s, onClose, onShow, onReplay, onAgents, extraActions, guideNext }: Props) {
  const n = card.narrative
  const i = s.insight
  const primary = primaryEvidence(card)
  const counts = evidenceCounts(card)
  const ref = useRef<HTMLDivElement>(null)
  // Focus moves into the dialog, Tab stays inside, Escape closes, focus returns to the card.
  useDialog(ref, onClose)
  const source = n.source === 'mock' ? s.sources.mock : n.source === 'microsoft-foundry' ? s.sources.foundry : s.sources.template

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal why" role="dialog" aria-modal="true" aria-labelledby="why-title" ref={ref} onClick={(e) => e.stopPropagation()}>
        <div className="card-top">
          <span className="card-kicker">{s.why} · {n.typeLabel} · {card.minute}'</span>
          <button className="ghost" onClick={onClose} aria-label={s.close}>✕</button>
        </div>
        <h2 id="why-title">{n.headline}</h2>

        <section aria-labelledby="why-flagged">
          <h3 id="why-flagged" className="why-h">{i.flaggedTitle}</h3>
          <p className="layer-label">{s.whyFacts}</p>
          {primary && (
            <div className="why-hero">
              <div>
                <div className="k">{primary.label}</div>
                <div className="k">{primary.baselineWindow ?? ''}</div>
                <div className="v">{primary.baseline ?? '—'}</div>
              </div>
              <div className="v" style={{ color: 'var(--ink-muted)' }} aria-hidden>→</div>
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
          <ul className="why-basis">
            {basisLines(card, s).map((line) => <li key={line}><span aria-hidden>✓ </span>{line}</li>)}
          </ul>

          <dl className="why-facts">
            <div>
              <dt>{s.confidence}</dt>
              <dd><Confidence label={n.confidenceLabel} level={card.confidence} /> <span className="mono">({confidenceScore(card, s)})</span></dd>
              <dd className="why-explain">{i.confidenceBasis} {i.confidenceScale[card.confidence]}</dd>
            </div>
            <div>
              <dt>{i.importance}</dt>
              <dd>{Math.round(card.importance)} / 100</dd>
              <dd className="why-explain">{i.importanceBasis}</dd>
            </div>
            <div>
              <dt>{s.evidence}</dt>
              <dd>{s.supportingLegend.replace('{n}', String(counts.supporting))}</dd>
              {counts.context > 0 && <dd className="why-explain">+ {countLabel(s.explain.contextCount, counts.context)}: {i.contextNote}</dd>}
            </div>
          </dl>
        </section>

        <section aria-labelledby="why-meaning">
          <h3 id="why-meaning" className="why-h">{i.meaningTitle}</h3>
          <dl className="why-claims">
            <div className="claim claim-fact">
              <dt><span aria-hidden>● </span>{i.fact} <small>· {i.factHint}</small></dt>
              <dd>{n.fact}</dd>
            </div>
            <div className="claim claim-inference">
              <dt><span aria-hidden>◐ </span>{i.inference} <small>· {i.inferenceHint}</small></dt>
              <dd>{n.inference}</dd>
            </div>
            {n.explanation && (
              <div className="claim claim-explanation">
                <dt><span aria-hidden>○ </span>{i.explanation} <small>· {i.explanationHint}</small></dt>
                <dd>{n.explanation}</dd>
              </div>
            )}
          </dl>
          <AgentInsight agent={card.agent} s={s} />
          {card.agent?.verified && card.agent.evidenceIds.length > 0 && (
            <p className="note">{s.evidence}: {card.agent.evidenceIds.map((id) => id.replace(/^.*-(E\d+)$/, '$1')).join(', ')}</p>
          )}
        </section>

        <details className="why-all">
          <summary>{i.allMeasurements}</summary>
          <table className="evidence">
            <caption className="sr-only">{i.allMeasurements}</caption>
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
          <p className="note">
            {primary?.comparison ? `${primary.comparison} · ` : ''}{s.derived}
            {n.verificationNote ? ` · ${n.verificationNote}` : ''} · {source}
          </p>
        </details>

        <div className="card-actions">
          <div className="evidence-chain" role="group" aria-label={s.chain}>
            <button className={`primary ${guideNext ? 'guide-target' : ''}`} onClick={onShow}>{s.showMe}</button>
            <span className="chain-sep" aria-hidden>›</span>
            <button className="chip replay-btn" onClick={onReplay}>{s.replay.button}</button>
          </div>
          {onAgents && <button className="ghost trust-link" onClick={onAgents}>{i.howVerified}</button>}
          {extraActions}
          <button onClick={onClose}>{s.close}</button>
        </div>
      </div>
    </div>
  )
}
