import { beforeAll, describe, expect, it, vi } from 'vitest'
import { createElement, isValidElement } from 'react'
import type { ReactElement, ReactNode } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { t } from '../i18n'
import { LANGUAGES, LANGUAGE_CODES } from '../languages'
import type { Lang } from '../languages'
import { a11yStrings } from '../a11y/strings'
import { GUIDE_START, GUIDE_STEPS, VIEWER_FEATURES, nextStep, prevStep, wantsGuideFromUrl } from './guide'
import type { GuideStep } from './guide'
import appSource from '../App.tsx?raw'

// "Built for every viewer": the guide step that shows the six languages and the real Accessibility Center. Rendered to
// static markup and, for clicks, by calling the (hook-free) guide components directly. No network, no Azure.

vi.stubGlobal('location', { search: '' })

let JudgeGuide: typeof import('./JudgeGuide').JudgeGuide
beforeAll(async () => { JudgeGuide = (await import('./JudgeGuide')).JudgeGuide })

interface Calls { go: GuideStep[]; exit: number; a11y: number }
const props = (step: GuideStep, lang: Lang = 'en', calls: Calls = { go: [], exit: 0, a11y: 0 }) => ({
  step, s: t(lang), lang,
  onGo: (s: GuideStep) => { calls.go.push(s) }, onExit: () => { calls.exit++ }, onOpenA11y: () => { calls.a11y++ },
})
const html = (step: GuideStep, lang: Lang = 'en') => renderToStaticMarkup(createElement(JudgeGuide, props(step, lang)))

/** Every native button in the rendered tree (function components expanded), with its text and click handler. */
function buttons(node: ReactNode): { text: string; label?: string; onClick: () => void }[] {
  const out: { text: string; label?: string; onClick: () => void }[] = []
  const text = (n: ReactNode): string => typeof n === 'string' || typeof n === 'number' ? String(n)
    : Array.isArray(n) ? n.map(text).join('') : isValidElement(n) ? text((n.props as { children?: ReactNode }).children) : ''
  const walk = (n: ReactNode) => {
    if (Array.isArray(n)) return n.forEach(walk)
    if (!isValidElement(n)) return
    const el = n as ReactElement<Record<string, unknown>>
    if (typeof el.type === 'function') return walk((el.type as (p: unknown) => ReactNode)(el.props))
    if (el.type === 'button') out.push({ text: text(el.props.children as ReactNode), label: el.props['aria-label'] as string | undefined, onClick: el.props.onClick as () => void })
    walk(el.props.children as ReactNode)
  }
  walk(node)
  return out
}
const press = (step: GuideStep, match: RegExp, calls: Calls) => {
  const b = buttons(JudgeGuide(props(step, 'en', calls))).find(x => match.test(x.text) || match.test(x.label ?? ''))
  expect(b, `button ${match} on ${step}`).toBeDefined()
  b!.onClick()
}

// The App's own guide wiring, checked in its source (the App needs a live API to render).
const goGuideBody = appSource.slice(appSource.indexOf('const goGuide = (step: GuideStep) => {'), appSource.indexOf('// The guide also follows'))

describe('one guide, every entry point', () => {
  it('1, 2, 3: ?demo=judge and the Guided demo buttons all start the same sequence, which includes the viewer step', () => {
    expect(wantsGuideFromUrl('?demo=judge')).toBe(true)
    expect(GUIDE_START).toBe('intro')
    expect(nextStep(GUIDE_START)).toBe('viewers')
    expect(appSource).toContain('wantsGuideFromUrl(window.location.search) ? GUIDE_START : null')
    const entries = appSource.match(/className="chip guide-entry" onClick=\{\(\) => goGuide\(([^)]*)\)\}/g) ?? []
    expect(entries.length).toBe(2)                                     // top bar + empty-state link
    for (const e of entries) expect(e).toContain('goGuide(GUIDE_START)')
    expect(appSource).not.toMatch(/goGuide\('(insight|why|show|replay|agents|trust)'\)/)   // no entry skips a step
  })

  it('14, 15: the flagship flow after it is unchanged', () => {
    expect(GUIDE_STEPS.slice(2)).toEqual(['insight', 'why', 'show', 'replay', 'agents', 'trust'])
    expect(nextStep('viewers')).toBe('insight')
    expect(prevStep('insight')).toBe('viewers')
    expect(html('viewers')).toContain('Jump to the flagship moment')  // its action performs the existing flagship jump
    expect(goGuideBody).toContain("if (step === 'intro' || step === 'viewers') return")
    expect(goGuideBody).toContain('await api.reset(FLAGSHIP.matchId); await api.seek(FLAGSHIP.seekMinute)')
  })
})

describe('Built for every viewer', () => {
  it('4, 5: highlights the real Language menu and the real Accessibility button only during this step', () => {
    expect(appSource).toContain("<label className={`control-label ${guide === 'viewers' ? 'guide-target' : ''}`}>{s.language}")
    expect(appSource).toContain("<A11yButton lang={profile.language} onOpen={() => setCenterOpen(true)} highlight={guide === 'viewers'} />")
    expect(appSource).toMatch(/<select value=\{profile\.language\} onChange=/)          // still the real, usable dropdown
  })

  it('6: names all six languages, each tagged with its own language', () => {
    const out = html('viewers')
    expect(LANGUAGES.map(l => l.name)).toEqual(['English', 'Français', 'Español', 'Deutsch', 'Italiano', 'Português'])
    for (const l of LANGUAGES) expect(out).toContain(`<li lang="${l.code}">${l.name}</li>`)
    expect(out).toContain('Six languages')
  })

  it('names only real Accessibility Center options, with the Center\'s own labels', () => {
    const out = html('viewers')
    const as = a11yStrings('en')
    for (const k of VIEWER_FEATURES) expect(out).toContain(as.opt[k][0].replace(/&/g, '&amp;'))
    for (const f of ['Spoken insights', 'Audio match mode', 'Evidence Replay audio description', 'Captions &amp; transcript', 'High contrast',
      'Colour vision mode', 'Reduced motion', 'Reduce flashing', 'Text size', 'Simplified insights', 'Keyboard shortcuts']) expect(out).toContain(f)
  })

  it('7, 8: the guide never changes the language or an accessibility setting by itself', () => {
    const calls: Calls = { go: [], exit: 0, a11y: 0 }
    for (const b of buttons(JudgeGuide(props('viewers', 'en', calls)))) b.onClick()
    expect(calls.go).toEqual(['insight'])                              // only guide navigation, the Center opening and exit
    expect(calls.a11y).toBe(1)
    expect(goGuideBody).not.toMatch(/update\(|setPrefs|setProfile|applyPreset|language/)
    expect(Object.keys(props('viewers'))).toEqual(['step', 's', 'lang', 'onGo', 'onExit', 'onOpenA11y']) // no setter is handed in
  })

  it('9: opens the real Accessibility Center from a native button', () => {
    const out = html('viewers')
    expect(out).toMatch(/<button type="button" class="chip guide-a11y" aria-haspopup="dialog">Open Accessibility Center<\/button>/)
    const calls: Calls = { go: [], exit: 0, a11y: 0 }
    press('viewers', /^Open Accessibility Center$/, calls)
    expect(calls.a11y).toBe(1)
    expect(appSource).toContain('onOpenA11y={() => setCenterOpen(true)}')
    expect(appSource).toContain('{centerOpen && <AccessibilityCenter')                   // the real panel, not a preview
  })

  it('10, 11, 12: after the Center, Next continues, Back returns, Exit leaves', () => {
    const calls: Calls = { go: [], exit: 0, a11y: 0 }
    press('viewers', /^Open Accessibility Center$/, calls)               // the Center is a separate dialog; the guide stays
    press('viewers', /Jump to the flagship moment/, calls)
    press('insight', /^Back$/, calls)
    press('viewers', /^Exit demo$/, calls)
    expect(calls).toEqual({ go: ['insight', 'viewers'], exit: 1, a11y: 1 })
    press('intro', /^Start/, calls)
    expect(calls.go.at(-1)).toBe('viewers')
  })

  it('13: every new string exists in all six languages, and the step is localized', () => {
    const seen = new Set<string>()
    for (const lang of LANGUAGE_CODES) {
      const g = t(lang).guide
      const parts = [g.steps.viewers.title, g.steps.viewers.body, g.steps.viewers.action, g.steps.intro.action,
        g.viewersStep.languages, g.viewersStep.languagesHint, g.viewersStep.open]
      for (const p of parts) expect(p.trim().length, `${lang}`).toBeGreaterThan(1)
      if (lang !== 'en') expect(g.steps.viewers.title).not.toBe(t('en').guide.steps.viewers.title)
      seen.add(g.viewersStep.open)
      const out = html('viewers', lang)
      expect(out).toContain(g.steps.viewers.title)
      expect(out).toContain(a11yStrings(lang).opt.highContrast[0])     // the Center's labels in that language
      expect(out).toContain(g.stepOf.replace('{n}', '2').replace('{total}', '8'))
    }
    expect(seen.size).toBe(6)
  })

  it('stays accessible and calm: native buttons, a polite live title, no animation classes', () => {
    const out = html('viewers')
    expect(out).toContain('aria-live="polite"')
    expect(out).toContain('<h2 class="guide-title">Built for every viewer</h2>')
    expect(out).not.toMatch(/pulse|glow|blink|animate/i)
    expect(buttons(JudgeGuide(props('viewers'))).map(b => b.label ?? b.text)).toEqual(['Exit demo', 'Open Accessibility Center', 'Jump to the flagship moment →'])
  })
})
