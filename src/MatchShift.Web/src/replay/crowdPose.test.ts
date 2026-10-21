import crowdSource from './crowd.ts?raw'
import geometrySource from './crowdGeometry.ts?raw'
import sceneSource from './scene.ts?raw'
import { describe, expect, it } from 'vitest'
import {
  COHORTS, CROWD_REACTIONS, HOLD_DROP, LOW_REACTIONS, cheeringCount, cheeringSlots, cohortCheering, cohortTiming, crowdMotionAllowed,
  crowdStepDown, layoutStand, spectatorCheering, spectatorReaction, standEvent, variationOf,
} from './crowd'
import type { CrowdTier } from './crowd'
import { cheeringSpectator, lowSpectator, neutralSpectator, triangles } from './crowdGeometry'
import type { ReplayEvent } from './types'

// The crowd reads as people, and a goal is a short deterministic wave of different responses - some cheer at once, some
// follow, some only stand, some stay seated, and they settle at different times - never one block moving like a lift.
// Geometry is built with three.js in Node (no WebGL); reactions use the same pure functions the vertex shader mirrors.

/** The body of one function or method in a source file (up to its closing brace at the same indent). */
const body = (raw: string, start: string, indent = '  ') => {
  const src = raw.replace(/\r\n/g, '\n')
  const i = src.indexOf(start)
  return src.slice(i, src.indexOf(`\n${indent}}\n`, i) + indent.length + 2)
}

const ev = (label: string, time: number): ReplayEvent => ({
  eventId: `E-${label}-${time}`, sequence: 1, time, endTime: time, matchSecond: 2100, clock: '35:00', type: 'Shot', outcome: 'Goal',
  label, teamId: 'H', isEvidence: true,
})
/** The 35' goal replay of the flagship match (clip-local seconds): build-up, a high-press interception, then the goal. */
const GOAL_CLIP = [ev('PASS', 4), ev('PASS_LOST', 6), ev('HIGH_PRESS_INTERCEPTION', 7), ev('PASS', 8), ev('CARRY', 10), ev('CUTBACK', 12), ev('GOAL', 14)]
const SIDE = { length: 125, depth: 18, height: 9 }

/** What one stand looks like at clip-local time t, from its real seats: who cheers, stands or stays seated, which cohorts move. */
function standState(events: readonly ReplayEvent[], t: number, stand = 0, tier: CrowdTier = 'normal') {
  const seats = layoutStand(SIDE, tier, 7001 + stand)
  const e = standEvent(events, t, stand, tier)
  let cheering = 0, standing = 0, reacting = 0, total = 0
  const moving = new Set<number>()
  for (const seat of seats) {
    if (!e) continue
    const k = spectatorReaction(seat, e.reaction, e.age, stand)
    const cheers = tier === 'normal' && spectatorCheering(seat, e.reaction, e.age, stand)
    if (k > 0) moving.add(seat.slot)
    if (cheers) cheering++
    else if (k > 0.5) standing++
    if (cheers || k > 0.1) reacting++
    total += k
  }
  return { n: seats.length, cheering, standing, reacting, total: Math.round(total * 1000) / 1000, neutral: seats.length - cheering - standing, cohorts: moving.size, label: e?.label ?? null }
}
const goalAt = (offset: number, stand = 0) => standState(GOAL_CLIP, 14 + offset, stand)

describe('spectators read as people', () => {
  const partBounds = (g: ReturnType<typeof neutralSpectator>, head: boolean) => {
    const pos = g.attributes.position, flag = g.attributes.aHead
    let minY = Infinity, maxY = -Infinity, maxZ = 0, n = 0
    for (let i = 0; i < pos.count; i++) {
      if ((flag.getX(i) === 1) !== head) continue
      n++; minY = Math.min(minY, pos.getY(i)); maxY = Math.max(maxY, pos.getY(i)); maxZ = Math.max(maxZ, Math.abs(pos.getZ(i)))
    }
    return { n, minY, maxY, maxZ }
  }

  it('a head above a torso wider at the shoulders, on NORMAL and LOW - not a rectangular peg', () => {
    for (const g of [neutralSpectator(), lowSpectator()]) {
      const head = partBounds(g, true), torsoBounds = partBounds(g, false)
      expect(head.n).toBeGreaterThan(0)
      expect(head.minY).toBeGreaterThan(torsoBounds.maxY - 0.05)
      expect(head.maxZ).toBeLessThan(torsoBounds.maxZ)
    }
    expect(triangles(neutralSpectator())).toBe(32)
    expect(triangles(lowSpectator())).toBe(24)
    expect(body(geometrySource, 'function head()', '')).toContain('IcosahedronGeometry')
  })

  it('the cheering pose raises both arms above the head', () => {
    const n = neutralSpectator(), c = cheeringSpectator()
    n.computeBoundingBox(); c.computeBoundingBox()
    expect(c.boundingBox!.max.y - n.boundingBox!.max.y).toBeGreaterThan(0.25)
    expect(triangles(c)).toBe(44)
  })
})

describe('a goal is a wave, not a lift', () => {
  it('C1: cohorts start at different times - the last one about 0.35 s after the first, plus up to 0.23 s between stands', () => {
    const goal = CROWD_REACTIONS.GOAL
    const onsets = Array.from({ length: goal.cohorts }, (_, s) => cohortTiming(goal, s)!.onset)
    expect(new Set(onsets).size).toBe(goal.cohorts)
    expect(onsets[0]).toBe(0)
    expect(onsets.at(-1)).toBeCloseTo(0.35, 5)
    expect([0, 1, 2, 3].map(i => variationOf(i).delay)).toEqual([0, 0.08, 0.15, 0.23])
    expect(goalAt(0.02).cohorts).toBeLessThan(goal.cohorts)                    // not everyone at once
  })

  it('C2: early in the goal some cohorts already react while others are still seated', () => {
    const early = goalAt(0.15)
    expect(early.cohorts).toBeGreaterThan(1)
    expect(early.cohorts).toBeLessThan(COHORTS.normal)
    const slots = layoutStand(SIDE, 'normal', 7001)
    const e = standEvent(GOAL_CLIP, 14.15, 0, 'normal')!
    expect(slots.some(x => spectatorReaction(x, e.reaction, e.age, 0) === 0)).toBe(true)
    expect(slots.some(x => spectatorReaction(x, e.reaction, e.age, 0) > 0.3)).toBe(true)
  })

  it('C3: at the peak, cheering, standing and seated spectators coexist (about half cheer, 10-25% stand)', () => {
    const peak = goalAt(0.6)
    expect(peak.cheering / peak.n).toBeGreaterThanOrEqual(0.4); expect(peak.cheering / peak.n).toBeLessThanOrEqual(0.65)
    expect(peak.standing / peak.n).toBeGreaterThanOrEqual(0.1); expect(peak.standing / peak.n).toBeLessThanOrEqual(0.25)
    expect(peak.neutral).toBeGreaterThan(0)
    for (const stand of [0, 1, 2, 3]) expect(goalAt(0.6, stand).cheering).toBeGreaterThan(0)
  })

  it('C4, C5: cohort membership and timing are seeded - re-running the replay gives exactly the same reaction', () => {
    expect(layoutStand(SIDE, 'normal', 7001).map(x => x.slot)).toEqual(layoutStand(SIDE, 'normal', 7001).map(x => x.slot))
    const run = () => [0, 0.05, 0.1, 0.2, 0.35, 0.6, 1.2, 1.8, 2.4].map(o => goalAt(o))
    expect(run()).toEqual(run())
    expect(crowdSource + body(sceneSource, '  setCrowd(') + body(sceneSource, 'private updateCrowd(')).not.toMatch(/Math\.random/)
    const slots = layoutStand(SIDE, 'normal', 7001).map(x => x.slot)
    expect(new Set(slots).size).toBe(COHORTS.normal)                            // every cohort is used
    expect(Math.min(...Array.from({ length: COHORTS.normal }, (_, s) => slots.filter(x => x === s).length))).toBeGreaterThan(slots.length / 30)
  })

  it('C9: the crowd settles in several phases, not one drop - and is back to neutral by about 2.5 s', () => {
    const goal = CROWD_REACTIONS.GOAL
    const ends = Array.from({ length: goal.cohorts }, (_, s) => { const x = cohortTiming(goal, s)!; return x.onset + x.duration })
    expect(new Set(ends.map(e => e.toFixed(3))).size).toBe(goal.cohorts)
    expect(Math.max(...ends) - Math.min(...ends)).toBeGreaterThan(0.4)
    const settling = [1.0, 1.3, 1.6, 1.9].map(o => goalAt(o).total)
    for (let i = 1; i < settling.length; i++) expect(settling[i]).toBeLessThan(settling[i - 1])
    expect(settling.at(-1)).toBeGreaterThan(0)                                        // some still settling at 1.9 s
    const arms = [1.2, 1.4, 1.55].map(o => goalAt(o).cheering)                        // arms come down group by group
    expect(arms[0]).toBeGreaterThan(arms[1]); expect(arms[1]).toBeGreaterThan(arms[2]); expect(arms[2]).toBeGreaterThan(0)
    expect(goalAt(1.0).cheering).toBeGreaterThan(goalAt(1.5).cheering)                 // arms come down in waves too
    expect(goalAt(1.5).cheering).toBeGreaterThan(0)
    for (const stand of [0, 1, 2, 3]) expect(goalAt(2.5, stand).reacting).toBe(0)
  })

  it('no second pulse: each spectator rises and settles once (no pogo jumping)', () => {
    const seat = layoutStand(SIDE, 'normal', 7001).find(x => x.rank < 0.3)!
    const e0 = standEvent(GOAL_CLIP, 14, 0, 'normal')!
    let rises = 0, prev = 0
    for (let t = 0; t <= 3; t += 0.02) { const k = spectatorReaction(seat, e0.reaction, t, 0); if (k > 0.5 && prev <= 0.5) rises++; prev = k }
    expect(rises).toBe(1)
  })
})

describe('other events', () => {
  const peakOf = (label: string) => {
    const clip = [ev(label, 1)]
    let best = { cheering: 0, standing: 0, cohorts: 0 }
    for (let t = 1; t <= 4; t += 0.05) {
      const s = standState(clip, t)
      if (s.cheering + s.standing > best.cheering + best.standing) best = { cheering: s.cheering, standing: s.standing, cohorts: best.cohorts }
      best.cohorts = Math.max(best.cohorts, s.cohorts)
    }
    return best
  }

  it('C6, C7: a goal involves more cohorts and far more cheering than a shot; a shot more than a turnover', () => {
    const goal = peakOf('GOAL'), shot = peakOf('SHOT_ON_TARGET'), turnover = peakOf('TURNOVER')
    expect(CROWD_REACTIONS.GOAL.cohorts).toBeGreaterThan(CROWD_REACTIONS.SHOT_ON_TARGET.cohorts)
    expect(goal.cohorts).toBeGreaterThan(shot.cohorts)
    expect(goal.cheering).toBeGreaterThan(2 * shot.cheering)
    expect(shot.cheering).toBeGreaterThan(0)
    expect(shot.cheering + shot.standing).toBeGreaterThan(turnover.cheering + turnover.standing)
    expect(turnover.cheering).toBe(0)
    for (const r of [CROWD_REACTIONS.SHOT_ON_TARGET, CROWD_REACTIONS.SHOT_OFF_TARGET, CROWD_REACTIONS.SHOT_BLOCKED]) {
      expect(r.duration).toBeGreaterThanOrEqual(0.8); expect(r.duration).toBeLessThanOrEqual(1.3)
    }
    for (const l of ['INTERCEPTION', 'HIGH_PRESS_INTERCEPTION', 'HIGH_PRESSURE_RECOVERY', 'TURNOVER']) {
      expect(CROWD_REACTIONS[l].cheer).toBe(false)
      expect(CROWD_REACTIONS[l].duration).toBeLessThanOrEqual(0.8)
      expect(CROWD_REACTIONS[l].cohorts).toBeLessThanOrEqual(2)
    }
  })

  it('C8: routine passes and carries activate no cohort', () => {
    for (const l of ['PASS', 'PROGRESSIVE_PASS', 'CARRY', 'CROSS', 'CUTBACK', 'PASS_LOST', 'KICK_OFF']) {
      expect(standEvent([ev(l, 1)], 1.3, 0, 'normal')).toBeNull()
      expect(peakOf(l)).toEqual({ cheering: 0, standing: 0, cohorts: 0 })
    }
    for (let t = 0; t < 7; t += 0.1) expect(standState(GOAL_CLIP, t).cohorts).toBe(0)       // the build-up before the interception
  })

  it('the scene can show cheering with one count: cheering slots are always 0..m-1, for every reaction and moment', () => {
    for (const r of Object.values(CROWD_REACTIONS)) for (let age = 0; age <= 3; age += 0.01) {
      const m = cheeringSlots(r, age)
      for (let s = 0; s < r.cohorts; s++) expect(cohortCheering(r, s, age)).toBe(s < m)
    }
    const seats = layoutStand(SIDE, 'normal', 7001)
    const e = standEvent(GOAL_CLIP, 14.6, 0, 'normal')!
    expect(cheeringCount(seats, cheeringSlots(e.reaction, e.age), 0)).toBe(seats.filter(x => spectatorCheering(x, e.reaction, e.age, 0)).length)
  })
})

describe('tiers, motion, replay clock and safety', () => {
  it('C10: LOW uses fewer cohorts, no cheering pose and a simple rise', () => {
    expect(COHORTS.low).toBeLessThan(COHORTS.normal)
    expect(LOW_REACTIONS.GOAL.cohorts).toBe(COHORTS.low)
    expect(Object.values(LOW_REACTIONS).every(r => !r.cheer)).toBe(true)
    const low = standState(GOAL_CLIP, 14.6, 0, 'low')
    expect(low.cheering).toBe(0)
    expect(low.standing).toBeGreaterThan(0)
    expect(standState(GOAL_CLIP, 14.05, 0, 'low').cohorts).toBeLessThan(COHORTS.low)     // staggered, not one block
    expect(sceneSource).toContain("const cheeringBase = tier === 'normal' ? cheeringSpectator() : null")
  })

  it('C11: reduced motion means no event reaches the shader - no cohort moves, nobody cheers', () => {
    expect(crowdMotionAllowed('normal', true, false)).toBe(false)
    expect(crowdMotionAllowed('low', true, false)).toBe(false)
    const update = body(sceneSource, 'private updateCrowd(')
    expect(update).toContain('const e = moving ? standEvent(this.reactionEvents, this.reactionLocal, i, this.crowdTier) : null')
    expect(update).toContain('u.uCohorts.value = e ? e.reaction.cohorts : 0')
    expect(update).toContain('const cheering = c.cheering && e ?')
  })

  it('C12, C13: state comes only from replay time - seeking gives the right state at once; a restart replays it exactly', () => {
    const fresh = goalAt(0.6)
    goalAt(4); goalAt(-10)                                           // jump around first
    expect(goalAt(0.6)).toEqual(fresh)
    expect(standState(GOAL_CLIP, 18, 0).cohorts).toBe(0)            // seeking past the reaction clears it
    expect(standState(GOAL_CLIP, 2, 0).cohorts).toBe(0)             // seeking back before it too
    expect(sceneSource).not.toMatch(/setTimeout|setInterval/)
  })

  it('C14: bounded draw calls - at most two instanced meshes per stand, no per-spectator work or allocation per frame', () => {
    expect(sceneSource.match(/new THREE\.InstancedMesh\(/g)).toHaveLength(1)
    const update = body(sceneSource, 'private updateCrowd(')
    expect(update).not.toMatch(/new |setMatrixAt|setColorAt|\.clone\(|\.filter\(|\.map\(/)
    expect(update).toContain('c.cheering.count = cheering; c.cheering.visible = cheering > 0')
    const shader = sceneSource.slice(sceneSource.indexOf('function crowdMaterial('), sceneSource.indexOf('customProgramCacheKey'))
    expect(shader).toContain('${HOLD_DROP.toFixed(3)} * aSlot / crowdLast')                    // same timing as cohortTiming
    expect(HOLD_DROP).toBeGreaterThan(0)
    expect(shader).toContain('float rise = min(0.25, d * 0.25);')                             // same envelope as crowd.ts
    expect(shader).toContain('aHead);')                                                        // skin-toned heads and arms
    expect(shader).not.toMatch(/emissive|map:|TextureLoader/)
  })

  it('C15: reactions leave evidence IDs, flags and counts untouched', () => {
    const events = Object.freeze(GOAL_CLIP.map(e => Object.freeze({ ...e })))
    const before = JSON.stringify(events)
    for (let t = 0; t <= 19; t += 0.25) for (let i = 0; i < 4; i++) standState(events, t, i)
    expect(JSON.stringify(events)).toBe(before)
    expect(events.filter(e => e.isEvidence)).toHaveLength(7)
    expect(crowdSource + geometrySource).not.toMatch(/evidenceEventIds|contextEventIds|supportingEventIds|isContext/)
  })

  it('C16: the crowd makes no sound', () => {
    for (const src of [crowdSource, geometrySource, sceneSource]) expect(src).not.toMatch(/AudioContext|new Audio\(|speechSynthesis|SpeechQueue|\.say\(/)
  })

  it('the guard can still step NORMAL → LOW → OFF', () => {
    expect(crowdStepDown('normal')).toBe('low')
    expect(crowdStepDown('low')).toBe('off')
  })
})
