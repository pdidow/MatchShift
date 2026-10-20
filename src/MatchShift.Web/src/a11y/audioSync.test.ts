import { describe, expect, it } from 'vitest'
import { SpeechQueue, isCurrent, RELEVANCE } from './speech'
import type { SynthLike, UtteranceLike, VoiceLike } from './speech'
import { capDetail, matchNarrationPolicy, matchTimelineLive, replayCuePriority, replayNarrationPolicy } from './narrationPolicy'
import { ReplayAudioFocus } from './audioFocus'
import type { MatchPlayback } from './audioFocus'
import { speechLang } from './strings'
import { LANGUAGE_CODES } from '../languages'
import { crowdMotionAllowed } from '../replay/crowd'

// Speech follows the timeline: match narration follows the match clock, replay narration the replay clock. The timeline
// never waits for speech. Everything here uses a fake synthesiser: no browser voices, no Azure, no Foundry.

class Synth implements SynthLike {
  spoken: UtteranceLike[] = []
  current: UtteranceLike | null = null
  cancelled = 0
  private readonly voices: VoiceLike[]
  constructor(voices: VoiceLike[] = [{ name: 'English', lang: 'en-GB' }]) { this.voices = voices }
  speak(u: UtteranceLike) { this.spoken.push(u); this.current = u }
  cancel() { this.cancelled++; this.current = null }
  pause() {}
  resume() {}
  getVoices() { return this.voices }
  end() { const u = this.current; this.current = null; u?.onend?.() }
  get texts() { return this.spoken.map(u => u.text) }
}

const make = (voices?: VoiceLike[]) => {
  const synth = new Synth(voices)
  const queue = new SpeechQueue({ synth, now: () => 0, makeUtterance: (text) => ({ text, lang: '', rate: 1, volume: 1, voice: null, onend: null, onerror: null }) })
  return { synth, queue }
}
const match = (text: string, at: number, priority: 0 | 1 | 2 = 1) => ({ text, kind: 'insight', lang: 'en-GB', source: 'match' as const, at, priority })
const replay = (text: string, at: number, priority: 0 | 1 | 2 = 1) => ({ text, kind: 'replay', lang: 'en-GB', source: 'replay' as const, at, priority })

// ---------------------------------------------------------------- normal match

describe('normal match narration', () => {
  it('1: at 1x a burst of cues never builds a backlog', () => {
    const { synth, queue } = make()
    queue.setTimeline('match', 600, true)
    for (let i = 0; i < 20; i++) queue.say(match(`cue ${i}`, 600))
    expect(queue.pending).toBeLessThanOrEqual(1)
    synth.end(); synth.end(); synth.end()
    expect(synth.texts).toEqual(['cue 0', 'cue 19'])          // the first, then only the newest one
  })

  it('2, 6: at the default 2x cues the match has moved past are dropped, never spoken late', () => {
    const { synth, queue } = make()
    queue.setTimeline('match', 3734, true)                       // 62:14
    queue.say(match('first insight', 3734))
    queue.say(match('second insight', 3736))                     // waits
    queue.setTimeline('match', 3836, true)                       // 63:56 - the match ran on while the first was spoken
    synth.end()
    expect(synth.texts).toEqual(['first insight'])               // the second was stale and disappeared
    expect(queue.pending).toBe(0)
    queue.say(match('late cue about 62:14', 3734))
    expect(synth.texts).toEqual(['first insight'])               // a cue that is already stale is text only
    expect(queue.getState().transcript.at(-1)?.text).toBe('late cue about 62:14')
  })

  it('6: a cue a few match seconds old is still current; one well past its window is not', () => {
    expect(isCurrent(3734, 3736, RELEVANCE.match[1])).toBe(true)          // 62:14 → 62:16
    expect(isCurrent(3734, 3734 + RELEVANCE.match[1] + 1, RELEVANCE.match[1])).toBe(false)
    expect(isCurrent(3734, 3600, RELEVANCE.match[1])).toBe(false)         // after a seek back: "from the future"
    expect(RELEVANCE.match[2]).toBeGreaterThan(RELEVANCE.match[0])        // important cues stay useful longer
  })

  it('3, 11 (rate): the speech rate is the viewer\'s, never scaled by playback speed', () => {
    for (const speed of [1, 2, 5, 10]) {
      const { synth, queue } = make()
      queue.rate = 1.1
      queue.setTimeline('match', 100, true)
      void matchNarrationPolicy(speed)
      queue.say(match('x', 100))
      expect(synth.spoken[0].rate).toBe(1.1)
    }
  })

  it('4, 5: faster playback narrates fewer, more important things', () => {
    expect(matchNarrationPolicy(1)).toEqual({ minPriority: 0, detailCap: 'detailed' })
    expect(matchNarrationPolicy(2)).toEqual({ minPriority: 0, detailCap: 'major' })   // concise at the default speed
    expect(matchNarrationPolicy(5)).toEqual({ minPriority: 1, detailCap: 'major' })
    expect(matchNarrationPolicy(10)).toEqual({ minPriority: 2, detailCap: 'major' })
    expect(capDetail('detailed', 'important')).toBe('important')
    expect(capDetail('major', 'detailed')).toBe('major')                  // never more detail than the viewer chose
  })

  it('7: a current critical cue supersedes waiting and speaking lower-priority narration', () => {
    const { synth, queue } = make()
    queue.setTimeline('match', 500, true)
    queue.say(match('routine', 500, 0))
    queue.say(match('another routine', 500, 0))
    queue.say(match('GOAL', 501, 2))
    expect(synth.cancelled).toBe(1)
    expect(synth.texts).toEqual(['routine', 'GOAL'])
    synth.end()
    expect(synth.texts).toEqual(['routine', 'GOAL'])                      // the routine one waiting was replaced
  })

  it('8: a lower-priority cue never interrupts, and never displaces a more important waiting cue', () => {
    const { synth, queue } = make()
    queue.setTimeline('match', 500, true)
    queue.say(match('insight', 500, 1))
    queue.say(match('tactical shift', 500, 1))
    queue.say(match('routine', 501, 0))
    expect(synth.cancelled).toBe(0)
    synth.end()
    expect(synth.texts).toEqual(['insight', 'tactical shift'])
  })

  it('9: the queue stays bounded across sources', () => {
    const { queue } = make()
    queue.setTimeline('match', 500, true)
    for (let i = 0; i < 50; i++) queue.say(match(`m${i}`, 500, (i % 3) as 0 | 1 | 2))
    expect(queue.pending).toBeLessThanOrEqual(1)
  })

  it('10: a paused match starts no new match cues; the viewer\'s own requests still speak', () => {
    const { synth, queue } = make()
    queue.setTimeline('match', 500, false)
    queue.say(match('while paused', 500))
    expect(synth.texts).toEqual([])
    queue.say({ text: 'Read explanation', kind: 'why', lang: 'en-GB' }, { allowRepeat: true })
    expect(synth.texts).toEqual(['Read explanation'])
    synth.end()
    queue.setTimeline('match', 500, true)                                 // resuming plays the cue only if still current
    expect(synth.texts).toEqual(['Read explanation', 'while paused'])
  })

  it('10b: full time is spoken although the match clock has stopped (a viewer pause still holds cues)', () => {
    const { synth, queue } = make()
    const ended = { isPlaying: false, finished: true }
    queue.setTimeline('match', 5400, true)
    queue.say(match('goal on the whistle', 5395, 2))
    queue.setTimeline('match', 5400, matchTimelineLive(ended))
    queue.say(match('Full time', 5400, 2))
    synth.end()
    expect(synth.texts).toEqual(['goal on the whistle', 'Full time'])
    expect(matchTimelineLive({ isPlaying: false, finished: false })).toBe(false)
  })

  it('11: a language switch stops and clears old-language narration', () => {
    const { synth, queue } = make([{ name: 'en', lang: 'en-GB' }, { name: 'de', lang: 'de-DE' }])
    queue.setLanguage('en-GB')
    queue.setTimeline('match', 500, true)
    queue.say(match('english speaking', 500))
    queue.say(match('english waiting', 500))
    queue.setLanguage('de-DE')
    expect(synth.cancelled).toBe(1)
    expect(queue.pending).toBe(0)
    queue.say({ ...match('deutsch', 500), lang: 'de-DE' })
    expect(synth.spoken.at(-1)?.lang).toBe('de-DE')
  })

  it('12: changing speed mid-match does not corrupt speech state', () => {
    const { synth, queue } = make()
    queue.setTimeline('match', 500, true)
    queue.say(match('a', 500))
    let clock = 500
    for (const speed of [1, 2, 5, 10, 2]) { void matchNarrationPolicy(speed); clock += speed; queue.setTimeline('match', clock, true) }
    synth.end()
    queue.say(match('b', 510))
    expect(synth.texts).toEqual(['a', 'b'])
    expect(queue.getState().speaking).toBe(true)
  })
})

// ---------------------------------------------------------------- Evidence Replay

const playback = (isPlaying: boolean, epoch = 1): MatchPlayback => ({ matchId: 'MS-FLAGSHIP-001', epoch, isPlaying })

describe('Evidence Replay audio focus', () => {
  const setup = () => {
    const { synth, queue } = make()
    const calls: string[] = []
    const deps = { speech: queue, pause: () => calls.push('pause'), play: () => calls.push('play') }
    return { synth, queue, calls, deps, focus: new ReplayAudioFocus() }
  }

  it('13, 14, 15: opening pauses the playing match and stops and clears match narration', () => {
    const { synth, queue, calls, deps, focus } = setup()
    queue.setTimeline('match', 3700, true)
    queue.say(match('speaking', 3700))
    queue.say(match('waiting', 3700))
    focus.open(playback(true), deps)
    expect(calls).toEqual(['pause'])
    expect(synth.cancelled).toBe(1)
    expect(queue.pending).toBe(0)
    expect(queue.getFocus()).toBe('replay')
  })

  it('16, 17: match narration cannot speak while the replay owns focus; replay narration can', () => {
    const { synth, queue, deps, focus } = setup()
    focus.open(playback(true), deps)
    queue.setTimeline('match', 3700, true)
    queue.say(match('match cue', 3700, 2))
    expect(synth.texts).toEqual([])
    queue.setTimeline('replay', 2, true)
    queue.say(replay('replay cue', 2))
    expect(synth.texts).toEqual(['replay cue'])
  })

  it('18, 26: replay utterances never overlap and fast replay builds no backlog', () => {
    const { synth, queue, deps, focus } = setup()
    focus.open(playback(false), deps)
    queue.setTimeline('replay', 0, true)
    for (let t = 0; t < 10; t += 0.5) { queue.setTimeline('replay', t, true); queue.say(replay(`event ${t}`, t)) }
    expect(synth.spoken.filter(u => u === synth.current)).toHaveLength(1)
    expect(queue.pending).toBeLessThanOrEqual(1)
    expect(synth.texts).toEqual(['event 0'])                              // the rest are dropped or replaced, not queued
  })

  it('19, 20: replay cues are tied to replay time and dropped once the replay has passed them', () => {
    const { synth, queue, deps, focus } = setup()
    focus.open(playback(false), deps)
    queue.setTimeline('replay', 4, true)
    queue.say(replay('pass at 4s', 4, 0))
    queue.say(replay('evidence at 4.5s', 4.5, 1))
    queue.setTimeline('replay', 9, true)                                  // moved well past both
    synth.end()
    expect(synth.texts).toEqual(['pass at 4s'])
    expect(queue.pending).toBe(0)
  })

  it('21, 22: a seek stops stale speech and never narrates skipped events', () => {
    const { synth, queue, deps, focus } = setup()
    focus.open(playback(false), deps)
    queue.setTimeline('replay', 2, true)
    queue.say(replay('at 2s', 2))
    queue.setTimeline('replay', 20, true)                                 // seek forward
    queue.clearKind('replay')                                             // ReplayView does this on every jump
    expect(synth.cancelled).toBeGreaterThanOrEqual(1)                     // the 2 s sentence no longer describes the screen
    queue.say(replay('skipped at 10s', 10))
    expect(synth.texts).toEqual(['at 2s'])
    queue.clearKind('replay')
    queue.setTimeline('replay', 1, true)                                  // seek back: new position, new cues allowed
    queue.say(replay('at 1s', 1))
    expect(synth.texts).toEqual(['at 2s', 'at 1s'])
  })

  it('23: restart resets replay narration state', () => {
    const { synth, queue, deps, focus } = setup()
    focus.open(playback(false), deps)
    queue.setTimeline('replay', 15, true)
    queue.say(replay('late', 15))
    queue.clearKind('replay')                                             // ReplayView does this on a jump back to 0
    queue.setTimeline('replay', 0, true)
    queue.say(replay('from the start', 0))
    expect(synth.texts).toEqual(['late', 'from the start'])
  })

  it('a sentence already speaking finishes during normal playback; it is only cut by a seek, restart or a reset', () => {
    const { synth, queue } = make()
    queue.setTimeline('match', 3700, true)
    queue.say(match('three-second sentence', 3700))
    queue.setTimeline('match', 3900, true)                                 // 10x ran far on while it was spoken
    expect(synth.cancelled).toBe(0)
    queue.clearSource('match')                                            // the viewer seeks / resets the match
    expect(synth.cancelled).toBe(1)
  })

  it('24: a paused replay starts no new cues (a sentence already speaking may finish)', () => {
    const { synth, queue, deps, focus } = setup()
    focus.open(playback(false), deps)
    queue.setTimeline('replay', 3, true)
    queue.say(replay('speaking', 3))
    queue.setTimeline('replay', 3, false)
    queue.say(replay('waits', 3))
    synth.end()
    expect(synth.texts).toEqual(['speaking'])
    expect(synth.cancelled).toBe(0)
  })

  it('25: replay speed never multiplies the speech rate; faster replay narrates evidence, goals and transitions only', () => {
    const { synth, queue, deps, focus } = setup()
    queue.rate = 1.25
    focus.open(playback(false), deps)
    queue.setTimeline('replay', 1, true)
    queue.say(replay('x', 1))
    expect(synth.spoken[0].rate).toBe(1.25)
    expect(replayNarrationPolicy(1).minPriority).toBe(0)
    expect(replayNarrationPolicy(2).minPriority).toBe(1)
    expect(replayCuePriority({ label: 'GOAL', isEvidence: false })).toBe(2)
    expect(replayCuePriority({ label: 'PASS', isEvidence: true })).toBe(1)
    expect(replayCuePriority({ label: 'PASS', isEvidence: false })).toBe(0)
  })

  it('27, 28: closing stops replay speech and does not resume the interrupted match narration', () => {
    const { synth, queue, deps, focus } = setup()
    queue.setTimeline('match', 3700, true)
    queue.say(match('interrupted', 3700))
    focus.open(playback(true), deps)
    queue.setTimeline('replay', 1, true)
    queue.say(replay('replay speaking', 1))
    focus.close(playback(false), deps)
    expect(queue.getFocus()).toBe('match')
    expect(synth.current).toBeNull()
    synth.end()
    expect(synth.texts).toEqual(['interrupted', 'replay speaking'])        // nothing old comes back
    queue.say(match('new current cue', 3700))
    expect(synth.texts.at(-1)).toBe('new current cue')
  })

  it('29, 30: play/pause is restored and the playback speed is never touched', () => {
    const { calls, deps, focus } = setup()
    focus.open(playback(true), deps)
    focus.close(playback(false), deps)
    expect(calls).toEqual(['pause', 'play'])                             // only pause/play: speed is not an argument
    calls.length = 0
    focus.open(playback(false), deps)
    focus.close(playback(false), deps)
    expect(calls).toEqual([])                                            // a paused match stays paused
    focus.open(playback(true, 1), deps)
    focus.close(playback(false, 2), deps)                                // reset/new match while open: never auto-resumed
    expect(calls).toEqual(['pause'])
  })

  it('31: every supported language keeps the right utterance.lang under replay focus', () => {
    const voices = LANGUAGE_CODES.map(l => ({ name: l, lang: speechLang(l) }))
    const { synth, queue } = make(voices)
    const focus = new ReplayAudioFocus()
    focus.open(playback(false), { speech: queue, pause: () => {}, play: () => {} })
    queue.setTimeline('replay', 1, true)
    for (const lang of LANGUAGE_CODES) {
      queue.setLanguage(speechLang(lang))
      queue.say({ ...replay(`text ${lang}`, 1), lang: speechLang(lang) })
      expect(synth.spoken.at(-1)?.lang).toBe(speechLang(lang))
      synth.end()
    }
  })

  it('32: the crowd has no say in audio focus', () => {
    const { queue, deps, focus } = setup()
    focus.open(playback(true), deps)
    for (const tier of ['off', 'low', 'normal'] as const) void crowdMotionAllowed(tier, false, false)
    expect(queue.getFocus()).toBe('replay')
  })
})
