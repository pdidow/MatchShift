import type { Strings } from '../i18n'
import type { Lang } from '../languages'
import { LANGUAGES } from '../languages'
import { a11yStrings } from '../a11y/strings'
import type { GuideStep } from './guide'
import { GUIDE_STEPS, VIEWER_FEATURES, nextStep, prevStep } from './guide'

interface Props {
  step: GuideStep
  s: Strings
  /** Performs the next step's real action (open WHY?, SHOW ME, Evidence Replay, Agents...). */
  onGo: (step: GuideStep) => void
  onExit: () => void
  /** Opens the real Accessibility Center (the guide never changes a setting itself). */
  onOpenA11y: () => void
  /** The viewer's language, for the Accessibility Center's own feature labels. */
  lang: Lang
}

/**
 * The guided demo panel: a small docked panel over the real product. It explains what is on screen and offers the next
 * real action; it never clicks anything on its own. Steps also advance when the viewer uses the product directly.
 */
export function JudgeGuide({ step, s, onGo, onExit, onOpenA11y, lang }: Props) {
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
      {step === 'viewers' && <ViewersPanel s={s} lang={lang} onOpenA11y={onOpenA11y} />}
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

/**
 * "Built for every viewer": the six languages (each name tagged with its own language for screen readers) and the real
 * Accessibility Center's options. The highlighted Language menu and Accessibility button stay the real controls.
 */
function ViewersPanel({ s, lang, onOpenA11y }: { s: Strings; lang: Lang; onOpenA11y: () => void }) {
  const v = s.guide.viewersStep
  const as = a11yStrings(lang)
  const features = [...VIEWER_FEATURES.map(k => as.opt[k][0]), as.keyboardHelp]
  return (
    <dl className="guide-viewers">
      <div>
        <dt>{v.languages}</dt>
        <dd>
          <ul className="guide-langs">{LANGUAGES.map(l => <li key={l.code} lang={l.code}>{l.name}</li>)}</ul>
          <span className="guide-hint">{v.languagesHint}</span>
        </dd>
      </div>
      <div>
        <dt>{as.title}</dt>
        <dd>
          <span>{features.join(' · ')}</span>
          <button type="button" className="chip guide-a11y" onClick={onOpenA11y} aria-haspopup="dialog">{v.open}</button>
        </dd>
      </div>
    </dl>
  )
}
