import { describe, expect, it } from 'vitest'
import { chooseVoice, SpeechQueue } from './speech'
import type { SynthLike, UtteranceLike, VoiceLike } from './speech'
import { a11yStrings, speechLang } from './strings'
import { describeReplayEvent, matchStatus } from './describe'
import { LANGUAGES, formatMinutes, formatNumber, formatPercent, spokenClock } from '../languages'
import type { Snapshot } from '../api'
import type { ReplayEvent, ReplayPlayer } from '../replay/types'

// Speech is the viewer's browser/OS Web Speech API. These tests mock it, so no installed voices are needed.

const v = (name: string, lang: string, extra: Partial<VoiceLike> & { localService?: boolean } = {}): VoiceLike => ({ name, lang, ...extra })

// The voices Chrome on Windows actually reported on the test machine (French/Spanish only as remote Google voices).
const windowsChrome: VoiceLike[] = [
  v('Microsoft David - English (United States)', 'en-US', { default: true, localService: true }),
  v('Microsoft Hazel - English (United Kingdom)', 'en-GB', { localService: true }),
  v('Microsoft Richard - English (Canada)', 'en-CA', { localService: true }),
  v('Google español', 'es-ES'),
  v('Google español de Estados Unidos', 'es-US'),
  v('Google français', 'fr-FR'),
]

class Synth implements SynthLike {
  spoken: UtteranceLike[] = []
  current: UtteranceLike | null = null
  cancelled = 0
  voices: VoiceLike[]
  listeners = new Set<() => void>()
  autoStart = true
  constructor(voices: VoiceLike[]) { this.voices = voices }
  speak(u: UtteranceLike) { this.spoken.push(u); this.current = u; if (this.autoStart) u.onstart?.() }
  cancel() { this.cancelled++; this.current = null }
  pause() {}
  resume() {}
  getVoices() { return this.voices }
  addEventListener(_: 'voiceschanged', l: () => void) { this.listeners.add(l) }
  removeEventListener(_: 'voiceschanged', l: () => void) { this.listeners.delete(l) }
  end() { const u = this.current; this.current = null; u?.onend?.() }
  fail(error: string) { const u = this.current; this.current = null; u?.onerror?.({ error }) }
  loadVoices(voices: VoiceLike[]) { this.voices = voices; for (const l of this.listeners) l() }
}

const queueOn = (synth: Synth) => new SpeechQueue({
  synth, now: () => 0,
  makeUtterance: (text) => ({ text, lang: '', rate: 1, volume: 1, voice: null, onend: null, onerror: null }),
})

describe('language to speech locale', () => {
  it('maps English, French and Spanish to matching BCP-47 locales', () => {
    expect(speechLang('en')).toMatch(/^en(-|$)/)
    expect(speechLang('fr')).toMatch(/^fr(-|$)/)
    expect(speechLang('es')).toMatch(/^es(-|$)/)
  })
})

describe('voice selection', () => {
  it('prefers the exact locale over the platform default of the same language', () => {
    expect(chooseVoice(windowsChrome, 'en-GB')?.name).toBe('Microsoft Hazel - English (United Kingdom)')
    expect(chooseVoice(windowsChrome, 'es-ES')?.name).toBe('Google español')
  })

  it('accepts another region of the same language', () => {
    expect(chooseVoice(windowsChrome, 'fr-CA')?.name).toBe('Google français')
    expect(chooseVoice(windowsChrome, 'es-MX')?.lang).toMatch(/^es-/)
  })

  it('never picks an English voice for French or Spanish when a matching voice exists', () => {
    expect(chooseVoice(windowsChrome, 'fr-FR')?.lang).toBe('fr-FR')
    expect(chooseVoice(windowsChrome, 'es-ES')?.lang.startsWith('es')).toBe(true)
  })

  it('honours a viewer-selected French voice without crossing languages', () => {
    const voices = [v('Hortense', 'fr-FR'), v('Julie', 'fr-FR'), v('English', 'en-GB')]
    expect(chooseVoice(voices, 'fr-FR', [], 'Julie')?.name).toBe('Julie')
    expect(chooseVoice(voices, 'en-GB', [], 'Julie')?.name).toBe('English')
    const synth = new Synth(voices)
    const q = queueOn(synth)
    q.setPreferredVoices({ fr: 'Julie' })
    q.setLanguage('fr')
    q.say({ text: 'Bonjour', kind: 'test', lang: 'fr' })
    expect(synth.spoken[0].voice?.name).toBe('Julie')
  })

  it('returns no explicit voice (never a different language) when the device has none for that language', () => {
    expect(chooseVoice(windowsChrome.filter(x => x.lang.startsWith('en')), 'fr-FR')).toBeNull()
  })
})

describe('speech queue language handling', () => {
  it('sets utterance.lang for every language, with a matching voice when available', () => {
    const synth = new Synth(windowsChrome)
    const q = queueOn(synth)
    for (const [lang, text] of [['en', 'Westhaven switch to the right'], ['fr', 'Westhaven passe à droite'], ['es', 'Westhaven cambia a la derecha']] as const) {
      q.setLanguage(speechLang(lang))
      q.say({ text, kind: 'insight', lang: speechLang(lang) })
      const u = synth.spoken.at(-1)!
      expect(u.text).toBe(text)
      expect(u.lang).toBe(speechLang(lang))
      expect(u.voice?.lang.split('-')[0]).toBe(lang)
      synth.end()
    }
  })

  it('does not allow a browser default to read French when no compatible voice exists', () => {
    const synth = new Synth([v('Microsoft David', 'en-US', { default: true })])
    const q = queueOn(synth)
    q.say({ text: 'Bonjour', kind: 'insight', lang: 'fr-FR' })
    expect(synth.spoken).toHaveLength(0)
    expect(q.getState().transcript.at(-1)).toMatchObject({ text: 'Bonjour', lang: 'fr-FR', spoken: false })
    expect(q.getState().missingVoice).toBe('fr-FR')
  })

  it('waits for voiceschanged, then speaks once using an actual matching voice', () => {
    const synth = new Synth([])
    const q = queueOn(synth)
    q.say({ text: 'Bonjour', kind: 'insight', lang: 'fr-FR' })
    expect(synth.spoken).toHaveLength(0)
    synth.loadVoices(windowsChrome)
    expect(synth.spoken).toHaveLength(1)
    expect(synth.spoken[0].lang).toBe('fr-FR')
    expect(synth.spoken[0].voice?.lang).toBe('fr-FR')
    synth.loadVoices(windowsChrome)
    expect(synth.spoken).toHaveLength(1)
    synth.end()
    q.say({ text: 'Encore', kind: 'insight', lang: 'fr-FR' })
    expect(synth.spoken[1].voice?.name).toBe('Google français')
    expect(synth.listeners.size).toBe(1)
    q.dispose()
    expect(synth.listeners.size).toBe(0)
  })

  it('clears a missing-voice notice once a matching voice loads', () => {
    const synth = new Synth([v('Microsoft David', 'en-US')])
    const q = queueOn(synth)
    q.say({ text: 'Hola', kind: 'insight', lang: 'es-ES' })
    expect(q.getState().missingVoice).toBe('es-ES')
    synth.loadVoices(windowsChrome)
    expect(q.getState().missingVoice).toBeNull()
  })

  it('retries a voice that fails to start using another voice of the same locale', () => {
    const synth = new Synth([v('Hortense', 'fr-FR'), v('Julie', 'fr-FR'), v('English', 'en-GB')])
    synth.autoStart = false
    const q = queueOn(synth)
    q.setLanguage('fr')
    q.say({ text: 'Bonjour', kind: 'insight', lang: 'fr' })
    expect(q.getState().transcript.at(-1)?.spoken).toBe(false)
    synth.autoStart = true
    synth.fail('voice-unavailable')
    synth.spoken[0].onend?.() // A late callback from the failed voice must not stop the replacement.
    expect(synth.spoken.map(u => u.voice?.name)).toEqual(['Hortense', 'Julie'])
    expect(synth.spoken.at(-1)?.lang).toBe('fr-FR')
    expect(q.getState().speaking).toBe(true)
    expect(q.getState().transcript.at(-1)?.spoken).toBe(true)
    expect(q.getState().error).toBeNull()
  })

  it('reports a failed engine instead of marking silent speech as spoken', () => {
    const synth = new Synth([v('French', 'fr-FR')])
    synth.autoStart = false
    const q = queueOn(synth)
    q.setLanguage('fr')
    q.say({ text: 'Bonjour', kind: 'insight', lang: 'fr' })
    synth.fail('synthesis-failed')
    expect(q.getState().transcript.at(-1)?.spoken).toBe(false)
    expect(q.getState().error).toBe('synthesis-failed')
    expect(q.canRepeat).toBe(false)
  })

  it('switching language uses the new locale for the next utterance', () => {
    const synth = new Synth(windowsChrome)
    const q = queueOn(synth)
    q.setLanguage('en-GB')
    q.say({ text: 'English', kind: 'insight', lang: 'en-GB' })
    synth.end()
    q.setLanguage('fr-FR')
    q.say({ text: 'Français', kind: 'insight', lang: 'fr-FR' })
    synth.end()
    q.setLanguage('es-ES')
    q.say({ text: 'Español', kind: 'insight', lang: 'es-ES' })
    expect(synth.spoken.map(u => u.lang)).toEqual(['en-GB', 'fr-FR', 'es-ES'])
  })

  it('drops queued and stops playing speech in the old language when the language changes', () => {
    const synth = new Synth(windowsChrome)
    const q = queueOn(synth)
    q.setLanguage('fr-FR')
    q.say({ text: 'Première analyse', kind: 'insight', lang: 'fr-FR' })
    q.say({ text: 'Deuxième analyse en attente', kind: 'insight', lang: 'fr-FR' })
    q.setLanguage('es-ES')
    expect(synth.cancelled).toBe(1) // the French utterance being spoken is stopped
    q.say({ text: 'Análisis nuevo', kind: 'insight', lang: 'es-ES' })
    synth.end()
    expect(synth.spoken.map(u => u.text)).toEqual(['Première analyse', 'Análisis nuevo']) // queued French never plays
    expect(q.getState().transcript.map(e => e.text)).toContain('Deuxième analyse en attente') // text is kept
  })
})

describe('numeric speech and runtime switching in all six languages', () => {
  const voices = LANGUAGES.map(l => v(l.name, l.locale))
  const minuteUnits = { en: ['minute', 'minutes'], fr: ['minute', 'minutes'], es: ['minuto', 'minutos'], de: ['Minute', 'Minuten'], it: ['minuto', 'minuti'], pt: ['minuto', 'minutos'] }

  for (const { code, locale } of LANGUAGES) {
    it(`${code}: formats minutes, score, percent and clock and dispatches with the selected voice`, () => {
      const synth = new Synth([v('English default', 'en-US', { default: true }), ...voices])
      const q = queueOn(synth)
      q.setLanguage(code) // UI code uses the same registry as full locale tags.
      for (const minute of [1, 15, 45]) {
        const snap = { clock: { minute }, score: { home: 2, away: 1 }, home: { shortName: 'Northbridge' }, away: { shortName: 'Westhaven' }, metrics: { homeControl: 50 } } as Snapshot
        const duration = formatMinutes(minute, code)
        expect(duration).toBe(`${minute} ${minuteUnits[code][minute === 1 ? 0 : 1]}`)
        const text = matchStatus(snap, a11yStrings(code))
        expect(text).toContain(duration)
        expect(text).toContain('2')
        expect(text).toContain('1')
        if (code !== 'en') expect(text).not.toContain('minutes played')
        q.say({ text, kind: 'status', lang: code })
        expect(synth.spoken.at(-1)).toMatchObject({ text, lang: locale, voice: { lang: locale } })
        synth.end()
      }
      const numeric = `${formatPercent(0.31, code)}; ${formatPercent(0.9, code)}; ${formatNumber(1.25, code)}; ${spokenClock(905, code)}`
      expect(numeric).toContain(code === 'en' ? '1.25' : '1,25')
      expect(numeric).toContain(formatMinutes(15, code))
      expect(numeric).not.toContain('15:05')
      q.say({ text: numeric, kind: 'status', lang: code })
      expect(synth.spoken.at(-1)).toMatchObject({ text: numeric, lang: locale, voice: { lang: locale } })
    })

    it(`English → ${code} → English cancels old speech and rejects stale callbacks/repeats`, () => {
      const synth = new Synth(voices)
      const q = queueOn(synth)
      q.setLanguage('en')
      q.say({ text: '15 minutes', kind: 'status', lang: 'en' })
      const old = q.getState().transcript.at(-1)!
      q.setLanguage(code)
      if (code !== 'en') {
        expect(q.repeatLast()).toBe(false)
        expect(q.replay(old)).toBe(false)
        expect(q.say({ text: 'Stale English callback', kind: 'status', lang: 'en' })).toBe(false)
      } else synth.end()
      q.say({ text: formatMinutes(15, code), kind: 'status', lang: code })
      expect(synth.spoken.at(-1)?.lang).toBe(locale)
      q.setLanguage('en')
      if (code === 'en') synth.end()
      q.say({ text: '45 minutes', kind: 'status', lang: 'en' })
      expect(synth.spoken.at(-1)?.lang).toBe('en-GB')
    })
  }

  it('drops stale replay cues while voices are loading instead of narrating them late', () => {
    const synth = new Synth([])
    const q = queueOn(synth)
    q.setLanguage('fr')
    q.setTimeline('replay', 0, true)
    q.say({ text: 'Passe', kind: 'replay', lang: 'fr', at: 0, validFor: 1 })
    q.setTimeline('replay', 5, true)
    synth.loadVoices(voices)
    expect(synth.spoken).toEqual([])
  })

  it('rechecks a voice removed between enqueue and dispatch', () => {
    const synth = new Synth(voices)
    const q = queueOn(synth)
    q.setLanguage('fr')
    q.say({ text: 'Première', kind: 'status', lang: 'fr' })
    q.say({ text: 'Deuxième', kind: 'status', lang: 'fr' })
    synth.voices = voices.filter(x => x.lang.startsWith('en'))
    synth.end()
    expect(synth.spoken).toHaveLength(1)
    expect(q.getState().missingVoice).toBe('fr-FR')
  })
})

describe('replay audio description follows the viewer language', () => {
  const players: ReplayPlayer[] = [{ id: 'WHA-9', teamId: 'WHA', isHome: false, shirtNumber: 9, name: 'Kwame Addo-Sterling', shortName: 'Addo-Sterling', position: 'Striker', isGoalkeeper: false }]
  const event: ReplayEvent = { eventId: 'E1', sequence: 1, time: 0, endTime: 1, matchSecond: 3500, clock: '58:20', type: 'Shot', outcome: 'OnTarget',
    label: 'SHOT_ON_TARGET', teamId: 'WHA', playerId: 'WHA-9', x: 88, y: 45, endX: 100, endY: 50 } as ReplayEvent

  it('produces French and Spanish text, not English, for the same event', () => {
    const en = describeReplayEvent(event, players, 'NBR', a11yStrings('en'))!
    const fr = describeReplayEvent(event, players, 'NBR', a11yStrings('fr'))!
    const es = describeReplayEvent(event, players, 'NBR', a11yStrings('es'))!
    expect(en).toContain('Number 9')
    expect(fr).toContain('Numéro 9')
    expect(es).toContain('El número 9')
    expect(fr).not.toBe(en)
    expect(es).not.toBe(fr)
  })

  it('names an unattributed team in the viewer language, never English "Home"/"Away"', () => {
    const anonymous = { ...event, playerId: undefined, type: 'Foul', label: 'FOUL', x: undefined, y: undefined } as ReplayEvent
    const fr = describeReplayEvent(anonymous, players, 'NBR', a11yStrings('fr'))!
    const es = describeReplayEvent(anonymous, players, 'NBR', a11yStrings('es'))!
    expect(fr).toContain("L'équipe à l'extérieur")
    expect(es).toContain('El equipo visitante')
    expect(`${fr} ${es}`).not.toMatch(/\b(Home|Away)\b/)
  })

  it('localizes corner teams, shirt numbers and shot distances in every supported language', () => {
    for (const { code, locale } of LANGUAGES) {
      const s = a11yStrings(code)
      const corner = describeReplayEvent({ ...event, type: 'Corner', label: 'CORNER' }, players, 'NBR', s)!
      expect(corner).toContain(s.ad.away)
      const shot = describeReplayEvent({ ...event, x: 12 }, players, 'NBR', s)!
      expect(shot).toContain('9')
      expect(shot).toContain('13') // Westhaven attacks towards x=0: about 13 metres from goal.
      const synth = new Synth([v(code, locale)])
      const q = queueOn(synth)
      q.setLanguage(code)
      q.say({ text: shot, kind: 'replay', lang: code })
      expect(synth.spoken[0]).toMatchObject({ lang: locale, voice: { lang: locale } })
    }
  })
})
