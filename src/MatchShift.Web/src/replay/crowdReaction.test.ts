import crowdSource from './crowd.ts?raw'
import sceneSource from './scene.ts?raw'
import { describe, expect, it } from 'vitest'
import {
  CROWD_POSE, CrowdFrameGuard, LOW_REACTIONS, LOW_RISE_M, STAND_VARIATION, autoCrowdTier, crowdMotionAllowed, crowdReaction, crowdStepDown,
  layoutStand, resolveCrowdTier, standReaction,
} from './crowd'
import type { CrowdCapabilities, CrowdTier } from './crowd'
import type { ReplayEvent, ReplaySequence } from './types'
import { t } from '../i18n'
import { LANGUAGE_CODES } from '../languages'

// Crowd reactions in the Evidence Replay: visible on capable hardware, cheap or absent elsewhere, driven only by the
// replay clock, and never part of the evidence. Pure logic plus source checks; no WebGL, no network, no Azure.

const ev = (label: string, time: number, extra: Partial<ReplayEvent> = {}): ReplayEvent => ({
  eventId: `E-${label}-${time}`, sequence: 1, time, endTime: time, matchSecond: 2100, clock: '35:00', type: 'Shot', outcome: 'Goal',
  label, teamId: 'H', isEvidence: true, ...extra,
})
/** The 35' goal replay of the flagship match, as served by the API (clip-local seconds). */
const GOAL_CLIP = [ev('PASS', 4), ev('PASS_LOST', 6), ev('HIGH_PRESS_INTERCEPTION', 7), ev('PASS', 8), ev('CARRY', 10), ev('CUTBACK', 12), ev('GOAL', 14)]

const peak = (label: string) => {
  let best = 0
  for (let tt = 0; tt <= 4; tt += 0.01) best = Math.max(best, crowdReaction([ev(label, 1)], tt))
  return best
}
const activeFor = (label: string) => {
  let n = 0
  for (let tt = 0; tt <= 4; tt += 0.01) if (crowdReaction([ev(label, 1)], tt) > 0) n++
  return n * 0.01
}
/** Head-height change in metres at reaction k (body about 1 m tall): stretch + lift, from the shader pose. */
const headRise = (k: number) => k * (CROWD_POSE.stretch + CROWD_POSE.lift)

const capable: CrowdCapabilities = { webgl: true, webgl2: true, cores: 12, deviceMemory: 16, maxTextureSize: 16384, pixelRatio: 1, viewportPixels: 1920 * 1080, gpu: 'ANGLE (AMD Radeon RX 6600)' }

describe('reactions on capable hardware', () => {
  it('1: NORMAL reacts to a goal, visibly (about half a metre of head movement at the peak)', () => {
    expect(crowdMotionAllowed('normal', false, false)).toBe(true)
    expect(standReaction(GOAL_CLIP, 14.5, 0)).toBeGreaterThan(0.95)
    expect(headRise(peak('GOAL'))).toBeGreaterThanOrEqual(0.5)
    expect(CROWD_POSE.lift).toBeLessThanOrEqual(0.2)                    // standing up, not launching into the air
  })

  it('2, 3: goal > shot > turnover/recovery, with durations in the intended ranges', () => {
    const shots = ['SHOT_ON_TARGET', 'SHOT_OFF_TARGET', 'SHOT_BLOCKED'].map(peak)
    const small = ['INTERCEPTION', 'HIGH_PRESS_INTERCEPTION', 'HIGH_PRESSURE_RECOVERY', 'TURNOVER'].map(peak)
    expect(peak('GOAL')).toBeGreaterThan(Math.max(...shots))
    expect(Math.min(...shots)).toBeGreaterThan(Math.max(...small))
    expect(activeFor('GOAL')).toBeGreaterThanOrEqual(1.5); expect(activeFor('GOAL')).toBeLessThanOrEqual(2.5)
    for (const l of ['SHOT_ON_TARGET', 'SHOT_OFF_TARGET', 'SHOT_BLOCKED']) { expect(activeFor(l)).toBeGreaterThanOrEqual(0.8); expect(activeFor(l)).toBeLessThanOrEqual(1.3) }
    for (const l of ['INTERCEPTION', 'HIGH_PRESS_INTERCEPTION', 'HIGH_PRESSURE_RECOVERY', 'TURNOVER']) { expect(activeFor(l)).toBeGreaterThanOrEqual(0.4); expect(activeFor(l)).toBeLessThanOrEqual(0.8) }
  })

  it('4: routine passes, carries and movement produce no reaction', () => {
    for (const label of ['PASS', 'PROGRESSIVE_PASS', 'CARRY', 'CROSS', 'CUTBACK', 'LONG_BALL', 'KICK_OFF', 'CLEARANCE', 'CORNER', 'PASS_LOST'])
      expect(peak(label)).toBe(0)
    for (let tt = 0; tt < 6.99; tt += 0.05) expect(crowdReaction(GOAL_CLIP, tt)).toBe(0)       // the passes before the interception
  })

  it('the four stands differ slightly and deterministically, never randomly', () => {
    const at = [0, 1, 2, 3].map(i => standReaction(GOAL_CLIP, 14.6, i))
    expect(new Set(at.map(v => v.toFixed(3))).size).toBeGreaterThan(1)
    expect(at.every(v => v > 0.8)).toBe(true)
    expect([0, 1, 2, 3].map(i => standReaction(GOAL_CLIP, 14.6, i))).toEqual(at)
    expect(STAND_VARIATION.every(v => v.strength >= 0.8 && v.strength <= 1 && v.delay <= 0.15)).toBe(true)
    expect(crowdSource).not.toMatch(/Math\.random/)
  })
})

describe('tiers, reduced motion and OFF', () => {
  it('7: reduced motion keeps every tier seated; the scene skips the calculation', () => {
    expect(crowdMotionAllowed('normal', true, false)).toBe(false)
    expect(crowdMotionAllowed('low', true, false)).toBe(false)
    expect(sceneSource).toContain('const moving = crowdMotionAllowed(this.crowdTier, this.options.reducedMotion, this.crowdGuardTripped)')
    expect(sceneSource).toContain('const k = moving ? standReaction(this.reactionEvents, this.reactionLocal, i, this.crowdTier) : 0')
  })

  it('6: OFF has no spectators, no crowd draw calls and no crowd work', () => {
    expect(layoutStand({ length: 125, depth: 18, height: 9 }, 'off', 1)).toEqual([])
    expect(crowdMotionAllowed('off', false, false)).toBe(false)
    expect(sceneSource).toContain("if (this.crowdMeshes.length === 0) return   // OFF: no spectators and no crowd work at all")
    expect(sceneSource).toContain("if (tier === 'off') return")
  })

  it('stays within the draw-call and geometry budget: one instanced mesh per stand, the reaction is one uniform', () => {
    expect(sceneSource).toContain('new THREE.InstancedMesh(geometry, material, seats.length)')
    expect(sceneSource).toContain('shader.uniforms.uCrowdReaction = reaction')
    expect(sceneSource).not.toMatch(/crowdMeshes[\s\S]{0,200}setMatrixAt[\s\S]{0,80}updateCrowd/)   // no per-seat matrix updates per frame
    expect(sceneSource).toContain('mesh.castShadow = false')
  })
})

describe('synchronised with the replay clock', () => {
  it('8: seeking away from an active reaction ends it at once; seeking back before the event shows nothing', () => {
    expect(crowdReaction(GOAL_CLIP, 14.6)).toBeGreaterThan(0.9)
    expect(crowdReaction(GOAL_CLIP, 18.5)).toBe(0)                     // seek forward past it
    expect(crowdReaction(GOAL_CLIP, 3)).toBe(0)                        // seek back
    expect(crowdReaction(GOAL_CLIP, 14.6)).toBeGreaterThan(0.9)        // pause: the same time gives the same pose
  })

  it('9: a restart lets the same event react again when the timeline reaches it', () => {
    const run = () => { let n = 0; for (let tt = 0; tt <= 19; tt += 0.1) if (crowdReaction(GOAL_CLIP, tt) > 0.9) n++; return n }
    const first = run()
    expect(first).toBeGreaterThan(0)
    expect(run()).toBe(first)
  })
})

describe('hardware policy and the frame-time guard', () => {
  it('10: a capability failure selects OFF', () => {
    expect(autoCrowdTier({ webgl: false })).toBe('off')
    expect(autoCrowdTier({ ...capable, webgl2: false })).toBe('off')
    expect(autoCrowdTier({ ...capable, gpu: 'llvmpipe (LLVM 15.0.7, 256 bits)' })).toBe('off')
    expect(resolveCrowdTier('normal', 'on', { webgl: false }, false)).toBe('off')
  })

  it('11: constrained or unknown hardware never gets NORMAL automatically; turning the crowd on does not force it', () => {
    for (const c of [{ webgl: true, webgl2: true }, { ...capable, deviceMemory: undefined }, { ...capable, gpu: undefined }, { ...capable, cores: 4 }] as CrowdCapabilities[]) {
      const auto = autoCrowdTier(c)
      expect(auto).not.toBe('normal')
      expect(resolveCrowdTier(auto, 'on', c, false)).not.toBe('normal')
    }
  })

  it('12: clearly capable hardware may select NORMAL', () => {
    expect(autoCrowdTier(capable)).toBe('normal')
    expect(resolveCrowdTier('normal', null, capable, false)).toBe('normal')
  })

  it('13: sustained slow frames step the crowd down once per check - NORMAL → LOW → OFF, never back up', () => {
    const slow = () => { const g = new CrowdFrameGuard(); let n = 0; for (let s = 0; s < 6; s += 0.05) if (g.feed(0.05)) n++; return n }
    expect(slow()).toBe(1)
    let tier: CrowdTier = 'normal'
    tier = crowdStepDown(tier); expect(tier).toBe('low')
    tier = crowdStepDown(tier); expect(tier).toBe('off')
    expect(crowdStepDown('off')).toBe('off')
    expect(sceneSource).toContain('const next = crowdStepDown(this.crowdTier)')
  })
})

describe('the crowd is never evidence and never audio', () => {
  const sequence: ReplaySequence = {
    matchId: 'MS-FLAGSHIP-001', insightId: 'MS-FLAGSHIP-001-I006', insightType: 'GOAL', homeTeamId: 'H', awayTeamId: 'A',
    insightWindow: { fromSecond: 2080, toSecond: 2100 }, evidenceEventIds: GOAL_CLIP.map(e => e.eventId), contextEventIds: ['E-CTX'],
    relevantPlayerIds: [], players: [], notice: '',
    clips: [{ key: 'key', label: 'KEY', startSecond: 2086, endSecond: 2105, duration: 19, scoreAtStart: { home: 0, away: 0 },
      events: [...GOAL_CLIP, ev('PASS', 2, { eventId: 'E-CTX', isEvidence: false, isContext: true })], tracks: [], ball: [], overlays: [],
      camera: { attackingTeamId: 'H', attackDirection: 1, focusPlayerId: 'H-9', centroidX: 60, centroidY: 40 } }],
  }
  const freeze = <T,>(o: T): T => { if (o && typeof o === 'object') { Object.values(o).forEach(freeze); Object.freeze(o) } return o }

  it('14, 15, 16: reactions leave evidence IDs, context IDs and counts unchanged', () => {
    const before = JSON.stringify(sequence)
    const frozen = freeze(structuredClone(sequence))
    for (let tt = 0; tt <= 19; tt += 0.05) for (let i = 0; i < 4; i++) standReaction(frozen.clips[0].events, tt, i)   // throws if it writes
    expect(JSON.stringify(sequence)).toBe(before)
    expect(sequence.evidenceEventIds).toHaveLength(7)
    expect(sequence.contextEventIds).toEqual(['E-CTX'])
    expect(sequence.clips[0].events.filter(e => e.isEvidence)).toHaveLength(7)
    expect(sequence.clips[0].events.filter(e => e.isContext)).toHaveLength(1)
  })

  it('17: crowd code never touches narration, speech or audio focus', () => {
    expect(crowdSource.match(/^import .*$/gm) ?? []).toEqual([])
    for (const src of [crowdSource, sceneSource]) expect(src).not.toMatch(/speechSynthesis|SpeechQueue|AudioContext|new Audio\(|acquireReplayFocus|\.say\(/)
  })

  it('18: the crowd toggle and both performance notices exist in all six languages', () => {
    for (const lang of LANGUAGE_CODES) {
      const r = t(lang).replay
      expect(r.crowd.length).toBeGreaterThan(0)
      expect(r.crowdAutoLow.length).toBeGreaterThan(10)
      expect(r.crowdAutoLow).not.toBe(r.crowdAutoOff)
      if (lang !== 'en') expect(r.crowdAutoLow).not.toBe(t('en').replay.crowdAutoLow)
    }
    expect(t('en').replay.crowdAutoLow).toBe('Crowd reduced to maintain replay performance.')
  })
})

// ---------------------------------------------------------------- LOW: lightweight reactions

describe('LOW: a lightweight reactive crowd', () => {
  const lowPeak = (label: string) => {
    let best = 0
    for (let tt = 0; tt <= 4; tt += 0.01) best = Math.max(best, standReaction([ev(label, 1)], tt, 0, 'low'))
    return best
  }

  it('LOW-1: LOW reacts to a goal, visibly but restrained (a whole-stand rise of under half a metre)', () => {
    expect(crowdMotionAllowed('low', false, false)).toBe(true)
    expect(standReaction(GOAL_CLIP, 14.6, 0, 'low')).toBeGreaterThan(0.95)
    expect(LOW_RISE_M * lowPeak('GOAL')).toBeGreaterThanOrEqual(0.4)
    expect(LOW_RISE_M).toBeLessThanOrEqual(0.5)
  })

  it('LOW-2: LOW is weaker than NORMAL - smaller movement, fewer kinds of event, no pose change', () => {
    expect(LOW_RISE_M * lowPeak('GOAL')).toBeLessThan(headRise(peak('GOAL')))
    for (const l of ['SHOT_ON_TARGET', 'SHOT_OFF_TARGET', 'SHOT_BLOCKED']) {
      expect(lowPeak(l)).toBeLessThan(peak(l))                                 // a small reaction to a shot
      expect(lowPeak(l)).toBeLessThan(lowPeak('GOAL') / 2)
      expect(lowPeak(l)).toBeGreaterThan(0)
    }
    for (const l of ['INTERCEPTION', 'HIGH_PRESS_INTERCEPTION', 'HIGH_PRESSURE_RECOVERY', 'TURNOVER']) expect(lowPeak(l)).toBe(0)
    expect(Object.keys(LOW_REACTIONS).every(k => k in CROWD_REACTIONS_KEYS)).toBe(true)  // same triggers, a subset
  })

  it('LOW-3: routine play does not move LOW', () => {
    for (const l of ['PASS', 'PROGRESSIVE_PASS', 'CARRY', 'CROSS', 'CUTBACK', 'PASS_LOST', 'KICK_OFF']) expect(lowPeak(l)).toBe(0)
    for (let tt = 0; tt < 13.99; tt += 0.05) expect(standReaction(GOAL_CLIP, tt, 0, 'low')).toBe(0)
  })

  it('LOW-4: reduced motion makes LOW completely static', () => {
    expect(crowdMotionAllowed('low', true, false)).toBe(false)
    expect(sceneSource).toContain("if (this.crowdTier === 'low') this.crowdMeshes[i].position.y = LOW_RISE_M * k")   // k is 0 when not moving
  })

  it('LOW-5: OFF stays empty and static', () => {
    expect(layoutStand({ length: 125, depth: 18, height: 9 }, 'off', 1)).toEqual([])
    expect(crowdMotionAllowed('off', false, false)).toBe(false)
    expect(standReaction(GOAL_CLIP, 14.6, 0, 'off')).toBe(0)
  })

  it('LOW-6: the guard can still step LOW → OFF, and OFF wins', () => {
    const g = new CrowdFrameGuard()
    let tripped = 0
    for (let s = 0; s < 6; s += 0.06) if (g.feed(0.06)) tripped++
    expect(tripped).toBe(1)
    expect(crowdStepDown('low')).toBe('off')
    expect(crowdStepDown('off')).toBe('off')
  })

  it('LOW-7: LOW reactions leave evidence IDs, context IDs, counts and narration untouched', () => {
    const events = Object.freeze(GOAL_CLIP.map(e => Object.freeze({ ...e })))
    const before = JSON.stringify(events)
    for (let tt = 0; tt <= 19; tt += 0.05) for (let i = 0; i < 4; i++) standReaction(events, tt, i, 'low')
    expect(JSON.stringify(events)).toBe(before)
    expect(events.filter(e => e.isEvidence)).toHaveLength(7)
    expect(crowdSource).not.toMatch(/evidenceEventIds|contextEventIds|supportingEventIds|speechSynthesis|SpeechQueue|\.say\(/)
  })

  it('LOW-8: LOW reactions add no draw calls, geometry or shader: the same mesh per stand, moved as one block', () => {
    // Geometry and instancing are chosen before (and independently of) the reaction; LOW keeps its box and a plain material.
    expect(sceneSource).toContain("const material = tier === 'normal' ? crowdMaterial(reaction) : new THREE.MeshLambertMaterial({ color: '#ffffff' })")
    expect(sceneSource).toContain('const geometry = spectatorGeometry(tier)')
    expect(sceneSource).toMatch(/if \(tier === 'low'\) \{\s*const box = new THREE\.BoxGeometry\(0\.42, 1\.0, 0\.36\)/)
    expect(sceneSource.match(/new THREE\.InstancedMesh\(/g)).toHaveLength(1)                                 // one per stand, no extras
    expect(layoutStand({ length: 125, depth: 18, height: 9 }, 'low', 7001).length).toBeLessThan(layoutStand({ length: 125, depth: 18, height: 9 }, 'normal', 7001).length)
  })
})

const CROWD_REACTIONS_KEYS = Object.fromEntries(['GOAL', 'SHOT_ON_TARGET', 'SHOT_OFF_TARGET', 'SHOT_BLOCKED', 'HIGH_PRESS_INTERCEPTION',
  'HIGH_PRESSURE_RECOVERY', 'INTERCEPTION', 'TURNOVER'].map(k => [k, true]))
