import { describe, expect, it } from 'vitest'
import type { Strings } from './i18n'
import { t } from './i18n'
import { LANGUAGE_CODES, LANGUAGES, isLang, localeOf } from './languages'
import { a11yStrings, speechLang } from './a11y/strings'
import { chooseVoice, SpeechQueue } from './a11y/speech'
import type { SynthLike, UtteranceLike, VoiceLike } from './a11y/speech'
import { evidenceLegend, splitEvidence } from './evidence'
import type { AgentRuntimeStatus, InsightEvents, PitchEvent, WorkflowExecution } from './api'
import { GUIDE_STEPS, chipLabels, modeLabel, publishedNotice, runtimeChips, verdict } from './guide/guide'
import { performanceLabels } from './components/PlayerPanel'

// German, Italian and European Portuguese join English, French and Spanish. No test here needs installed voices or Azure.

const NEW = ['de', 'it', 'pt'] as const

/** Every leaf string path of an object, e.g. "guide.steps.why.title". */
function leaves(o: unknown, prefix = ''): Map<string, unknown> {
  const out = new Map<string, unknown>()
  if (o && typeof o === 'object' && !Array.isArray(o)) {
    for (const [k, v] of Object.entries(o)) for (const [p, x] of leaves(v, prefix ? `${prefix}.${k}` : k)) out.set(p, x)
  } else out.set(prefix, o)
  return out
}

describe('language definitions', () => {
  it('defines six languages with their speech locales', () => {
    expect(LANGUAGE_CODES).toEqual(['en', 'fr', 'es', 'de', 'it', 'pt'])
    expect(localeOf('de')).toBe('de-DE')
    expect(localeOf('it')).toBe('it-IT')
    expect(localeOf('pt')).toBe('pt-PT')
    expect(speechLang('de')).toBe('de-DE')
    expect(speechLang('it')).toBe('it-IT')
    expect(speechLang('pt')).toBe('pt-PT')
    expect(localeOf('xx')).toBe('en-GB')
    expect(LANGUAGES.map(l => l.name)).toEqual(['English', 'Français', 'Español', 'Deutsch', 'Italiano', 'Português'])
    expect(isLang('pt')).toBe(true)
    expect(isLang('pt-BR')).toBe(false)
  })

  it('every language has every UI and accessibility string (no empty values, no English left in key texts)', () => {
    const en = leaves(t('en'))
    const enA11y = leaves(a11yStrings('en'))
    for (const lang of LANGUAGE_CODES) {
      const ui = leaves(t(lang))
      const a11y = leaves(a11yStrings(lang))
      expect([...ui.keys()].filter(k => !k.startsWith('replay.events.')).sort(), lang).toEqual([...en.keys()].filter(k => !k.startsWith('replay.events.')).sort())
      expect([...a11y.keys()].sort(), lang).toEqual([...enA11y.keys()].sort())
      for (const [k, v] of ui) if (typeof v === 'string') expect(v.length, `${lang}.${k}`).toBeGreaterThan(0)
    }
    for (const lang of NEW) {
      const s = t(lang)
      for (const k of ['why', 'showMe', 'live', 'agents', 'chain', 'whyFacts', 'supportingLegend', 'contextLegend'] as const)
        expect(s[k], `${lang}.${k}`).not.toBe(t('en')[k])
      expect(s.replay.button).not.toBe(t('en').replay.button)
      expect(s.agentsUi.title).not.toBe(t('en').agentsUi.title)
      expect(a11yStrings(lang).speech.noVoice).not.toBe(a11yStrings('en').speech.noVoice)
    }
  })

  it('no supported language silently reuses English text (only reviewed cognates and identifiers may match)', () => {
    // Identical to English on purpose: same word in that language, key names, product names or pure templates.
    const allowed: Record<string, string[]> = {
      fr: ['text.normal', 'describe.title', 'agents', 'pause', 'match', 'possession', 'minute', 'replay.pause', 'replay.legend', 'replay.events.INTERCEPTION', 'replay.events.CORNER'],
      es: ['text.normal'],
      de: ['text.normal', 'highlights', 'pause', 'minute', 'replay.pause', 'replay.clipOf', 'replay.events.PASS', 'replay.events.FOUL'],
      it: ['keys.replayList.2.0', 'replay.clipOf', 'replay.events.CROSS'],
      pt: ['text.normal', 'keys.list.0.0', 'keys.replayList.2.0', 'replay.clipOf'],
    }
    const always = ['keys.list.2.0', 'keys.list.4.0', 'keys.replayList.1.0', 'keys.replayList.4.0', 'ad.clip', 'agentsUi.modeLive', 'agentsUi.entraId', 'agentsUi.tokens']
    const enUi = leaves(t('en'))
    const enA11y = leaves(a11yStrings('en'))
    for (const lang of LANGUAGE_CODES.filter(l => l !== 'en')) {
      const same = [...leaves(t(lang)), ...leaves(a11yStrings(lang))]
        .filter(([k, v]) => typeof v === 'string' && v.length > 1 && (v === enUi.get(k) || v === enA11y.get(k)))
        .map(([k]) => k)
        .filter(k => !always.includes(k) && !allowed[lang].includes(k))
      expect(same, `${lang} strings identical to English`).toEqual([])
    }
  })

  it('uses natural football terms for each new language', () => {
    expect(t('de').showMe).toBe('Zeig es mir')
    expect(t('de').replay.events.TURNOVER).toBe('Ballverlust')
    expect(t('it').replay.events.CORNER).toBe("Calcio d'angolo")
    expect(t('it').replay.events.OFFSIDE).toBe('Fuorigioco')
    expect(t('pt').replay.events.GOAL).toBe('Golo')                      // European Portuguese
    expect(t('pt').shots).toBe('Remates')
    expect(performanceLabels('pt').shots).toBe('Remates')
    expect(performanceLabels('de').turnovers).toBe('Ballverluste')
  })
})

describe('language switching', () => {
  it('English → German → Italian → Portuguese → French changes every visible label', () => {
    const path = ['en', 'de', 'it', 'pt', 'fr'] as const
    for (let i = 1; i < path.length; i++) {
      const before = t(path[i - 1])
      const after = t(path[i])
      expect(after.guide.steps.insight.title).not.toBe(before.guide.steps.insight.title)
      expect(after.verdict.verified[0]).not.toBe(before.verdict.verified[0])
      expect(speechLang(path[i])).not.toBe(speechLang(path[i - 1]))
    }
  })
})

// ---------------------------------------------------------------- speech

const v = (name: string, lang: string, extra: Partial<VoiceLike> & { localService?: boolean } = {}): VoiceLike => ({ name, lang, ...extra })
const device: VoiceLike[] = [
  v('Microsoft David', 'en-US', { default: true, localService: true }),
  v('Microsoft Katja', 'de-DE', { localService: true }),
  v('Google Deutsch (Österreich)', 'de-AT'),
  v('Google italiano', 'it-IT'),
  v('Google português do Brasil', 'pt-BR'),
]

class Synth implements SynthLike {
  spoken: UtteranceLike[] = []
  current: UtteranceLike | null = null
  voices: VoiceLike[]
  constructor(voices: VoiceLike[]) { this.voices = voices }
  speak(u: UtteranceLike) { this.spoken.push(u); this.current = u }
  cancel() { this.current = null }
  pause() {}
  resume() {}
  getVoices() { return this.voices }
  addEventListener() {}
  removeEventListener() {}
  end() { const u = this.current; this.current = null; u?.onend?.() }
}
const queueOn = (synth: Synth) => new SpeechQueue({
  synth, now: () => 0, makeUtterance: (text) => ({ text, lang: '', rate: 1, volume: 1, voice: null, onend: null, onerror: null }),
})

describe('speech for German, Italian and Portuguese', () => {
  it('prefers the exact locale voice', () => {
    expect(chooseVoice(device, 'de-DE')?.name).toBe('Microsoft Katja')
    expect(chooseVoice(device, 'it-IT')?.name).toBe('Google italiano')
  })

  it('allows another region of the same language, never another language', () => {
    expect(chooseVoice(device, 'pt-PT')?.lang).toBe('pt-BR')              // same language, different region
    expect(chooseVoice(device.filter(x => !x.lang.startsWith('pt')), 'pt-PT')).toBeNull()
    expect(chooseVoice(device.filter(x => !x.lang.startsWith('de')), 'de-DE')).toBeNull()
  })

  it('every utterance carries the right lang, with or without a matching voice', () => {
    const synth = new Synth(device)
    const q = queueOn(synth)
    for (const lang of NEW) {
      const text = a11yStrings(lang).testPhrase
      q.setLanguage(speechLang(lang))
      q.say({ text, kind: 'insight', lang: speechLang(lang) })
      const u = synth.spoken.at(-1)!
      expect(u.text).toBe(text)
      expect(u.lang).toBe(speechLang(lang))
      expect(u.voice?.lang.split('-')[0]).toBe(lang)
      synth.end()
    }

    const englishOnly = new Synth([v('Microsoft David', 'en-US', { default: true })])
    const q2 = queueOn(englishOnly)
    for (const lang of NEW) {
      q2.setLanguage(speechLang(lang))
      q2.say({ text: a11yStrings(lang).testPhrase, kind: 'insight', lang: speechLang(lang) })
      const u = englishOnly.spoken.at(-1)!
      expect(u.lang).toBe(speechLang(lang))
      expect(u.voice).toBeNull()                                          // never an English voice for German/Italian/Portuguese
      expect(q2.getState().missingVoice).toBe(speechLang(lang))           // the localized no-voice notice is shown
      englishOnly.end()
    }
  })
})

// ---------------------------------------------------------------- evidence, verification, live status

const ev = (n: number): PitchEvent => ({ id: `MS-FLAGSHIP-001-E${String(n).padStart(5, '0')}`, seq: n, second: 3000 + n, clock: '55:00', type: 'Pass', outcome: 'Complete', teamId: 'WHA' } as PitchEvent)
const supporting = Array.from({ length: 30 }, (_, i) => ev(842 + i))
const focus: InsightEvents = {
  insightId: 'MS-FLAGSHIP-001-I015', layers: [], events: [ev(797), ...supporting],
  supportingEventIds: supporting.map(e => e.id), contextEventIds: [ev(797).id],
}

describe('evidence and context in every new language', () => {
  it('renders 30 supporting + 1 context, with context explicitly not evidence', () => {
    const expected: Record<(typeof NEW)[number], RegExp> = {
      de: /^30 belegende Ereignisse \+ 1 zum Vergleich \(grau, zählt nicht als Beleg\)$/,
      it: /^30 eventi a supporto \+ 1 di contesto \(in grigio, non contano come prova\)$/,
      pt: /^30 eventos de suporte \+ 1 de contexto \(a cinzento, não contam como prova\)$/,
    }
    for (const lang of NEW) {
      const s: Strings = t(lang)
      expect(evidenceLegend(splitEvidence(focus), s.supportingLegend, s.contextLegend)).toMatch(expected[lang])
    }
  })
})

describe('verification and live status wording', () => {
  const exec = (mode: WorkflowExecution['executionMode'], outcome: WorkflowExecution['outcome'], verified: boolean) => ({
    workflowExecutionId: 'wf', insightId: 'I', matchId: 'M', executionMode: mode, provider: 'p', modelDeployment: mode === 'LIVE_FOUNDRY' ? 'gpt-4.1-mini' : undefined,
    orchestrator: 'o', status: 'COMPLETED', startedAt: '', steps: [], handoffs: [], frameworkSupersteps: 0, frameworkExecutorInvocations: 0,
    modelCalls: 0, modelCallLimit: 8, inputTokens: 0, outputTokens: 0, outcome, verified, authoritativeFactsUnchanged: true, recoveryActions: [],
  }) as WorkflowExecution
  const status = (mode: WorkflowExecution['executionMode']): AgentRuntimeStatus => ({
    runtime: { executionMode: mode, provider: 'p', modelDeployment: mode === 'LIVE_FOUNDRY' ? 'gpt-4.1-mini' : undefined, credential: 'ManagedIdentity', orchestrator: 'o', packages: {} },
    warmUp: 'done', agents: [],
  })

  it('localizes the verdict, chips and mode wording while keeping identifiers exact', () => {
    for (const lang of NEW) {
      const s = t(lang)
      const live = runtimeChips(status('LIVE_FOUNDRY'), exec('LIVE_FOUNDRY', 'APPROVED', true), chipLabels(s)).map(c => c.label)
      expect(live[0]).toBe('LIVE · Microsoft Foundry')
      expect(live).toContain('gpt-4.1-mini')
      expect(live).toContain(s.agentsUi.managedIdentity)
      expect(live.at(-1)).toBe(`${s.agentsUi.verification}: APPROVED`)
      expect(s.verdict[verdict(exec('LIVE_FOUNDRY', 'APPROVED', true))!.kind][0]).toBe(s.verdict.verified[0])
      expect(modeLabel(s, 'OFFLINE_TEMPLATE')).toMatch(/^OFFLINE TEMPLATE · /)
      expect(modeLabel(s, 'RECORDED_DEMO')).toMatch(/^RECORDED DEMO · /)
    }
  })

  it('the published-result note is localized (English keeps the server notice)', () => {
    const server = 'Verified by the MatchShift Verification Agent and deterministic validation.'
    expect(publishedNotice(t('en'), 'APPROVED', server, true)).toBe(server)
    for (const lang of NEW) {
      expect(publishedNotice(t(lang), 'APPROVED', server, false)).toBe(t(lang).verdict.verified[1])
      expect(publishedNotice(t(lang), 'REJECTED', server, false)).toBe(t(lang).verdict.rejected[1])
      expect(publishedNotice(t(lang), 'FALLBACK_UNVERIFIED', 'Live AI analysis is temporarily unavailable', false)).toBe(t(lang).verdict.unverified[1])
    }
  })

  it('offline and recorded runs never show LIVE or a model, in any language', () => {
    for (const lang of NEW) {
      for (const mode of ['OFFLINE_TEMPLATE', 'RECORDED_DEMO'] as const) {
        const labels = runtimeChips(status(mode), exec(mode, 'DETERMINISTIC_ONLY', false), chipLabels(t(lang))).map(c => c.label).join(' | ')
        expect(labels).not.toMatch(/LIVE ·|gpt-4\.1-mini/)
      }
      expect(verdict(exec('OFFLINE_TEMPLATE', 'DETERMINISTIC_ONLY', false))?.kind).toBe('deterministic')
    }
  })

  it('the guided demo has all seven steps in German, Italian and Portuguese', () => {
    for (const lang of NEW) {
      const g = t(lang).guide
      for (const step of GUIDE_STEPS) {
        expect(g.steps[step].title.length, `${lang}.${step}`).toBeGreaterThan(0)
        expect(g.steps[step].action).not.toBe(t('en').guide.steps[step].action)
      }
      expect(g.stepOf).toContain('{n}')
      expect(t(lang).agentsUi.nodes['verification-agent'][0]).not.toBe('Verification Agent')
    }
  })
})
