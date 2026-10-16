import { describe, expect, it } from 'vitest'
import { ballAt, cameraPose, currentEvent, floorIndex, layout, locate, matchClock, samplePlayer, stepEvent, toWorld } from './timeline'
import type { ReplayClip, ReplaySequence } from './types'

const clip = (key: ReplayClip['key'], duration: number, startSecond = 2058): ReplayClip => ({
  key, label: key.toUpperCase(), startSecond, endSecond: startSecond + duration, duration,
  scoreAtStart: { home: 0, away: 0 },
  events: [
    { eventId: 'a', sequence: 1, time: 1, endTime: 2, matchSecond: startSecond + 1, clock: '', type: 'Pass', outcome: 'Complete', label: 'PASS', teamId: 'H', playerId: 'H-1', relatedPlayerId: 'H-2', x: 40, y: 50, endX: 60, endY: 50, isEvidence: true },
    { eventId: 'b', sequence: 2, time: 4, endTime: 4, matchSecond: startSecond + 4, clock: '', type: 'Shot', outcome: 'Goal', label: 'GOAL', teamId: 'H', playerId: 'H-2', x: 60, y: 50, endX: 100, endY: 50, isEvidence: true },
  ],
  tracks: [{ playerId: 'H-2', samples: [
    { t: 0, x: 50, y: 40, provenance: 'ESTIMATED_POSITION' },
    { t: 2, x: 60, y: 50, provenance: 'ACTUAL_EVENT_POSITION' },
    { t: 4, x: 60, y: 50, provenance: 'ACTUAL_EVENT_POSITION' },
  ] }],
  ball: [
    { t0: 0, t1: 1, motion: 'Held', x0: 40, y0: 50, x1: 40, y1: 50, z0: 0, z1: 0, arcHeight: 0, holderId: 'H-1', provenance: 'INTERPOLATED_POSITION' },
    { t0: 1, t1: 2, motion: 'Pass', x0: 40, y0: 50, x1: 60, y1: 50, z0: 0, z1: 0, arcHeight: 2, eventId: 'a', provenance: 'INTERPOLATED_POSITION' },
    { t0: 2, t1: 4, motion: 'Held', x0: 60, y0: 50, x1: 60, y1: 50, z0: 0, z1: 0, arcHeight: 0, holderId: 'H-2', provenance: 'INTERPOLATED_POSITION' },
  ],
  overlays: [],
  camera: { attackingTeamId: 'H', attackDirection: 1, focusPlayerId: 'H-2', centroidX: 50, centroidY: 50 },
})

const seq = (clips: ReplayClip[]): ReplaySequence => ({
  matchId: 'M', insightId: 'I', insightType: 'ATTACK_CHANNEL_SHIFT', homeTeamId: 'H', awayTeamId: 'A',
  insightWindow: { fromSecond: 0, toSecond: 1 }, evidenceEventIds: [], relevantPlayerIds: [], players: [], clips, notice: '',
})

describe('coordinates', () => {
  it('maps the absolute pitch to world metres centred on the centre spot', () => {
    expect(toWorld(50, 50)).toEqual({ x: 0, z: 0 })
    expect(toWorld(100, 0)).toEqual({ x: 52.5, z: -34 })
    expect(toWorld(0, 100)).toEqual({ x: -52.5, z: 34 })
  })
})

describe('timeline', () => {
  it('places clips back to back and locates global times', () => {
    const { placed, total } = layout(seq([clip('before', 10), clip('after', 20)]))
    expect(total).toBe(30)
    expect(placed[1].offset).toBe(10)
    expect(locate(placed, 12).placed.clip.key).toBe('after')
    expect(locate(placed, 12).local).toBe(2)
    expect(locate(placed, 99).local).toBe(20)
  })

  it('steps between events across clips', () => {
    const { placed } = layout(seq([clip('before', 10), clip('after', 20)]))
    expect(stepEvent(placed, 0, 1)).toBe(1)
    expect(stepEvent(placed, 4.5, 1)).toBe(11)
    expect(stepEvent(placed, 11.5, -1)).toBe(11)
  })

  it('binary-searches samples', () => {
    const arr = [{ t: 0 }, { t: 1 }, { t: 2 }]
    expect(floorIndex(arr, -1)).toBe(-1)
    expect(floorIndex(arr, 1.5)).toBe(1)
    expect(floorIndex(arr, 9)).toBe(2)
  })

  it('formats the match clock from the clip start', () => {
    expect(matchClock(clip('key', 10), 4.6)).toBe('34:22')
  })
})

describe('interpolation and provenance', () => {
  it('interpolates players linearly and reports the nearest provenance', () => {
    const track = clip('key', 10).tracks[0]
    const mid = samplePlayer(track, 1)
    expect(mid.x).toBeCloseTo(55)
    expect(mid.y).toBeCloseTo(45)
    expect(samplePlayer(track, 0.4).provenance).toBe('ESTIMATED_POSITION')
    expect(samplePlayer(track, 1.8).provenance).toBe('ACTUAL_EVENT_POSITION')
    expect(samplePlayer(track, 50).x).toBe(60)
  })

  it('moves the ball along a pass with a visual arc and returns it to the ground at the end', () => {
    const c = clip('key', 10)
    const mid = ballAt(c, 1.5)
    expect(mid.x).toBeCloseTo(50)
    expect(mid.z).toBeCloseTo(2)
    expect(ballAt(c, 2).z).toBeCloseTo(0)
  })

  it('attaches a held ball to its holder', () => {
    const c = clip('key', 10)
    const b = ballAt(c, 3, (id) => (id === 'H-2' ? { x: 61, y: 49, vx: 0, vy: 0, provenance: 'ACTUAL_EVENT_POSITION' } : undefined))
    expect(b.x).toBe(61)
    expect(b.holderId).toBe('H-2')
  })

  it('captions the latest started event and lets it fade', () => {
    const c = clip('key', 10)
    expect(currentEvent(c, 0.5)).toBeNull()
    expect(currentEvent(c, 1.2)?.label).toBe('PASS')
    expect(currentEvent(c, 4.1)?.label).toBe('GOAL')
    expect(currentEvent(c, 9.9)).toBeNull()
  })
})

describe('camera presets', () => {
  it('puts the behind-goal camera behind the attacked goal', () => {
    expect(cameraPose('behindGoal', { x: 10, z: 0 }, 1).position.x).toBeGreaterThan(52.5)
    expect(cameraPose('behindGoal', { x: 10, z: 0 }, -1).position.x).toBeLessThan(-52.5)
  })

  it('keeps the tactical view high and the sideline view low on the near touchline', () => {
    const tactical = cameraPose('tactical', { x: 0, z: 0 }, 1)
    const sideline = cameraPose('sideline', { x: 20, z: 0 }, 1)
    expect(tactical.position.y).toBeGreaterThan(40)
    expect(sideline.position.y).toBeLessThan(20)
    expect(sideline.position.z).toBeGreaterThan(34)
    expect(sideline.target.x).toBe(20)
  })

  it('follows the focus player from behind in the attack direction', () => {
    const pose = cameraPose('player', { x: 0, z: 0 }, -1, { x: 20, z: 5 })
    expect(pose.position.x).toBeGreaterThan(20)
    expect(pose.target.x).toBeLessThan(20)
  })
})
