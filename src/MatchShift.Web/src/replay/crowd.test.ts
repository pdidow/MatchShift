import crowdSource from './crowd.ts?raw'
import { describe, expect, it } from 'vitest'
import {
  CROWD_KEY, CROWD_REACTIONS, CrowdFrameGuard, FRONT_CLEARANCE, autoCrowdTier, crowdMotionAllowed, crowdReaction, layoutStand,
  loadCrowdChoice, readCrowdCapabilities, resolveCrowdTier, saveCrowdChoice, standSurface,
} from './crowd'
import type { CrowdCapabilities } from './crowd'
import { layout } from './timeline'
import type { ReplayClip, ReplayEvent, ReplaySequence } from './types'
import { t } from '../i18n'
import { LANGUAGE_CODES } from '../languages'

// The four stands exactly as scene.ts builds them.
const SIDE = { length: 125, depth: 18, height: 9 }
const END = { length: 82, depth: 15, height: 8 }
const STANDS = [SIDE, SIDE, END, END]
const count = (tier: 'off' | 'low' | 'normal') => STANDS.reduce((n, s, i) => n + layoutStand(s, tier, 7001 + i).length, 0)

const strong: CrowdCapabilities = { webgl: true, webgl2: true, cores: 12, deviceMemory: 16, maxTextureSize: 16384, pixelRatio: 1, viewportPixels: 1920 * 1080 }

describe('crowd tiers and layout', () => {
  it('OFF renders no spectators; LOW uses fewer than NORMAL', () => {
    expect(count('off')).toBe(0)
    expect(count('low')).toBeGreaterThan(500)
    expect(count('normal')).toBeGreaterThan(count('low') * 1.8)
    expect(count('normal')).toBeLessThan(3000)                              // restrained density
  })

  it('is deterministic: the same seed gives the same crowd', () => {
    expect(layoutStand(SIDE, 'normal', 7001)).toEqual(layoutStand(SIDE, 'normal', 7001))
    expect(layoutStand(SIDE, 'normal', 7001)).not.toEqual(layoutStand(SIDE, 'normal', 7002))
  })

  it('keeps every spectator in the stand, clear of the front edge, on the stepped surface, in a narrow scale range', () => {
    for (const s of STANDS) for (const p of layoutStand(s, 'normal', 1)) {
      expect(p.x).toBeGreaterThanOrEqual(FRONT_CLEARANCE - 0.1)            // low cameras sit in front of the crowd
      expect(p.x).toBeLessThanOrEqual(s.depth)
      expect(Math.abs(p.z)).toBeLessThanOrEqual(s.length / 2)
      expect(p.y).toBeCloseTo(standSurface(s, p.x), 0)
      expect(p.scale).toBeGreaterThanOrEqual(0.94)
      expect(p.scale).toBeLessThanOrEqual(1.06)
    }
  })

  it('stays behind the low cameras: sideline (z = 47) and behind goal (x = 66)', () => {
    // Side stands start at z = 34 + 10, end stands at x = 52.5 + 12 (scene.ts); cameras are inside those fronts.
    const nearestSideZ = 34 + 10 + Math.min(...layoutStand(SIDE, 'normal', 7001).map(p => p.x))
    const nearestEndX = 52.5 + 12 + Math.min(...layoutStand(END, 'normal', 7003).map(p => p.x))
    expect(nearestSideZ).toBeGreaterThan(47)
    expect(nearestEndX).toBeGreaterThan(66)
  })
})

describe('defaults and user choice', () => {
  it('capable hardware gets NORMAL; modest gets LOW; limited, unknown or no WebGL2 gets OFF', () => {
    expect(autoCrowdTier(strong)).toBe('normal')
    expect(autoCrowdTier({ ...strong, cores: 4, deviceMemory: 8 })).toBe('low')
    expect(autoCrowdTier({ ...strong, pixelRatio: 2, viewportPixels: 3840 * 2160 })).toBe('low')   // heavy screen
    expect(autoCrowdTier({ ...strong, deviceMemory: 2 })).toBe('off')
    expect(autoCrowdTier({ ...strong, cores: 2 })).toBe('off')
    expect(autoCrowdTier({ ...strong, cores: undefined })).toBe('off')                              // unknown → off
    expect(autoCrowdTier({ ...strong, webgl2: false })).toBe('off')
    expect(autoCrowdTier({ webgl: false })).toBe('off')
    expect(autoCrowdTier({ ...strong, deviceMemory: undefined })).toBe('normal')                    // Safari/Firefox omit memory
  })

  it('an explicit OFF is always respected; an explicit ON wins over the automatic default (LOW on weak hardware)', () => {
    expect(resolveCrowdTier('normal', 'off', strong, false)).toBe('off')
    expect(resolveCrowdTier('off', 'on', strong, false)).toBe('low')
    expect(resolveCrowdTier('normal', 'on', strong, true)).toBe('normal')    // the user's later ON replaces the guard's decision
    expect(resolveCrowdTier('normal', null, strong, false)).toBe('normal')
    expect(resolveCrowdTier('normal', null, strong, true)).toBe('off')       // never forced back on after the guard tripped
    expect(resolveCrowdTier('normal', 'on', { webgl: false }, false)).toBe('off')  // technically impossible
  })

  it('reads capabilities without crashing when browser APIs are missing', () => {
    const caps = readCrowdCapabilities(undefined)
    expect(caps.webgl).toBe(false)
    expect(autoCrowdTier(caps)).toBe('off')
    expect(() => readCrowdCapabilities({})).not.toThrow()
    expect(readCrowdCapabilities({}).webgl2).toBeUndefined()
  })

  it('remembers an explicit choice, and survives unavailable storage', () => {
    const store = new Map<string, string>()
    const fake = { getItem: (k: string) => store.get(k) ?? null, setItem: (k: string, v: string) => void store.set(k, v) }
    globalThis.localStorage = fake as unknown as Storage
    expect(loadCrowdChoice()).toBeNull()
    saveCrowdChoice('off')
    expect(store.get(CROWD_KEY)).toBe('off')
    expect(loadCrowdChoice()).toBe('off')
    globalThis.localStorage = { getItem: () => { throw new Error('blocked') }, setItem: () => { throw new Error('blocked') } } as unknown as Storage
    expect(loadCrowdChoice()).toBeNull()
    expect(() => saveCrowdChoice('on')).not.toThrow()
  })
})

describe('performance guard', () => {
  const run = (frameMs: number, seconds = 5) => {
    const g = new CrowdFrameGuard()
    let tripped = 0
    for (let s = 0; s < seconds; s += frameMs / 1000) if (g.feed(frameMs / 1000)) tripped++
    return { tripped, finished: g.finished }
  }

  it('switches the crowd off once when frames are sustained-slow, and never when they are fine', () => {
    expect(run(50)).toEqual({ tripped: 1, finished: true })     // 20 fps
    expect(run(16.7)).toEqual({ tripped: 0, finished: true })   // 60 fps
    expect(run(50, 20).tripped).toBe(1)                         // measured once, then it stops
  })

  it('ignores warm-up and tab-switch gaps', () => {
    const g = new CrowdFrameGuard()
    for (let i = 0; i < 50; i++) expect(g.feed(0.5)).toBe(false)   // long gaps are ignored entirely
    expect(g.finished).toBe(false)
  })
})

// ---------------------------------------------------------------- reactions

const ev = (label: string, time: number): ReplayEvent => ({
  eventId: `E${label}${time}`, sequence: 1, time, endTime: time, matchSecond: 100, clock: '01:40', type: 'Pass', outcome: 'Complete',
  label, teamId: 'H', isEvidence: true,
})

describe('crowd reactions', () => {
  it('react only to known replay event labels; routine events do nothing', () => {
    expect(crowdReaction([ev('GOAL', 2)], 2.3)).toBeGreaterThan(0.9)
    expect(crowdReaction([ev('SHOT_ON_TARGET', 2)], 2.3)).toBeGreaterThan(0.3)
    expect(crowdReaction([ev('INTERCEPTION', 2)], 2.3)).toBeLessThan(0.2)
    for (const label of ['PASS', 'PROGRESSIVE_PASS', 'CARRY', 'CLEARANCE', 'CORNER', 'KICK_OFF', 'UNKNOWN'])
      expect(crowdReaction([ev(label, 2)], 2.3)).toBe(0)
    expect(Object.keys(CROWD_REACTIONS)).not.toContain('PASS')
  })

  it('are short: nothing before the event or after its duration', () => {
    expect(crowdReaction([ev('GOAL', 2)], 1.9)).toBe(0)
    expect(crowdReaction([ev('GOAL', 2)], 2 + CROWD_REACTIONS.GOAL.duration + 0.01)).toBe(0)
  })

  it('never move with reduced motion, on LOW, when OFF, or after the guard', () => {
    expect(crowdMotionAllowed('normal', false, false)).toBe(true)
    expect(crowdMotionAllowed('normal', true, false)).toBe(false)
    expect(crowdMotionAllowed('low', false, false)).toBe(false)
    expect(crowdMotionAllowed('off', false, false)).toBe(false)
    expect(crowdMotionAllowed('normal', false, true)).toBe(false)
  })
})

// ---------------------------------------------------------------- evidence safety

const clip: ReplayClip = {
  key: 'after', label: 'AFTER SHIFT', startSecond: 3000, endSecond: 3010, duration: 10, scoreAtStart: { home: 1, away: 1 },
  events: [ev('PASS', 1), ev('GOAL', 4), { ...ev('PASS', 6), isEvidence: false, isContext: true }],
  tracks: [], ball: [], overlays: [],
  camera: { attackingTeamId: 'H', attackDirection: 1, focusPlayerId: 'H-2', centroidX: 50, centroidY: 50 },
}
const sequence: ReplaySequence = {
  matchId: 'M', insightId: 'I', insightType: 'ATTACK_CHANNEL_SHIFT', homeTeamId: 'H', awayTeamId: 'A',
  insightWindow: { fromSecond: 3000, toSecond: 3010 }, evidenceEventIds: ['EPASS1', 'EGOAL4'], contextEventIds: ['EPASS6'],
  relevantPlayerIds: [], players: [], clips: [clip], notice: '',
}

const deepFreeze = <T,>(o: T): T => { if (o && typeof o === 'object') { Object.values(o).forEach(deepFreeze); Object.freeze(o) } return o }

describe('the crowd never touches evidence', () => {
  it('leaves evidence IDs, context IDs, event flags, counts and the timeline unchanged', () => {
    const before = JSON.stringify(sequence)
    const timelineBefore = JSON.stringify(layout(sequence))
    const frozen = deepFreeze(structuredClone(sequence))
    for (let tt = 0; tt <= 10; tt += 0.5) crowdReaction(frozen.clips[0].events, tt)   // would throw if it tried to write
    for (const s of STANDS) layoutStand(s, 'normal', 3)
    expect(JSON.stringify(sequence)).toBe(before)
    expect(JSON.stringify(layout(sequence))).toBe(timelineBefore)
    expect(sequence.evidenceEventIds).toEqual(['EPASS1', 'EGOAL4'])
    expect(sequence.contextEventIds).toEqual(['EPASS6'])
    expect(clip.events.filter(e => e.isEvidence)).toHaveLength(2)
    expect(clip.events.filter(e => e.isContext)).toHaveLength(1)
  })

  it('spectators carry no event, evidence or player identity', () => {
    const p = layoutStand(SIDE, 'normal', 1)[0]
    expect(Object.keys(p).sort()).toEqual(['color', 'scale', 'x', 'y', 'z'])
  })

  it('the crowd module imports nothing from evidence, analytics, agents or the API', () => {
    const source = crowdSource
    expect(source.match(/^import .*$/gm) ?? []).toEqual([])
    expect(source).not.toMatch(/evidenceEventIds|contextEventIds|supportingEventIds|isEvidence|isContext/)
  })
})

describe('crowd localization', () => {
  it('has the toggle and the performance message in every supported language', () => {
    for (const lang of LANGUAGE_CODES) {
      const r = t(lang).replay
      expect(r.crowd.length).toBeGreaterThan(0)
      expect(r.crowdOn).not.toBe(r.crowdOff)
      expect(r.crowdAutoOff.length).toBeGreaterThan(10)
      if (lang !== 'en') expect(r.crowdAutoOff).not.toBe(t('en').replay.crowdAutoOff)
    }
    // Truthful: the message says the crowd was switched off for performance, nothing more.
    expect(t('en').replay.crowdAutoOff).toBe('Crowd disabled to maintain replay performance.')
  })
})
