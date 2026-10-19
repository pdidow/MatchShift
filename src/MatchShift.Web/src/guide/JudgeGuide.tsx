import type { Strings } from '../i18n'
import type { GuideStep } from './guide'
import { GUIDE_STEPS, nextStep, prevStep } from './guide'

interface Props {
  step: GuideStep
  s: Strings
  /** Performs the next step's real action (open WHY?, SHOW ME, Evidence Replay, Agents...). */
  onGo: (step: GuideStep) => void
  onExit: () => void
}

/**
 * The guided demo panel: a small docked panel over the real product. It explains what is on screen and offers the next
 * real action; it never clicks anything on its own. Steps also advance when the viewer uses the product directly.
 */
export function JudgeGuide({ step, s, onGo, onExit }: Props) {
  const g = s.guide
  const text = g.steps[step]
  const n = GUIDE_STEPS.indexOf(step) + 1
  const next = nextStep(step)
  const back = prevStep(step)
  return (
    <section className="guide" role="region" aria-label={g.label} data-step={step}>
      <div className="guide-top">
        <span className="guide-kicker">{g.label} · {g.stepOf.replace('{n}', String(n)).replace('{total}', String(GUIDE_STEPS.length))}</span>
        <button className="ghost guide-exit" onClick={onExit} aria-label={g.exit} title={g.exit}>✕</button>
      </div>
      <ol className="guide-progress" aria-hidden>
        {GUIDE_STEPS.map((k, i) => <li key={k} className={i < n ? 'done' : ''} />)}
      </ol>
      {/* Polite live region: each step's title and text are announced once when the step changes. */}
      <div aria-live="polite" aria-atomic="true">
        <h2 className="guide-title">{text.title}</h2>
        <p className="guide-body">{text.body}</p>
      </div>
      {step === 'insight' && (
        <dl className="guide-layers">
          <div className="facts"><dt>{g.layers.facts}</dt><dd>{g.layers.factsItems}</dd></div>
          <div className="ai"><dt>{g.layers.ai}</dt><dd>{g.layers.aiItems}</dd></div>
          <div className="verify"><dt>{g.layers.verify}</dt><dd>{g.layers.verifyItems}</dd></div>
        </dl>
      )}
      <div className="guide-actions">
        {back && back !== 'intro' && <button onClick={() => onGo(back)}>{g.back}</button>}
        <span className="spacer" />
        {next
          ? <button className="primary" onClick={() => onGo(next)}>{text.action} →</button>
          : <button className="primary" onClick={onExit}>{g.finish}</button>}
      </div>
    </section>
  )
}
