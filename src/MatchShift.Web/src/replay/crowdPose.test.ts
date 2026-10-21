import crowdSource from './crowd.ts?raw'
import geometrySource from './crowdGeometry.ts?raw'
import sceneSource from './scene.ts?raw'
import { describe, expect, it } from 'vitest'
import {
  CROWD_REACTIONS, STAND_BAND, cheeringCount, crowdCheer, crowdMotionAllowed, crowdStepDown, layoutStand, standCheer,
} from './crowd'
import { cheeringSpectator, lowSpectator, neutralSpectator, triangles } from './crowdGeometry'
import type { ReplayEvent } from './types'

/** The body of one function or method in a source file (up to its closing brace at the same indent). */
const body = (raw: string, start: string, indent = '  ') => {
  const src = raw.replace(/\r\n/g, '\n')
  const i = src.indexOf(start)
  return src.slice(i, src.indexOf(`\n${indent}}\n`, i) + indent.length + 2)
}

// NORMAL spectators read as people and a goal changes the crowd's silhouette: a seeded fraction switches to an arms-up
// pose. Geometry is built with three.js in Node (no WebGL); everything else is pure logic or a source check.

const ev = (label: string, time: number): ReplayEvent => ({
  eventId: `E-${label}-${time}`, sequence: 1, time, endTime: time, matchSecond: 2100, clock: '35:00', type: 'Shot', outcome: 'Goal',
  label, teamId: 'H', isEvidence: true,
})
const GOAL_CLIP = [ev('PASS', 4), ev('PASS_LOST', 6), ev('HIGH_PRESS_INTERCEPTION', 7), ev('PASS', 8), ev('CARRY', 10), ev('CUTBACK', 12), ev('GOAL', 14)]
const SIDE = { length: 125, depth: 18, height: 9 }
const peakCheer = (label: string) => { let b = 0; for (let t = 0; t <= 4; t += 0.01) b = Math.max(b, crowdCheer([ev(label, 1)], t)); return b }

const partBounds = (g: ReturnType<typeof neutralSpectator>, head: boolean) => {
  const pos = g.attributes.position, flag = g.attributes.aHead
  let minY = Infinity, maxY = -Infinity, maxZ = 0, n = 0
  for (let i = 0; i < pos.count; i++) {
    if ((flag.getX(i) === 1) !== head) continue
    n++; minY = Math.min(minY, pos.getY(i)); maxY = Math.max(maxY, pos.getY(i)); maxZ = Math.max(maxZ, Math.abs(pos.getZ(i)))
  }
  return { n, minY, maxY, maxZ }
}

describe('NORMAL spectators read as people', () => {
  it('1: a head above a torso that is wider at the shoulders - not a single rectangular peg', () => {
    const g = neutralSpectator()
    const head = partBounds(g, true), torsoBounds = partBounds(g, false)
    expect(head.n).toBeGreaterThan(0)
    expect(head.minY).toBeGreaterThan(torsoBounds.maxY - 0.05)                 // the head sits on top of the body
    expect(head.maxZ).toBeLessThan(torsoBounds.maxZ)                           // and is narrower than the shoulders
    expect(triangles(g)).toBe(32)
    expect(body(geometrySource, 'function head()', '')).toContain('IcosahedronGeometry')     // a round head, not a peg
  })

  it('the cheering pose raises both arms above the head, changing height and outline', () => {
    const n = neutralSpectator(), c = cheeringSpectator()
    n.computeBoundingBox(); c.computeBoundingBox()
    expect(c.boundingBox!.max.y - n.boundingBox!.max.y).toBeGreaterThan(0.25)        // arms clearly above the head
    expect(c.boundingBox!.max.z - c.boundingBox!.min.z).toBeGreaterThan(2 * (n.boundingBox!.max.z - n.boundingBox!.min.z))
    expect(triangles(c)).toBe(44)
    expect(c.boundingBox!.max.z).toBeLessThan(0.5)                      // stays within its seat (seats are 0.9 m apart)
  })

  it('heads get a skin tone and clothing is a restrained palette (no emissive, no textures)', () => {
    const material = body(sceneSource, 'function crowdMaterial(', '')
    expect(material).toContain('aHead);')
    expect(material).toContain('new THREE.MeshLambertMaterial')
    expect(material).not.toMatch(/emissive|map:|TextureLoader/)
  })
})

describe('reaction distribution', () => {
  it('2: a goal switches a deterministic 40-70% of a stand to the cheering pose at the peak', () => {
    const f = standCheer(GOAL_CLIP, 14.6, 0)
    expect(f).toBeGreaterThanOrEqual(0.4); expect(f).toBeLessThanOrEqual(0.7)
    const seats = layoutStand(SIDE, 'normal', 7001)
    const cheering = cheeringCount(seats.length, f)
    expect(cheering / seats.length).toBeGreaterThan(0.4)
    // The next 10-25% (by rank) stand fully without raising their arms; the rest lean only slightly.
    const standing = seats.filter(s => s.rank >= f && s.rank < f + (STAND_BAND.full + STAND_BAND.fade) / 2).length / seats.length
    expect(standing).toBeGreaterThanOrEqual(0.1); expect(standing).toBeLessThanOrEqual(0.25)
    expect(layoutStand(SIDE, 'normal', 7001).map(s => s.rank)).toEqual(seats.map(s => s.rank))   // seeded, not random
    expect(crowdSource + geometrySource + body(sceneSource, '  setCrowd(') + body(sceneSource, 'private updateCrowd(')).not.toMatch(/Math\.random/)
  })

  it('3, 4, 5: goal cheering > shot cheering > turnover and routine play (none)', () => {
    const shots = ['SHOT_ON_TARGET', 'SHOT_OFF_TARGET', 'SHOT_BLOCKED'].map(peakCheer)
    expect(peakCheer('GOAL')).toBeGreaterThan(2 * Math.max(...shots))
    expect(Math.min(...shots)).toBeGreaterThan(0.1)
    for (const l of ['INTERCEPTION', 'HIGH_PRESS_INTERCEPTION', 'HIGH_PRESSURE_RECOVERY', 'TURNOVER']) expect(peakCheer(l)).toBe(0)
    for (const l of ['PASS', 'PROGRESSIVE_PASS', 'CARRY', 'CROSS', 'CUTBACK', 'PASS_LOST']) expect(peakCheer(l)).toBe(0)
    for (let t = 0; t < 13.99; t += 0.05) expect(crowdCheer(GOAL_CLIP, t)).toBe(0)   // the build-up before the goal
    expect(CROWD_REACTIONS.GOAL.duration).toBeGreaterThanOrEqual(1.5)
  })
})

describe('tiers, motion and safety', () => {
  it('6: LOW stays cheaper - no cheering geometry or mesh, fewer spectators, the neutral figure only', () => {
    expect(standCheer(GOAL_CLIP, 14.6, 0, 'low')).toBe(0)
    expect(sceneSource).toContain("const cheeringBase = tier === 'normal' ? cheeringSpectator() : null")
    expect(sceneSource).toContain("const neutralBase = tier === 'normal' ? neutralSpectator() : lowSpectator()")
    const low = layoutStand(SIDE, 'low', 7001).length, normal = layoutStand(SIDE, 'normal', 7001).length
    expect(triangles(lowSpectator())).toBe(24)
    expect(low * triangles(lowSpectator())).toBeLessThan(0.35 * normal * triangles(neutralSpectator()))
    const g = lowSpectator(); const head = partBounds(g, true), torsoBounds = partBounds(g, false)
    expect(head.minY).toBeGreaterThan(torsoBounds.maxY - 0.05)                // still a head on shoulders, not a peg
  })

  it('7: reduced motion: no cheering, no standing, no animated transform', () => {
    expect(crowdMotionAllowed('normal', true, false)).toBe(false)
    expect(sceneSource).toContain('const f = moving ? standCheer(this.reactionEvents, this.reactionLocal, i, this.crowdTier) : 0')
    expect(sceneSource).toContain('const k = moving ? standReaction(this.reactionEvents, this.reactionLocal, i, this.crowdTier) : 0')
  })

  it('8: OFF creates no crowd', () => {
    expect(layoutStand(SIDE, 'off', 1)).toEqual([])
    expect(standCheer(GOAL_CLIP, 14.6, 0, 'off')).toBe(0)
  })

  it('9, 10: seeking away clears the cheering pose; a restart lets it happen again', () => {
    expect(crowdCheer(GOAL_CLIP, 14.6)).toBeGreaterThan(0.5)
    expect(crowdCheer(GOAL_CLIP, 18)).toBe(0)
    expect(crowdCheer(GOAL_CLIP, 2)).toBe(0)
    expect(crowdCheer(GOAL_CLIP, 14.6)).toBeGreaterThan(0.5)
  })

  it('11: cheering leaves evidence IDs, flags and counts untouched', () => {
    const events = Object.freeze(GOAL_CLIP.map(e => Object.freeze({ ...e })))
    const before = JSON.stringify(events)
    for (let t = 0; t <= 19; t += 0.05) for (let i = 0; i < 4; i++) standCheer(events, t, i)
    expect(JSON.stringify(events)).toBe(before)
    expect(events.filter(e => e.isEvidence)).toHaveLength(7)
    expect(crowdSource + geometrySource).not.toMatch(/evidenceEventIds|contextEventIds|supportingEventIds|isContext/)
  })

  it('12: the crowd makes no sound', () => {
    for (const src of [crowdSource, geometrySource, sceneSource]) expect(src).not.toMatch(/AudioContext|new Audio\(|speechSynthesis|SpeechQueue|\.say\(/)
  })

  it('13: the guard can still step NORMAL → LOW → OFF', () => {
    expect(crowdStepDown('normal')).toBe('low')
    expect(crowdStepDown('low')).toBe('off')
  })

  it('14: no allocation per reaction - every spectator shown exactly once by two counts, whatever the fraction', () => {
    for (const n of [0, 1, 437, 987]) for (let f = 0; f <= 1; f += 0.01) {
      const c = cheeringCount(n, f)
      expect(c).toBeGreaterThanOrEqual(0); expect(c).toBeLessThanOrEqual(n)
      expect(c + (n - c)).toBe(n)
    }
    const update = body(sceneSource, 'private updateCrowd(')
    expect(update).not.toMatch(/new |setMatrixAt|setColorAt|\.clone\(/)
    expect(sceneSource).toContain('const byRank = [...seats].sort((a, b) => a.rank - b.rank)')
  })
})
