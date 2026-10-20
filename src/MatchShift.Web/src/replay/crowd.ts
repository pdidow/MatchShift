// Optional decorative stadium crowd for the Evidence Replay. Pure logic only (no WebGL): quality tiers, capability-based
// defaults, deterministic seat layout, group-level reactions and a short frame-time guard.
//
// The crowd is decoration. Nothing here reads or writes evidence: no event IDs, no supporting/context sets, no counts.
// Reactions only look at the labels of events that are already in the replay timeline.

export type CrowdTier = 'off' | 'low' | 'normal'

/** What the browser reports. Every field may be missing; missing data never enables the crowd on its own. */
export interface CrowdCapabilities {
  webgl: boolean
  webgl2?: boolean
  deviceMemory?: number       // GB, Chromium only
  cores?: number              // navigator.hardwareConcurrency
  maxTextureSize?: number
  pixelRatio?: number
  viewportPixels?: number     // CSS width x height
}

/**
 * Default tier from capabilities. Conservative: no WebGL2 → off; unknown CPU → off; a high-resolution screen on a
 * modest CPU → low; plenty of cores and memory (or memory unreported) → normal.
 */
export function autoCrowdTier(c: CrowdCapabilities): CrowdTier {
  if (!c.webgl || c.webgl2 !== true) return 'off'
  if (c.cores === undefined || !Number.isFinite(c.cores)) return 'off'
  if (c.maxTextureSize !== undefined && c.maxTextureSize < 4096) return 'off'
  if (c.deviceMemory !== undefined && c.deviceMemory < 4) return 'off'
  if (c.cores < 4) return 'off'
  const heavyScreen = (c.pixelRatio ?? 1) * (c.pixelRatio ?? 1) * (c.viewportPixels ?? 0) > 8_300_000 // more than a 4K frame
  if (c.cores >= 8 && (c.deviceMemory === undefined || c.deviceMemory >= 8) && !heavyScreen) return 'normal'
  return 'low'
}

export type CrowdChoice = 'on' | 'off'

/**
 * The tier to render. An explicit user choice wins ("on" needs WebGL; weak hardware gets the LOW tier); without one the
 * capability default applies. Once the performance guard has switched the crowd off it stays off this session unless the
 * user turns it on again (which replaces the guard's decision).
 */
export function resolveCrowdTier(auto: CrowdTier, choice: CrowdChoice | null, caps: CrowdCapabilities, guardTripped: boolean): CrowdTier {
  if (!caps.webgl) return 'off'
  if (choice === 'off') return 'off'
  if (choice === 'on') return auto === 'off' ? 'low' : auto
  return guardTripped ? 'off' : auto
}

/** Safe, feature-detected read of the browser's capabilities. Never throws; missing APIs stay undefined. */
export function readCrowdCapabilities(renderer?: { isWebGL2?: boolean; maxTextureSize?: number }): CrowdCapabilities {
  const nav = (typeof navigator === 'undefined' ? undefined : navigator) as (Navigator & { deviceMemory?: number }) | undefined
  const win = typeof window === 'undefined' ? undefined : window
  const num = (v: unknown) => (typeof v === 'number' && Number.isFinite(v) ? v : undefined)
  return {
    webgl: renderer !== undefined,
    webgl2: renderer?.isWebGL2,
    deviceMemory: num(nav?.deviceMemory),
    cores: num(nav?.hardwareConcurrency),
    maxTextureSize: num(renderer?.maxTextureSize),
    pixelRatio: num(win?.devicePixelRatio),
    viewportPixels: win ? num(win.innerWidth * win.innerHeight) : undefined,
  }
}

// ---------------------------------------------------------------- layout

/** A stand as built by the scene, in its own local frame: x = depth from the front edge, z = along its length. */
export interface StandShape { length: number; depth: number; height: number }

export interface Spectator { x: number; y: number; z: number; scale: number; color: number }

/** Spectators keep this far back from a stand's front edge so low cameras (sideline, behind goal) sit in front of them. */
export const FRONT_CLEARANCE = 4
const ROW_SPACING = 1.2
const SEAT_SPACING = 0.9
const END_MARGIN = 2
export const OCCUPANCY: Record<Exclude<CrowdTier, 'off'>, number> = { low: 0.22, normal: 0.5 }

/** Muted clothing colours. No club colours: allegiance is not known, so none is implied. */
export const CROWD_PALETTE = ['#3a4252', '#4a5160', '#2d3340', '#5a4a46', '#3f4a3c', '#4b4f63', '#61594a', '#353a48']

/** Height of the stand's stepped surface at depth x (matches the extruded stand profile in scene.ts). */
export const standSurface = (s: StandShape, x: number) => 1.2 + (x * (s.height + 0.3)) / s.depth

/** Small deterministic PRNG (mulberry32): the same seed always gives the same crowd. */
export function seeded(seed: number) {
  let a = seed >>> 0
  return () => {
    a = (a + 0x6d2b79f5) >>> 0
    let t = a
    t = Math.imul(t ^ (t >>> 15), t | 1)
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61)
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

/** Deterministic seats for one stand at a tier. Off → none. */
export function layoutStand(stand: StandShape, tier: CrowdTier, seed: number): Spectator[] {
  if (tier === 'off') return []
  const rand = seeded(seed)
  const occupancy = OCCUPANCY[tier]
  const out: Spectator[] = []
  for (let x = FRONT_CLEARANCE; x <= stand.depth - 1; x += ROW_SPACING) {
    for (let z = -stand.length / 2 + END_MARGIN; z <= stand.length / 2 - END_MARGIN; z += SEAT_SPACING) {
      if (rand() > occupancy) continue
      out.push({
        x: x + (rand() - 0.5) * 0.2,
        y: standSurface(stand, x),
        z: z + (rand() - 0.5) * 0.25,
        scale: 0.94 + rand() * 0.12,               // narrow, safe range
        color: Math.floor(rand() * CROWD_PALETTE.length),
      })
    }
  }
  return out
}

// ---------------------------------------------------------------- reactions

/** Event labels that move the crowd, and how much / how long. Everything else (passes, carries...) is ignored. */
export const CROWD_REACTIONS: Record<string, { amplitude: number; duration: number }> = {
  GOAL: { amplitude: 1, duration: 2.2 },
  SHOT_ON_TARGET: { amplitude: 0.45, duration: 1.2 },
  SHOT_OFF_TARGET: { amplitude: 0.4, duration: 1.1 },
  SHOT_BLOCKED: { amplitude: 0.35, duration: 1 },
  INTERCEPTION: { amplitude: 0.15, duration: 0.8 },
  HIGH_PRESS_INTERCEPTION: { amplitude: 0.18, duration: 0.8 },
  HIGH_PRESSURE_RECOVERY: { amplitude: 0.15, duration: 0.8 },
  TURNOVER: { amplitude: 0.12, duration: 0.7 },
}

/** Reactions run only when they are virtually free and wanted: NORMAL tier, no reduced motion, guard not tripped. */
export const crowdMotionAllowed = (tier: CrowdTier, reducedMotion: boolean, guardTripped: boolean) =>
  tier === 'normal' && !reducedMotion && !guardTripped

/**
 * Group-level reaction (0..1) at clip-local time t, from events already in the timeline. A short rise, then a decay.
 * Pure: reads labels and times only.
 */
export function crowdReaction(events: readonly { label: string; time: number }[], t: number): number {
  let best = 0
  for (const e of events) {
    const r = CROWD_REACTIONS[e.label]
    if (!r) continue
    const age = t - e.time
    if (age < 0 || age > r.duration) continue
    const envelope = age < 0.25 ? age / 0.25 : 1 - (age - 0.25) / (r.duration - 0.25)
    best = Math.max(best, r.amplitude * Math.max(0, envelope))
  }
  return best
}

// ---------------------------------------------------------------- performance guard

/**
 * Watches frame times once, for a short window after the crowd appears. If the average frame takes longer than the budget
 * (about 25 fps), it says "disable" once. It ignores the warm-up and long gaps (tab switches), then stops measuring.
 */
export class CrowdFrameGuard {
  static readonly WARMUP_S = 1
  static readonly WINDOW_S = 3
  static readonly BUDGET_MS = 40
  private elapsed = 0
  private total = 0
  private frames = 0
  private done = false

  /** Feed one frame (seconds since the previous one). Returns true exactly once if the crowd should be switched off. */
  feed(dt: number): boolean {
    if (this.done || dt <= 0 || dt > 0.25) return false
    this.elapsed += dt
    if (this.elapsed < CrowdFrameGuard.WARMUP_S) return false
    this.total += dt
    this.frames++
    if (this.elapsed < CrowdFrameGuard.WARMUP_S + CrowdFrameGuard.WINDOW_S) return false
    this.done = true
    return (this.total / this.frames) * 1000 > CrowdFrameGuard.BUDGET_MS
  }

  get finished() { return this.done }
}

/** Remembered explicit choice ("on" / "off"), stored like the other small local preferences. Storage may be unavailable. */
export const CROWD_KEY = 'matchshift.replay.crowd'

export function loadCrowdChoice(): CrowdChoice | null {
  try {
    const v = localStorage.getItem(CROWD_KEY)
    return v === 'on' || v === 'off' ? v : null
  } catch { return null }
}

export function saveCrowdChoice(choice: CrowdChoice) {
  try { localStorage.setItem(CROWD_KEY, choice) } catch { /* storage unavailable: the choice lasts this session */ }
}
