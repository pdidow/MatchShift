// Pure replay maths - no Three.js here, so it can be unit tested.
import type { BallSegment, CameraPreset, PlayerTrack, Provenance, ReplayClip, ReplayEvent, ReplaySequence } from './types'

export const PITCH_LENGTH = 105
export const PITCH_WIDTH = 68

/** Absolute 0-100 pitch coordinates → world metres. X runs along the pitch, Z across it (Y = 0 is the far touchline). */
export const toWorld = (x: number, y: number) => ({ x: (x / 100) * PITCH_LENGTH - PITCH_LENGTH / 2, z: (y / 100) * PITCH_WIDTH - PITCH_WIDTH / 2 })

export interface Placed { clip: ReplayClip; index: number; offset: number }

/** Clips play back to back on one timeline. */
export function layout(seq: ReplaySequence): { placed: Placed[]; total: number } {
  let offset = 0
  const placed = seq.clips.map((clip, index) => {
    const p = { clip, index, offset }
    offset += clip.duration
    return p
  })
  return { placed, total: offset }
}

/** Which clip a global time falls in, and the time within it. */
export function locate(placed: Placed[], t: number): { placed: Placed; local: number } {
  const last = placed[placed.length - 1]
  for (const p of placed) {
    if (t < p.offset + p.clip.duration || p === last) return { placed: p, local: Math.min(Math.max(0, t - p.offset), p.clip.duration) }
  }
  return { placed: last, local: last.clip.duration }
}

/** Largest index i with arr[i].t <= t (binary search); -1 if none. */
export function floorIndex<T extends { t: number }>(arr: T[], t: number): number {
  let lo = 0
  let hi = arr.length - 1
  let ans = -1
  while (lo <= hi) {
    const mid = (lo + hi) >> 1
    if (arr[mid].t <= t) { ans = mid; lo = mid + 1 } else hi = mid - 1
  }
  return ans
}

export interface PlayerState { x: number; y: number; vx: number; vy: number; provenance: Provenance }

/** Linearly interpolate a player's track. Provenance is that of the nearest sample. */
export function samplePlayer(track: PlayerTrack, t: number): PlayerState {
  const s = track.samples
  const i = floorIndex(s, t)
  if (i < 0) return { x: s[0].x, y: s[0].y, vx: 0, vy: 0, provenance: s[0].provenance }
  if (i >= s.length - 1) return { x: s[s.length - 1].x, y: s[s.length - 1].y, vx: 0, vy: 0, provenance: s[s.length - 1].provenance }
  const a = s[i]
  const b = s[i + 1]
  const span = b.t - a.t || 1
  const f = Math.min(1, Math.max(0, (t - a.t) / span))
  return {
    x: a.x + (b.x - a.x) * f,
    y: a.y + (b.y - a.y) * f,
    vx: (b.x - a.x) / span,
    vy: (b.y - a.y) / span,
    provenance: f < 0.5 ? a.provenance : b.provenance,
  }
}

export interface BallState { x: number; y: number; z: number; segment: BallSegment | null; holderId?: string }

/**
 * Ball position at time t. Held segments attach the ball to the holder (via `holderAt`), slightly ahead of
 * their feet. Height: linear between endpoints plus a parabolic arc whose peak is `arcHeight` - a visual aid
 * only, because the synthetic data records no ball height.
 */
export function ballAt(clip: ReplayClip, t: number, holderAt?: (playerId: string) => PlayerState | undefined): BallState {
  const segs = clip.ball
  if (segs.length === 0) return { x: 50, y: 50, z: 0, segment: null }
  let seg = segs[segs.length - 1]
  for (const s of segs) {
    if (t <= s.t1) { seg = s; break }
  }
  const span = seg.t1 - seg.t0
  const f = span <= 0 ? 1 : Math.min(1, Math.max(0, (t - seg.t0) / span))
  if (seg.motion === 'Held' && seg.holderId && holderAt) {
    const h = holderAt(seg.holderId)
    if (h) {
      const speed = Math.hypot(h.vx, h.vy)
      const ahead = speed > 0.05 ? 0.9 / Math.max(speed, 1) : 0
      return { x: h.x + h.vx * ahead, y: h.y + h.vy * ahead, z: 0, segment: seg, holderId: seg.holderId }
    }
  }
  const z = seg.z0 + (seg.z1 - seg.z0) * f + 4 * seg.arcHeight * f * (1 - f)
  return { x: seg.x0 + (seg.x1 - seg.x0) * f, y: seg.y0 + (seg.y1 - seg.y0) * f, z, segment: seg, holderId: seg.holderId }
}

/** The event to caption: the latest one that has started (kept on screen for up to 3.5 s). */
export function currentEvent(clip: ReplayClip, t: number, holdSeconds = 3.5): ReplayEvent | null {
  let found: ReplayEvent | null = null
  for (const e of clip.events) {
    if (e.time <= t + 1e-6) found = e
    else break
  }
  return found && t - Math.max(found.time, found.endTime) <= holdSeconds ? found : null
}

/** Time of the previous/next event across the whole timeline (for the step buttons). */
export function stepEvent(placed: Placed[], t: number, direction: 1 | -1): number {
  const times = placed.flatMap(p => p.clip.events.map(e => p.offset + e.time)).sort((a, b) => a - b)
  if (direction > 0) return times.find(x => x > t + 0.05) ?? t
  const before = times.filter(x => x < t - 0.25)
  return before.length ? before[before.length - 1] : 0
}

export interface Vec3 { x: number; y: number; z: number }
export interface CameraPose { position: Vec3; target: Vec3 }

/**
 * Camera preset poses in world metres.
 * - tactical: high, angled, slightly favouring where the ball is.
 * - sideline: low TV-style camera on the near touchline following the ball.
 * - behindGoal: behind the goal the attacking team is attacking, looking back up the pitch.
 * - player: behind the focus player relative to the attack direction.
 */
export function cameraPose(preset: CameraPreset, ball: { x: number; z: number }, attackDirection: 1 | -1, focus?: { x: number; z: number }): CameraPose {
  switch (preset) {
    case 'sideline':
      return { position: { x: ball.x * 0.85, y: 13, z: 47 }, target: { x: ball.x, y: 0, z: ball.z * 0.4 } }
    case 'behindGoal':
      return {
        position: { x: attackDirection * 66, y: 11, z: ball.z * 0.25 },
        target: { x: ball.x - attackDirection * 6, y: 0, z: ball.z },
      }
    case 'player': {
      const p = focus ?? ball
      return { position: { x: p.x - attackDirection * 13, y: 7.5, z: p.z + 6 }, target: { x: p.x + attackDirection * 6, y: 1, z: p.z } }
    }
    default: {
      // High and back enough to frame most of the pitch, drifting gently towards the ball.
      const cx = ball.x * 0.45
      return { position: { x: cx, y: 76, z: 62 }, target: { x: cx, y: 0, z: 4 + ball.z * 0.2 } }
    }
  }
}

/** Seconds → m:ss for the replay clock. */
export const formatTime = (t: number) => `${Math.floor(t / 60)}:${String(Math.floor(t % 60)).padStart(2, '0')}`

/** Match clock at a point in a clip, e.g. "34:24". */
export const matchClock = (clip: ReplayClip, local: number) => {
  const s = Math.floor(clip.startSecond + local)
  return `${String(Math.floor(s / 60)).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`
}
