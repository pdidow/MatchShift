import { useId, useRef } from 'react'
import type { Lang } from '../api'
import { useA11y, useDialog } from './A11yContext'
import type { A11yPrefs, PresetId } from './prefs'
import { applyPreset, matchingPreset } from './prefs'
import { a11yStrings, speechLang } from './strings'

interface Props {
  lang: Lang
  onClose: () => void
  onKeyboardHelp: () => void
}

type BoolKey = { [K in keyof A11yPrefs]: A11yPrefs[K] extends boolean ? K : never }[keyof A11yPrefs]

/** Accessibility Center: presets plus independent, persisted options. Nothing here is on by default. */
export function AccessibilityCenter({ lang, onClose, onKeyboardHelp }: Props) {
  const { prefs, update, setPrefs, speech, speechState, systemReducedMotion } = useA11y()
  const s = a11yStrings(lang)
  const ref = useRef<HTMLDivElement>(null)
  useDialog(ref, onClose)
  const preset = matchingPreset(prefs)
  const titleId = useId()

  // Render helpers (plain functions, not components, so inputs keep focus across re-renders).
  const toggle = (k: BoolKey) => {
    const [label, hint] = s.opt[k as keyof typeof s.opt] ?? [k, '']
    const id = `a11y-${k}`
    return (
      <div className="a11y-row">
        <label htmlFor={id} className="a11y-label">
          <span>{label}</span>
          {hint && <small id={`${id}-hint`}>{hint}</small>}
        </label>
        <input id={id} type="checkbox" role="switch" className="a11y-switch" checked={prefs[k]}
          aria-describedby={hint ? `${id}-hint` : undefined} onChange={(e) => update({ [k]: e.target.checked } as Partial<A11yPrefs>)} />
      </div>
    )
  }

  function choice<K extends keyof A11yPrefs>(k: K, options: [A11yPrefs[K], string][]) {
    const [label, hint] = s.opt[k as keyof typeof s.opt] ?? [String(k), '']
    const id = `a11y-${String(k)}`
    return (
      <div className="a11y-row">
        <label htmlFor={id} className="a11y-label">
          <span>{label}</span>
          {hint && <small id={`${id}-hint`}>{hint}</small>}
        </label>
        <select id={id} value={String(prefs[k])} aria-describedby={hint ? `${id}-hint` : undefined}
          onChange={(e) => update({ [k]: options.find(o => String(o[0]) === e.target.value)?.[0] } as Partial<A11yPrefs>)}>
          {options.map(([v, text]) => <option key={String(v)} value={String(v)}>{text}</option>)}
        </select>
      </div>
    )
  }

  const slider = (k: 'speechRate' | 'speechVolume', min: number, max: number, step: number, format: (v: number) => string) => {
    const id = `a11y-${k}`
    return (
      <div className="a11y-row">
        <label htmlFor={id} className="a11y-label"><span>{s.opt[k][0]}</span></label>
        <span className="a11y-slider">
          <input id={id} type="range" min={min} max={max} step={step} value={prefs[k]} aria-valuetext={format(prefs[k])}
            onChange={(e) => update({ [k]: Number(e.target.value) } as Partial<A11yPrefs>)} />
          <output htmlFor={id}>{format(prefs[k])}</output>
        </span>
      </div>
    )
  }

  const presets: Exclude<PresetId, 'custom'>[] = ['default', 'lowVision', 'audioFirst', 'reducedMotion']

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal a11y-center" role="dialog" aria-modal="true" aria-labelledby={titleId} ref={ref} onClick={(e) => e.stopPropagation()}>
        <div className="card-top">
          <h2 id={titleId}>{s.title}</h2>
          <button className="ghost" onClick={onClose} aria-label={s.close}>✕</button>
        </div>
        <p className="a11y-intro">{s.intro}</p>

        <fieldset className="a11y-presets">
          <legend>{s.presets}</legend>
          {presets.map(id => (
            <button key={id} type="button" className={`a11y-preset ${preset === id ? 'active' : ''}`} aria-pressed={preset === id}
              data-autofocus={preset === id ? true : undefined} onClick={() => setPrefs(p => applyPreset(id, p))}>
              <strong>{s.preset[id]}</strong>
              <small>{s.presetHint[id]}</small>
            </button>
          ))}
          <div className={`a11y-preset static ${preset === 'custom' ? 'active' : ''}`} aria-hidden={preset !== 'custom'}>
            <strong>{s.preset.custom}</strong>
            <small>{s.presetHint.custom}</small>
          </div>
        </fieldset>

        <div className="a11y-grid">
          <section aria-labelledby="a11y-audio">
            <h3 id="a11y-audio">{s.sections.audio}</h3>
            {toggle('spokenInsights')}
            {choice('speechDetail', [['major', s.detail.major], ['important', s.detail.important], ['detailed', s.detail.detailed]])}
            {toggle('audioMatchMode')}
            {choice('periodicRecap', [['off', s.periodic.off], ['5', s.periodic['5']], ['10', s.periodic['10']], ['halftime', s.periodic.halftime], ['shifts', s.periodic.shifts]])}
            {toggle('replayAudioDescription')}
            {toggle('describeVisuals')}
            {slider('speechRate', 0.5, 2, 0.1, (v) => `${v.toFixed(1)}×`)}
            {slider('speechVolume', 0, 1, 0.1, (v) => `${Math.round(v * 100)}%`)}
            {toggle('soundCues')}
            <div className="a11y-row">
              <button type="button" onClick={() => speech.say({ text: s.testPhrase, kind: 'test', lang: speechLang(lang), priority: 1 }, { allowRepeat: true })}>
                {s.testVoice}
              </button>
              {!speechState.supported && <small className="a11y-warn">{s.speech.unsupported}</small>}
              {speechState.missingVoice && <small className="a11y-warn">{s.speech.noVoice}</small>}
            </div>
          </section>

          <section aria-labelledby="a11y-display">
            <h3 id="a11y-display">{s.sections.display}</h3>
            {toggle('transcript')}
            {toggle('highContrast')}
            {choice('colorVision', [['default', s.cvd.default], ['deuteranopia', s.cvd.deuteranopia], ['protanopia', s.cvd.protanopia], ['tritanopia', s.cvd.tritanopia], ['monochrome', s.cvd.monochrome]])}
            {choice('textSize', [['normal', s.text.normal], ['large', s.text.large], ['xl', s.text.xl]])}

            <h3>{s.sections.motion}</h3>
            {choice('reducedMotion', [['system', `${s.motion.system} (${systemReducedMotion ? s.motion.on : s.motion.off})`], ['on', s.motion.on], ['off', s.motion.off]])}
            {toggle('reduceFlashing')}

            <h3>{s.sections.content}</h3>
            {toggle('simplified')}

            <h3>{s.sections.help}</h3>
            <div className="a11y-row"><button type="button" onClick={onKeyboardHelp}>{s.keyboardHelp} (?)</button></div>
          </section>
        </div>
      </div>
    </div>
  )
}
