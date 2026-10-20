import { describe, expect, it } from 'vitest'
import { SpeechQueue, STICKY_GRACE_MS, cueRank } from './speech'
import type { CueClass, Priority, SynthLike, UtteranceLike, VoiceLike } from './speech'
import { insightCueClass, matchNarrationPolicy } from './narrationPolicy'
import { ReplayAudioFocus } from './audioFocus'

// Goals, half-time and full-time are never silently lost: goals outrank every other cue and interrupt it; boundaries wait
// a short grace behind a sentence already speaking. Ordinary cues are still dropped when stale. Fake synthesiser only.

class Synth implements SynthLike {
  spoken: UtteranceLike[] = []
  current: UtteranceLike | null = null
  cancelled = 0
  getVoices(): VoiceLike[] { return [{ name: 'English', lang: 'en-GB' }] }
  speak(u: UtteranceLike) { this.spoken.push(u); this.current = u }
  cancel() { this.cancelled++; this.current = null }
  pause() {}
  resume() {}
  end() { const u = this.current; this.current = null; u?.onend?.() }
  get texts() { return this.spoken.map(u => u.text) }
}

const make = () => {
  const synth = new Synth()
  const clock = { ms: 0 }
  const queue = new SpeechQueue({ synth, now: () => clock.ms, makeUtterance: (text) => ({ text, lang: '', rate: 1, volume: 1, voice: null, onend: null, onerror: null }) })
  // Real time passes and the match clock moves with it (at `speed`, 1x = 30 match seconds per real second).
  let second = 0
  const run = (realMs: number, speed: number) => {
    clock.ms += realMs
    second += (realMs / 1000) * 30 * speed
    queue.setTimeline('match', second, true)
  }
  const at = (s: number) => { second = s; queue.setTimeline('match', s, true) }
  return { synth, queue, clock, run, at, get second() { return second } }
}

const cue = (text: string, at: number, priority: Priority, cueClass?: CueClass) =>
  ({ text, kind: 'status', lang: 'en-GB', source: 'match' as const, at, priority, cueClass })
const goal = (text: string, at: number) => cue(text, at, 2, 'goal')
const boundary = (text: string, at: number) => cue(text, at, 2, 'boundary')

describe('semantic priority', () => {
  it('ranks goal = boundary > major verified insight > other critical > normal > low, from event semantics only', () => {
    expect(cueRank({ priority: 2, cueClass: 'goal' })).toBe(cueRank({ priority: 2, cueClass: 'boundary' }))
    expect(cueRank({ priority: 2, cueClass: 'goal' })).toBeGreaterThan(cueRank({ priority: 2, cueClass: 'major' }))
    expect(cueRank({ priority: 1, cueClass: 'major' })).toBeGreaterThan(cueRank({ priority: 2 }))
    expect(cueRank({ priority: 2 })).toBeGreaterThan(cueRank({ priority: 1 }))
    expect(cueRank({ priority: 1 })).toBeGreaterThan(cueRank({ priority: 0 }))
    expect(insightCueClass({ type: 'GOAL', severity: 'High' })).toBe('goal')
    expect(insightCueClass({ type: 'DANGEROUS_TURNOVER', severity: 'High', agent: { verified: true } })).toBe('major')
    expect(insightCueClass({ type: 'DANGEROUS_TURNOVER', severity: 'High', agent: { verified: false } })).toBeUndefined()
    expect(insightCueClass({ type: 'DANGEROUS_TURNOVER', severity: 'High' })).toBeUndefined()
    expect(insightCueClass({ type: 'MATCH_SHIFT', severity: 'Medium', agent: { verified: true } })).toBeUndefined()
  })
})

describe('goals are never lost', () => {
  it('1, 2: a goal interrupts a non-goal critical cue, and the interrupted cue is not resumed', () => {
    const { synth, queue, at } = make()
    at(2080)
    queue.say(cue('Key moment, Westhaven', 2080, 2))
    at(2100)
    queue.say(goal('Goal for Northbridge', 2100))
    expect(synth.cancelled).toBe(1)
    expect(synth.texts).toEqual(['Key moment, Westhaven', 'Goal for Northbridge'])
    synth.end()
    expect(synth.texts).toEqual(['Key moment, Westhaven', 'Goal for Northbridge'])   // never resumed
    expect(queue.pending).toBe(0)
    queue.say(cue('Major verified insight', 2100, 2, 'major'))
    at(2110)
    queue.say(goal('Second goal', 2110))
    expect(synth.texts.at(-1)).toBe('Second goal')                                    // a major insight is cut too
  })

  it('3: at 10x a goal is not lost behind a key moment, and a second goal waits (bounded) instead of vanishing', () => {
    const { synth, queue, at, run } = make()
    const policy = matchNarrationPolicy(10)
    expect(policy.minPriority).toBe(2)                                   // 10x: critical only
    at(780)
    queue.say(cue("13'. Key moment, Northbridge", 780, 2))
    run(4000, 10)                                                        // 4 s of speech = 20 match minutes at 10x
    at(2100)
    queue.say(goal('GOAL! Blackwood breaks the deadlock', 2100))
    expect(synth.texts.at(-1)).toBe('GOAL! Blackwood breaks the deadlock')
    run(1000, 10)
    queue.say(goal('Goal for Westhaven. 1-1', 2400))                    // another goal, while the first is speaking
    expect(queue.pending).toBe(1)
    run(4000, 10)                                                        // 20 match minutes pass while the first is spoken
    synth.end()
    expect(synth.texts).toEqual(["13'. Key moment, Northbridge", 'GOAL! Blackwood breaks the deadlock', 'Goal for Westhaven. 1-1'])
  })

  it('a newer goal is merged into the waiting goal: still at most one waiting goal, and no goal is lost', () => {
    const { synth, queue, at } = make()
    at(3000)
    queue.say(goal('Goal one (1-0)', 3000))
    at(3010)
    queue.say(goal('Goal two (2-0)', 3010))
    at(3020)
    queue.say(goal('Goal three (2-1)', 3020))
    expect(queue.pending).toBe(1)
    synth.end()
    expect(synth.texts).toEqual(['Goal one (1-0)', 'Goal two (2-0) Goal three (2-1)'])
    expect(queue.getState().transcript.filter(e => e.text.startsWith('Goal')).every(e => e.spoken)).toBe(true)
  })

  it('3b: at 10x every goal of a four-goal match is spoken, with half-time and full-time (browser-observed timings)', () => {
    const { synth, queue, at, clock } = make()
    const speak = (s: number, ms: number) => { clock.ms += ms; at(s) }
    at(780); queue.say(cue("13'. Key moment", 780, 2))
    speak(2100, 4400); queue.say(goal('Goal 1-0.', 2100))                // 35': interrupts the key moment
    speak(2640, 1800); queue.say(goal('Goal 1-1.', 2640))                // 44': waits behind 1-0
    speak(2700, 200);  queue.say(boundary('Half-time.', 2700))           // 45': waits too
    speak(3710, 3400); synth.end()                                       // 1-0 ends at about 62': 1-1 starts
    speak(4500, 2600); queue.say(goal('Goal 1-2.', 4500))                // 75': waits
    speak(5130, 2100); synth.end()                                       // 1-1 ends at 85': half-time (FIFO)
    speak(5280, 500);  queue.say(goal('Goal 2-2.', 5280))                // 88': merged with the waiting 1-2
    speak(5400, 400);  queue.say(boundary('Full-time.', 5400))
    synth.end(); synth.end(); synth.end()
    expect(synth.texts).toEqual(["13'. Key moment", 'Goal 1-0.', 'Goal 1-1.', 'Half-time.', 'Goal 1-2. Goal 2-2.', 'Full-time.'])
    expect(queue.pending).toBe(0)
  })
})

describe('match boundaries are sticky', () => {
  it('4, 5: half-time survives briefly behind a speaking critical cue and is spoken when it finishes (2x)', () => {
    const { synth, queue, at, run } = make()
    at(2580)
    queue.say(cue("43'. Key moment, Westhaven", 2580, 2))
    run(4000, 2)                                                         // 2x: 4 minutes later
    at(2700)
    queue.say(boundary('Half-time', 2700))
    expect(synth.cancelled).toBe(0)                                      // waits, does not cut the sentence
    run(4000, 2)                                                         // now 4 match minutes past 45' - beyond its window
    expect(queue.pending).toBe(1)                                        // still kept: within the grace period
    synth.end()
    expect(synth.texts).toEqual(["43'. Key moment, Westhaven", 'Half-time'])
  })

  it('half-time still interrupts non-critical narration, as before', () => {
    const { synth, queue, at } = make()
    at(2690)
    queue.say(cue('normal insight', 2690, 1))
    at(2700)
    queue.say(boundary('Half-time', 2700))
    expect(synth.cancelled).toBe(1)
    expect(synth.texts.at(-1)).toBe('Half-time')
  })

  it('6: half-time does not interrupt a goal, and is spoken after it', () => {
    const { synth, queue, at, run } = make()
    at(2602)
    queue.say(goal('Goal for Westhaven. 1-1', 2602))
    run(2000, 2)
    queue.say(boundary('Half-time', 2700))
    expect(synth.cancelled).toBe(0)
    expect(synth.texts).toEqual(['Goal for Westhaven. 1-1'])
    run(3000, 2)
    synth.end()
    expect(synth.texts).toEqual(['Goal for Westhaven. 1-1', 'Half-time'])
  })

  it('7: full-time has the same protection (behind a goal on the whistle and behind a key moment)', () => {
    for (const before of [goal('Goal on the whistle', 5395), cue('Key moment', 5395, 2)]) {
      const { synth, queue, at, clock } = make()
      at(5395)
      queue.say(before)
      at(5400)
      queue.setTimeline('match', 5400, true)                             // finished: still live (matchTimelineLive)
      queue.say(boundary('Full-time', 5400))
      expect(synth.cancelled).toBe(0)
      clock.ms += 6000
      synth.end()
      expect(synth.texts).toEqual([before.text, 'Full-time'])
    }
  })

  it('8: only one waiting boundary - full-time replaces a waiting half-time; a goal and a boundary may both wait', () => {
    const { synth, queue, at } = make()
    at(2700)
    queue.say(goal('Goal', 2700))
    queue.say(boundary('Half-time', 2700))
    at(2710)
    queue.say(boundary('Full-time', 2710))
    expect(queue.pending).toBe(1)
    queue.say(goal('Another goal', 2710))
    expect(queue.pending).toBe(2)                                        // one goal + one boundary, never more
    queue.say(cue('ordinary', 2710, 1))
    expect(queue.pending).toBe(2)                                        // ordinary cues never wait behind them
    synth.end(); synth.end(); synth.end()
    expect(synth.texts).toEqual(['Goal', 'Full-time', 'Another goal'])
  })

  it('the grace is bounded: a boundary that waits longer than the grace is dropped (text only)', () => {
    const { synth, queue, at, run } = make()
    at(2600)
    queue.say(goal('Goal', 2600))
    queue.say(boundary('Half-time', 2700))
    run(STICKY_GRACE_MS + 1000, 2)
    synth.end()
    expect(synth.texts).toEqual(['Goal'])
    expect(queue.getState().transcript.map(e => e.text)).toContain('Half-time')
  })

  it('a goal or boundary that is already stale when raised gets no grace', () => {
    const { synth, queue, at } = make()
    at(3000)
    queue.say(cue('speaking', 3000, 2))
    queue.say(boundary('Half-time from long ago', 2700))
    expect(queue.pending).toBe(0)
    synth.end()
    expect(synth.texts).toEqual(['speaking'])
  })
})

describe('no backlog of ordinary commentary', () => {
  it('9: ordinary stale narration is still dropped (no grace)', () => {
    const { synth, queue, at, run } = make()
    at(1000)
    queue.say(cue('critical', 1000, 2))
    queue.say(cue('ordinary', 1000, 1))
    run(4000, 2)                                                         // 2 match minutes: beyond the 90 s window
    synth.end()
    expect(synth.texts).toEqual(['critical'])
  })

  it('10: the queue stays bounded under a flood of every kind of cue', () => {
    const { queue, at } = make()
    at(1000)
    const classes: (CueClass | undefined)[] = [undefined, 'major', 'goal', 'boundary']
    for (let i = 0; i < 200; i++) {
      queue.say(cue(`c${i}`, 1000, (i % 3) as Priority, classes[i % 4]))
      expect(queue.pending).toBeLessThanOrEqual(2)
    }
  })

  it('11: playback speed never changes the speech rate', () => {
    for (const speed of [1, 2, 5, 10]) {
      const { synth, queue, at, run } = make()
      queue.rate = 1.1
      at(100)
      run(1000, speed)
      void matchNarrationPolicy(speed)
      queue.say(goal(`goal at ${speed}x`, 100 + 30 * speed))
      expect(synth.spoken[0].rate).toBe(1.1)
    }
  })
})

describe('Evidence Replay focus is unchanged', () => {
  it('12: opening cancels a speaking goal and clears waiting boundaries; match goals stay text-only; closing resumes nothing', () => {
    const { synth, queue, at } = make()
    const calls: string[] = []
    const deps = { speech: queue, pause: () => calls.push('pause'), play: () => calls.push('play') }
    const focus = new ReplayAudioFocus()
    at(2650)
    queue.say(goal('Goal', 2650))
    queue.say(boundary('Half-time', 2700))
    focus.open({ matchId: 'm', epoch: 1, isPlaying: true }, deps)
    expect(calls).toEqual(['pause'])
    expect(synth.cancelled).toBe(1)
    expect(queue.pending).toBe(0)
    queue.say(goal('Goal during replay', 2700))
    expect(synth.texts).toEqual(['Goal'])
    expect(queue.getState().transcript.at(-1)?.text).toBe('Goal during replay')
    focus.close({ matchId: 'm', epoch: 1, isPlaying: false }, deps)
    expect(calls).toEqual(['pause', 'play'])
    expect(queue.pending).toBe(0)
    expect(synth.texts).toEqual(['Goal'])
  })
})
