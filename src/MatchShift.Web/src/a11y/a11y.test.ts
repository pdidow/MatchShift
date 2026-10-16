import { describe, expect, it } from 'vitest'
import type { InsightCard, Snapshot } from '../api'
import type { ReplayEvent, ReplayPlayer } from '../replay/types'
import { channelOf, describeFocus, describeReplayEvent, insightSpeech, matchStatus, metresFromGoal, periodicDue, priorityOf, shouldSpeak, simplifiedText, teamFrame, whatChanged, whyNarration } from './describe'
import { activeFeatures, applyPreset, DEFAULT_PREFS, documentAttributes, effectiveReducedMotion, loadPrefs, matchingPreset, savePrefs, speechEnabled, STORAGE_KEY } from './prefs'
import type { SynthLike, UtteranceLike, VoiceLike } from './speech'
import { SpeechQueue } from './speech'
import { a11yStrings } from './strings'

// ------------------------------------------------------------------ preferences

describe('accessibility preferences', () => {
  it('are all off by default, so the default experience is unchanged', () => {
    expect(speechEnabled(DEFAULT_PREFS)).toBe(false)
    expect(activeFeatures(DEFAULT_PREFS, false)).toEqual([])
    expect(documentAttributes(DEFAULT_PREFS, false)).toEqual({})
    expect(matchingPreset(DEFAULT_PREFS)).toBe('default')
  })

  it('follow the system reduced-motion setting only while the user has not chosen', () => {
    expect(effectiveReducedMotion(DEFAULT_PREFS, true)).toBe(true)
    expect(documentAttributes(DEFAULT_PREFS, true)).toEqual({ 'data-reduced-motion': 'true' })
    expect(effectiveReducedMotion({ ...DEFAULT_PREFS, reducedMotion: 'off' }, true)).toBe(false)
    expect(effectiveReducedMotion({ ...DEFAULT_PREFS, reducedMotion: 'on' }, false)).toBe(true)
  })

  it('presets start from defaults and never leave unrelated options on', () => {
    const custom = { ...DEFAULT_PREFS, highContrast: true, spokenInsights: true, speechRate: 1.4 }
    const motion = applyPreset('reducedMotion', custom)
    expect(motion.highContrast).toBe(false)
    expect(motion.spokenInsights).toBe(false)
    expect(motion.reducedMotion).toBe('on')
    expect(motion.reduceFlashing).toBe(true)
    expect(motion.speechRate).toBe(1.4) // comfort setting kept
    expect(matchingPreset(motion)).toBe('reducedMotion')
    expect(matchingPreset(applyPreset('lowVision', custom))).toBe('lowVision')
    expect(matchingPreset(applyPreset('audioFirst', custom))).toBe('audioFirst')
    expect(matchingPreset({ ...applyPreset('lowVision', custom), simplified: true })).toBe('custom')
  })

  it('options are independent: each one maps to its own attribute', () => {
    const p = { ...DEFAULT_PREFS, colorVision: 'tritanopia' as const, textSize: 'xl' as const }
    expect(documentAttributes(p, false)).toEqual({ 'data-cvd': 'tritanopia', 'data-text': 'xl' })
    expect(documentAttributes({ ...DEFAULT_PREFS, reduceFlashing: true }, false)).toEqual({ 'data-reduce-flash': 'true' })
    expect(documentAttributes({ ...DEFAULT_PREFS, highContrast: true }, false)).toEqual({ 'data-contrast': 'high' })
  })

  it('persist, and garbage in storage falls back safely', () => {
    const store = new Map<string, string>()
    const storage = { getItem: (k: string) => store.get(k) ?? null, setItem: (k: string, v: string) => { store.set(k, v) } }
    savePrefs({ ...DEFAULT_PREFS, highContrast: true, speechVolume: 0 }, storage)
    const loaded = loadPrefs(storage)
    expect(loaded.highContrast).toBe(true)
    expect(loaded.speechVolume).toBe(0)
    store.set(STORAGE_KEY, JSON.stringify({ colorVision: 'rainbow', speechRate: 99, textSize: 7 }))
    const bad = loadPrefs(storage)
    expect(bad.colorVision).toBe('default')
    expect(bad.speechRate).toBe(2)
    expect(bad.textSize).toBe('normal')
    store.set(STORAGE_KEY, '{not json')
    expect(loadPrefs(storage)).toEqual(DEFAULT_PREFS)
  })
})

// ------------------------------------------------------------------ speech queue

class FakeSynth implements SynthLike {
  spoken: UtteranceLike[] = []
  current: UtteranceLike | null = null
  cancelled = 0
  voices: VoiceLike[] = [{ lang: 'en-GB', name: 'English', default: true }, { lang: 'fr-FR', name: 'Français' }]
  speak(u: UtteranceLike) { this.spoken.push(u); this.current = u }
  cancel() { this.cancelled++; this.current = null }
  pause() {}
  resume() {}
  getVoices() { return this.voices }
  /** Finish the utterance being spoken. */
  end() { const u = this.current; this.current = null; u?.onend?.() }
}

function makeQueue(now = { t: 0 }) {
  const synth = new FakeSynth()
  const queue = new SpeechQueue({
    synth,
    makeUtterance: (text) => ({ text, lang: '', rate: 1, volume: 1, voice: null, onend: null, onerror: null }),
    now: () => now.t,
  })
  return { synth, queue, now }
}

describe('speech queue', () => {
  it('speaks one message at a time, in priority order', () => {
    const { synth, queue } = makeQueue()
    queue.say({ text: 'first', kind: 'insight', lang: 'en-GB', priority: 1 })
    queue.say({ text: 'low', kind: 'insight', lang: 'en-GB', priority: 0 })
    queue.say({ text: 'normal', kind: 'insight', lang: 'en-GB', priority: 1 })
    expect(synth.spoken.map(u => u.text)).toEqual(['first'])
    synth.end()
    synth.end()
    synth.end()
    expect(synth.spoken.map(u => u.text)).toEqual(['first', 'normal', 'low'])
  })

  it('never repeats the same key', () => {
    const { synth, queue } = makeQueue()
    expect(queue.say({ text: 'goal', kind: 'insight', key: 'insight:1', lang: 'en-GB' })).toBe(true)
    expect(queue.say({ text: 'goal', kind: 'insight', key: 'insight:1', lang: 'en-GB' })).toBe(false)
    synth.end()
    expect(synth.spoken).toHaveLength(1)
    expect(queue.getState().transcript).toHaveLength(1)
  })

  it('discards stale messages', () => {
    const { synth, queue, now } = makeQueue()
    queue.say({ text: 'long one', kind: 'insight', lang: 'en-GB', priority: 1 })
    queue.say({ text: 'stale detail', kind: 'insight', lang: 'en-GB', priority: 0 })
    now.t = 20_000 // > 15 s low-priority TTL
    synth.end()
    expect(synth.spoken.map(u => u.text)).toEqual(['long one'])
  })

  it('critical messages interrupt lower-priority speech', () => {
    const { synth, queue } = makeQueue()
    queue.say({ text: 'tempo detail', kind: 'insight', lang: 'en-GB', priority: 0 })
    queue.say({ text: 'GOAL', kind: 'insight', lang: 'en-GB', priority: 2 })
    expect(synth.cancelled).toBe(1)
    expect(synth.spoken.map(u => u.text)).toEqual(['tempo detail', 'GOAL'])
  })

  it('mute stops speech but keeps the transcript; repeat last re-speaks', () => {
    const { synth, queue } = makeQueue()
    queue.say({ text: 'one', kind: 'insight', lang: 'en-GB' })
    synth.end()
    queue.setMuted(true)
    queue.say({ text: 'two', kind: 'insight', lang: 'en-GB' })
    expect(synth.spoken).toHaveLength(1)
    expect(queue.getState().transcript.map(e => e.text)).toEqual(['one', 'two'])
    queue.setMuted(false)
    queue.repeatLast()
    expect(synth.spoken.map(u => u.text)).toEqual(['one', 'one'])
  })

  it('uses a voice for the language and falls back to text when none exists', () => {
    const { synth, queue } = makeQueue()
    queue.say({ text: 'bonjour', kind: 'insight', lang: 'fr-FR' })
    expect(synth.spoken[0].voice?.name).toBe('Français')
    synth.end()
    queue.say({ text: 'hola', kind: 'insight', lang: 'es-ES' })
    expect(synth.spoken).toHaveLength(1)
    expect(queue.getState().missingVoice).toBe('es-ES')
    expect(queue.getState().transcript.at(-1)?.text).toBe('hola')
  })

  it('clears one category (replay narration after a seek)', () => {
    const { synth, queue } = makeQueue()
    queue.say({ text: 'pass', kind: 'replay', lang: 'en-GB', priority: 0 })
    queue.say({ text: 'carry', kind: 'replay', lang: 'en-GB', priority: 0 })
    queue.clearKind('replay')
    synth.end()
    expect(synth.spoken.map(u => u.text)).toEqual(['pass'])
    expect(queue.getState().speaking).toBe(false)
  })

  it('applies rate and volume', () => {
    const { synth, queue } = makeQueue()
    queue.rate = 1.5
    queue.volume = 0.4
    queue.say({ text: 'x', kind: 'test', lang: 'en-GB' })
    expect(synth.spoken[0].rate).toBe(1.5)
    expect(synth.spoken[0].volume).toBe(0.4)
  })

  it('without speech synthesis, messages are still captioned', () => {
    const queue = new SpeechQueue({ synth: null })
    queue.say({ text: 'text only', kind: 'insight', lang: 'en-GB' })
    expect(queue.getState().supported).toBe(false)
    expect(queue.getState().transcript[0].text).toBe('text only')
  })
})

// ------------------------------------------------------------------ descriptions (from structured data only)

const en = a11yStrings('en')
const names: Record<string, string> = { NBR: 'Northbridge', WHA: 'Westhaven' }
const teamName = (id?: string) => (id ? names[id] ?? '' : '')
const opponentOf = (id?: string) => (id === 'NBR' ? 'Westhaven' : id === 'WHA' ? 'Northbridge' : '')

function card(type: string, severity: 'Low' | 'Medium' | 'High', metric = 'RightAttackShare', baseline: string | null = '31%'): InsightCard {
  return {
    id: 'I1', type, minute: 63, second: 3780, teamId: 'WHA', severity, confidence: 'High', confidenceScore: 0.9, importance: 80,
    span: { fromSecond: 3000, toSecond: 3720 }, visuals: [], supportingEventCount: 12,
    narrative: {
      insightId: 'I1', language: 'en', mode: 'Analyst', typeLabel: 'Match shift', headline: 'Westhaven switch to the right',
      fact: 'Attacks down the right rose from 31% to 90%', inference: 'They are targeting the right flank', explanation: 'A fresh winger',
      ticker: '', studioLine: '', confidenceLabel: 'High', severityLabel: 'High', source: 'mock',
      evidence: [{ metric, label: 'Attacks down the right (share)', baseline: baseline ?? undefined, recent: '90%', baselineWindow: "0'-50'", recentWindow: "50'-62'", sampleSize: 20, sampleRequirementMet: true, isPrimary: true }],
    },
  }
}

describe('insight descriptions', () => {
  it('pick what to speak by detail level', () => {
    expect(shouldSpeak(card('GOAL', 'High'), 'major')).toBe(true)
    expect(shouldSpeak(card('TEMPO_SHIFT', 'Medium'), 'major')).toBe(false)
    expect(shouldSpeak(card('TEMPO_SHIFT', 'Medium'), 'important')).toBe(true)
    expect(shouldSpeak(card('TURNOVER_SPIKE', 'Low'), 'important')).toBe(false)
    expect(shouldSpeak(card('TURNOVER_SPIKE', 'Low'), 'detailed')).toBe(true)
    expect(priorityOf(card('DANGEROUS_TURNOVER', 'High'))).toBe(2)
    expect(priorityOf(card('TURNOVER_SPIKE', 'Low'))).toBe(0)
  })

  it('use the channel named by the evidence', () => {
    const c = card('ATTACK_CHANNEL_SHIFT', 'High')
    expect(channelOf(c)).toBe('Right')
    expect(simplifiedText(c, en, teamName, opponentOf)).toBe('Westhaven are now attacking mostly down the right.')
    expect(simplifiedText(card('DANGEROUS_TURNOVER', 'High', 'LossLocationX'), en, teamName, opponentOf))
      .toBe('Westhaven lost the ball near their own goal and Northbridge got a shot.')
  })

  it('grow with the detail level', () => {
    const c = card('ATTACK_CHANNEL_SHIFT', 'High')
    const major = insightSpeech(c, 'major', false, en, teamName, opponentOf)
    const detailed = insightSpeech(c, 'detailed', false, en, teamName, opponentOf)
    expect(major).toBe("63'. Match shift, Westhaven. Westhaven switch to the right.")
    expect(detailed).toContain('They are targeting the right flank.')
    expect(detailed).toContain('A fresh winger.')
  })

  it('read the WHY? evidence and what changed', () => {
    const c = card('ATTACK_CHANNEL_SHIFT', 'High')
    const why = whyNarration(c, en)
    expect(why).toContain('Fact: Attacks down the right rose from 31% to 90%.')
    expect(why).toContain('Confidence: High.')
    expect(why).toContain("Attacks down the right (share): 31% → 90% (0'-50' → 50'-62')")
    expect(whatChanged(c, en)).toBe("At minute 63, MatchShift detected a change. Before the change, Attacks down the right (share) was 31% between 0' and 50'. Afterwards it was 90% between 50' and 62'.")
    expect(whatChanged(card('GOAL', 'High', 'ChanceQuality', null), en)).toBeNull()
  })
})

describe('match status and recaps', () => {
  const snap = (homeControl: number, score = { home: 1, away: 1 }) => ({
    clock: { second: 3600, minute: 60, period: 2, label: '60:00' }, score,
    home: { id: 'NBR', shortName: 'Northbridge' }, away: { id: 'WHA', shortName: 'Westhaven' },
    metrics: {
      homeControl,
      home: { attacks: 10, leftShare: 0.6, centreShare: 0.2, rightShare: 0.2 },
      away: { attacks: 10, leftShare: 0.1, centreShare: 0.2, rightShare: 0.7 },
    },
  }) as unknown as Snapshot

  it('describes time, score, control and attack side', () => {
    expect(matchStatus(snap(30), en)).toBe('60 minutes played. The match is level, Northbridge 1, Westhaven 1. Westhaven have had the upper hand in the last ten minutes. Their attacks are concentrated on the right.')
    expect(matchStatus(snap(50, { home: 2, away: 1 }), en)).toBe('60 minutes played. Northbridge lead 2 to 1. Control has been fairly even over the last ten minutes.')
  })

  it('schedules periodic recaps', () => {
    expect(periodicDue('5', 9, 10)).toBe(true)
    expect(periodicDue('5', 10, 11)).toBe(false)
    expect(periodicDue('10', 18, 21)).toBe(true)
    expect(periodicDue('halftime', 44, 45)).toBe(true)
    expect(periodicDue('off', 9, 10)).toBe(false)
    expect(periodicDue('5', 30, 10)).toBe(false) // seeking back never triggers a recap
  })
})

describe('spatial descriptions', () => {
  const players: ReplayPlayer[] = [
    { id: 'WHA-11', teamId: 'WHA', isHome: false, shirtNumber: 11, name: 'Samir Haddadi', shortName: 'Haddadi', position: 'LeftWinger', isGoalkeeper: false },
    { id: 'WHA-9', teamId: 'WHA', isHome: false, shirtNumber: 9, name: 'Nine', shortName: 'Nine', position: 'Striker', isGoalkeeper: false },
  ]
  const ev = (patch: Partial<ReplayEvent>): ReplayEvent => ({
    eventId: 'E', sequence: 1, time: 0, endTime: 1, matchSecond: 0, clock: '34:30', type: 'Pass', outcome: 'Complete', label: 'PASS',
    teamId: 'WHA', playerId: 'WHA-11', isEvidence: true, ...patch,
  })

  it('uses the attacking team frame (away team flipped)', () => {
    expect(teamFrame(20, 80, false)).toEqual({ x: 80, y: 20 })
    expect(metresFromGoal(100, 50)).toBe(0)
    expect(metresFromGoal(89, 50)).toBe(12)
  })

  it('describes passes, shots and recoveries from coordinates', () => {
    // Away team attacks towards X = 0: absolute (24,77) is team-frame (76,23) -> left side, attacking third.
    const pass = describeReplayEvent(ev({ label: 'CUTBACK', relatedPlayerId: 'WHA-9', x: 13, y: 77, endX: 8, endY: 55 }), players, 'NBR', en)
    expect(pass).toBe('Haddadi (Number 11) cuts the ball back to Nine (Number 9).')
    const fwd = describeReplayEvent(ev({ label: 'PROGRESSIVE_PASS', relatedPlayerId: 'WHA-9', x: 60, y: 77, endX: 30, endY: 77 }), players, 'NBR', en)
    expect(fwd).toBe('Haddadi (Number 11) passes forward to Nine (Number 9), on the left, the attacking third.')
    const shot = describeReplayEvent(ev({ type: 'Shot', outcome: 'Goal', label: 'GOAL', x: 11, y: 50 }), players, 'NBR', en)
    expect(shot).toBe('Haddadi (Number 11) shoots from about 12 metres. Haddadi (Number 11) scores!')
    const press = describeReplayEvent(ev({ type: 'Interception', label: 'HIGH_PRESS_INTERCEPTION', x: 30, y: 50 }), players, 'NBR', en)
    expect(press).toBe('Haddadi (Number 11) presses and intercepts high up the pitch, in the centre, the attacking third.')
  })

  it('summarises SHOW ME layers from the plotted events', () => {
    const text = describeFocus({
      insightId: 'I1',
      layers: [{ key: 'k', style: 'Routes', eventIds: ['a', 'b', 'c'], emphasised: true }],
      events: [
        { id: 'a', seq: 1, second: 3060, clock: '51:00', type: 'Pass', outcome: 'Complete', teamId: 'NBR', x: 60, y: 85, endX: 75, endY: 90 },
        { id: 'b', seq: 2, second: 3300, clock: '55:00', type: 'Pass', outcome: 'Complete', teamId: 'NBR', x: 70, y: 80, endX: 85, endY: 88 },
        { id: 'c', seq: 3, second: 3600, clock: '60:00', type: 'Shot', outcome: 'OffTarget', teamId: 'NBR', x: 88, y: 70 },
      ],
    }, 'NBR', 'NBR', teamName, en)
    expect(text).toBe('The visualisation shows 3 actions by Northbridge between minute 51 and minute 60. 2 are passes or carries. 2 reached the final third. 1 are shots. Most of the play is on the right.')
  })
})
