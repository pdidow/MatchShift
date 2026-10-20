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
  gpu?: string                // WebGL renderer string, when the browser exposes it
}

/** Software rasterisers: WebGL works, but every frame is drawn by the CPU. */
const SOFTWARE_GPU = /swiftshader|llvmpipe|softpipe|software|basic render|mesa offscreen/i

/**
 * Default tier from capabilities. Safe failure: a missing signal never counts as "capable".
 *  - OFF: no WebGL2, a software renderer, a small texture limit, under 4 GB or under 4 cores.
 *  - NORMAL: only when every signal is present and clearly capable - 8+ cores, 8+ GB reported, a texture limit of 8192+,
 *    a known hardware GPU and no more than a 4K frame to fill. Cores alone never imply a strong GPU.
 *  - LOW: everything else, including unknown or ambiguous devices.
 * The runtime frame-time guard (CrowdFrameGuard) remains the final safety net.
 */
export function autoCrowdTier(c: CrowdCapabilities): CrowdTier {
  if (!c.webgl || c.webgl2 !== true) return 'off'
  if (c.gpu !== undefined && SOFTWARE_GPU.test(c.gpu)) return 'off'
  if (c.maxTextureSize !== undefined && c.maxTextureSize < 4096) return 'off'
  if (c.deviceMemory !== undefined && c.deviceMemory < 4) return 'off'
  if (c.cores !== undefined && c.cores < 4) return 'off'
  const heavyScreen = (c.pixelRatio ?? 1) * (c.pixelRatio ?? 1) * (c.viewportPixels ?? 0) > 8_300_000 // more than a 4K frame
  const capable = c.cores !== undefined && c.cores >= 8 && c.deviceMemory !== undefined && c.deviceMemory >= 8
    && c.maxTextureSize !== undefined && c.maxTextureSize >= 8192 && c.gpu !== undefined && c.viewportPixels !== undefined && !heavyScreen
  return capable ? 'normal' : 'low'
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
export function readCrowdCapabilities(renderer?: { isWebGL2?: boolean; maxTextureSize?: number; gpu?: string }): CrowdCapabilities {
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
    gpu: typeof renderer?.gpu === 'string' && renderer.gpu.length > 0 ? renderer.gpu : undefined,
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

/**
 * Event labels that move the crowd, how strongly (0..1) and for how long (replay seconds). A goal is the strongest; a shot
 * or chance is medium; an interception, high recovery or turnover is small. Everything else (passes, carries, routine
 * movement) is ignored.
 */
export const CROWD_REACTIONS: Record<string, { amplitude: number; duration: number }> = {
  GOAL: { amplitude: 1, duration: 2.2 },
  SHOT_ON_TARGET: { amplitude: 0.55, duration: 1.2 },
  SHOT_OFF_TARGET: { amplitude: 0.5, duration: 1.1 },
  SHOT_BLOCKED: { amplitude: 0.45, duration: 1 },
  HIGH_PRESS_INTERCEPTION: { amplitude: 0.25, duration: 0.7 },
  HIGH_PRESSURE_RECOVERY: { amplitude: 0.22, duration: 0.7 },
  INTERCEPTION: { amplitude: 0.2, duration: 0.6 },
  TURNOVER: { amplitude: 0.2, duration: 0.6 },
}

/**
 * The pose at full reaction (k = 1), in metres, applied in the vertex shader to the shared spectator mesh: spectators
 * stand up (the body stretches upward from the feet), rise a little and lean towards the pitch. Sized to read from the
 * tactical camera (about 130 m from the far stand) without looking like jumping.
 */
export const CROWD_POSE = { stretch: 0.55, lift: 0.18, lean: 0.2 } as const

/**
 * Small fixed differences between the four stands so they do not move as one block: strength and a timing offset in
 * replay seconds. Deterministic; says nothing about which stand supports which team.
 */
export const STAND_VARIATION = [
  { strength: 1, delay: 0 },
  { strength: 0.92, delay: 0.06 },
  { strength: 0.85, delay: 0.12 },
  { strength: 0.95, delay: 0.03 },
] as const

/**
 * LOW reactions: the cheapest form - each stand's instanced mesh rises as one block (one transform per stand, no shader
 * change, no per-spectator work). Restrained: a goal clearly, a shot slightly, turnovers and routine play not at all.
 */
export const LOW_REACTIONS: Record<string, { amplitude: number; duration: number }> = {
  GOAL: { amplitude: 1, duration: 2.2 },
  SHOT_ON_TARGET: { amplitude: 0.4, duration: 1.2 },
  SHOT_OFF_TARGET: { amplitude: 0.35, duration: 1.1 },
  SHOT_BLOCKED: { amplitude: 0.3, duration: 1 },
}

/** LOW's block rise at full reaction, in metres (NORMAL's head rise is CROWD_POSE.stretch + CROWD_POSE.lift). */
export const LOW_RISE_M = 0.45

/** Reactions run when wanted and affordable: any visible tier, no reduced motion, guard not tripped. OFF never reacts. */
export const crowdMotionAllowed = (tier: CrowdTier, reducedMotion: boolean, guardTripped: boolean) =>
  tier !== 'off' && !reducedMotion && !guardTripped

/**
 * Group-level reaction (0..1) at clip-local replay time t, from events already in the timeline: a quick rise, a hold,
 * then an ease back down. Pure: reads labels and times only, so pause, seek, restart and clip changes follow the replay
 * clock exactly (a seek away from an event ends its reaction; replaying it reacts again).
 */
export function crowdReaction(events: readonly { label: string; time: number }[], t: number,
  table: Record<string, { amplitude: number; duration: number }> = CROWD_REACTIONS): number {
  let best = 0
  for (const e of events) {
    const r = table[e.label]
    if (!r) continue
    const age = t - e.time
    if (age < 0 || age > r.duration) continue
    const rise = Math.min(0.25, r.duration * 0.25)
    const hold = r.duration * 0.45
    const envelope = age < rise ? age / rise
      : age < hold ? 1
      : 1 - smooth((age - hold) / (r.duration - hold))
    best = Math.max(best, r.amplitude * envelope)
  }
  return best
}

const smooth = (x: number) => { const c = Math.min(1, Math.max(0, x)); return c * c * (3 - 2 * c) }

/** One stand's reaction (0..1) at a tier, with its fixed strength and timing offset. LOW uses its smaller table; OFF is 0. */
export const standReaction = (events: readonly { label: string; time: number }[], t: number, stand: number, tier: CrowdTier = 'normal') => {
  if (tier === 'off') return 0
  const v = STAND_VARIATION[stand % STAND_VARIATION.length]
  return v.strength * crowdReaction(events, t - v.delay, tier === 'low' ? LOW_REACTIONS : CROWD_REACTIONS)
}

// ---------------------------------------------------------------- performance guard

/**
 * Watches frame times once, for a short window after the crowd appears. If the average frame takes longer than the budget,
 * it says "too slow" once. It ignores the warm-up and long gaps (tab switches), then stops measuring - no continuous
 * benchmarking. The automatic crowd uses 40 ms (about 25 fps); a crowd the viewer switched on explicitly is only reduced
 * when the replay becomes hard to use (66 ms, about 15 fps).
 */
export class CrowdFrameGuard {
  static readonly WARMUP_S = 1
  static readonly WINDOW_S = 3
  static readonly BUDGET_MS = 40
  static readonly EXPLICIT_BUDGET_MS = 66
  private elapsed = 0
  private total = 0
  private frames = 0
  private done = false
  private readonly budgetMs: number

  constructor(budgetMs: number = CrowdFrameGuard.BUDGET_MS) { this.budgetMs = budgetMs }

  /**
   * Feed one frame (seconds since the previous one). Returns true exactly once if the crowd should be reduced. Gaps over
   * 2 s (sleep, a returning tab; hidden tabs are not fed at all) are ignored. Slow frames up to that count, clamped to
   * 0.5 s, so a device that really renders at 1-4 fps is caught rather than mistaken for a tab switch.
   */
  feed(dt: number): boolean {
    if (this.done || dt <= 0 || dt > 2) return false
    dt = Math.min(dt, 0.5)
    this.elapsed += dt
    if (this.elapsed < CrowdFrameGuard.WARMUP_S) return false
    this.total += dt
    this.frames++
    if (this.elapsed < CrowdFrameGuard.WARMUP_S + CrowdFrameGuard.WINDOW_S) return false
    this.done = true
    return (this.total / this.frames) * 1000 > this.budgetMs
  }

  get finished() { return this.done }
}

/**
 * Where the guard goes when frames are too slow: NORMAL drops to LOW (about half the spectators at 12 instead of 20
 * triangles each, and no reactions), LOW drops to OFF. Never upward, so at most two steps per replay and no oscillation.
 */
export const crowdStepDown = (tier: CrowdTier): CrowdTier => (tier === 'normal' ? 'low' : 'off')

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
