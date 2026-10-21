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

/**
 * One seat. `rank` (0..1, seeded) decides how strongly a spectator reacts: the lowest ranks cheer, the next ones stand,
 * the rest barely move. `slot` is the spectator's reaction cohort: slot 0 reacts first and holds longest.
 */
export interface Spectator { x: number; y: number; z: number; scale: number; color: number; rank: number; slot: number }

/** Spectators keep this far back from a stand's front edge so low cameras (sideline, behind goal) sit in front of them. */
export const FRONT_CLEARANCE = 4
const ROW_SPACING = 1.2
const SEAT_SPACING = 0.9
const END_MARGIN = 2
export const OCCUPANCY: Record<Exclude<CrowdTier, 'off'>, number> = { low: 0.22, normal: 0.5 }

/**
 * Restrained clothing colours, light enough to stand out against the dark stands. No club colours: allegiance is not
 * known, so none is implied.
 */
export const CROWD_PALETTE = ['#8a93a6', '#b8bcc6', '#64708a', '#b5a184', '#9a6e60', '#748f7a', '#d2ccbf', '#556078', '#a08a69', '#857a96']

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

/** Reaction cohorts per stand: NORMAL spreads a reaction over 8 groups, LOW over 3. */
export const COHORTS: Record<Exclude<CrowdTier, 'off'>, number> = { normal: 8, low: 3 }

/**
 * Deterministic seats for one stand at a tier. Off → none. Cohorts are loose patches: mostly by position along the stand,
 * partly random (seeded), with the order in which those patches react shuffled per stand - not a sweeping line.
 */
export function layoutStand(stand: StandShape, tier: CrowdTier, seed: number): Spectator[] {
  if (tier === 'off') return []
  const rand = seeded(seed)
  const ranks = seeded(seed ^ 0x5bd1e995)            // separate streams, so ranks and cohorts never change the seating
  const groups = seeded(seed ^ 0x2545f491)
  const n = COHORTS[tier]
  const order = shuffled(n, seeded(seed ^ 0x68e31da4))
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
        rank: ranks(),
        slot: 0.65 * (z / stand.length + 0.5) + 0.35 * groups(),    // a cohort key for now; replaced below
      })
    }
  }
  // Equal-sized cohorts: sort by key, cut into n groups, then map each group to its (shuffled) reaction slot.
  const byKey = out.map((s, i) => [s.slot, i] as const).sort((a, b) => a[0] - b[0])
  byKey.forEach(([, i], k) => { out[i].slot = order[Math.min(n - 1, Math.floor((k * n) / byKey.length))] })
  return out
}

/** A seeded permutation of 0..n-1 (Fisher-Yates). */
function shuffled(n: number, rand: () => number) {
  const a = Array.from({ length: n }, (_, i) => i)
  for (let i = n - 1; i > 0; i--) { const j = Math.floor(rand() * (i + 1)); [a[i], a[j]] = [a[j], a[i]] }
  return a
}

// ---------------------------------------------------------------- reactions

/**
 * How the crowd responds to an event label: strength (0..1), how long the first cohort's response lasts (replay seconds),
 * how many cohorts take part, how far apart their onsets are spread (seconds, first to last), and whether the
 * participating spectators raise their arms. Later cohorts start later and settle sooner, so a goal rises and settles in
 * waves. Everything else (passes, carries, routine movement) is ignored.
 */
export interface CohortReaction { amplitude: number; duration: number; cohorts: number; spread: number; cheer: boolean }

/** NORMAL: goal = every cohort, arms up; shot = a few cohorts ("ooh"); turnovers = one or two groups half-stand. */
export const CROWD_REACTIONS: Record<string, CohortReaction> = {
  GOAL: { amplitude: 1, duration: 2.2, cohorts: 8, spread: 0.35, cheer: true },
  SHOT_ON_TARGET: { amplitude: 0.6, duration: 1.2, cohorts: 3, spread: 0.18, cheer: true },
  SHOT_OFF_TARGET: { amplitude: 0.55, duration: 1.1, cohorts: 2, spread: 0.12, cheer: true },
  SHOT_BLOCKED: { amplitude: 0.5, duration: 1, cohorts: 2, spread: 0.12, cheer: true },
  HIGH_PRESS_INTERCEPTION: { amplitude: 0.3, duration: 0.7, cohorts: 2, spread: 0.1, cheer: false },
  HIGH_PRESSURE_RECOVERY: { amplitude: 0.28, duration: 0.7, cohorts: 2, spread: 0.1, cheer: false },
  INTERCEPTION: { amplitude: 0.25, duration: 0.6, cohorts: 1, spread: 0, cheer: false },
  TURNOVER: { amplitude: 0.25, duration: 0.6, cohorts: 1, spread: 0, cheer: false },
}

/** LOW: three cohorts and a simple rise (no arms); a goal ripples through all three, a shot moves one; turnovers nothing. */
export const LOW_REACTIONS: Record<string, CohortReaction> = {
  GOAL: { amplitude: 1, duration: 2.2, cohorts: 3, spread: 0.3, cheer: false },
  SHOT_ON_TARGET: { amplitude: 0.4, duration: 1.2, cohorts: 1, spread: 0, cheer: false },
  SHOT_OFF_TARGET: { amplitude: 0.35, duration: 1.1, cohorts: 1, spread: 0, cheer: false },
  SHOT_BLOCKED: { amplitude: 0.3, duration: 1, cohorts: 1, spread: 0, cheer: false },
}

/** Each later cohort holds this much shorter (the last one by the whole fraction), so settling is staggered too. */
export const HOLD_DROP = 0.4

/** A cheering cohort raises its arms this long after its own onset and lowers them at this fraction of its duration. */
export const CHEER_WINDOW = { start: 0.1, end: 0.75 } as const

/**
 * The pose at full reaction (k = 1), in metres, applied per spectator in the vertex shader: NORMAL stands up (the body
 * stretches upward from the feet), rises a little and leans towards the pitch; LOW only rises.
 */
export const CROWD_POSE = { stretch: 0.3, lift: 0.08, lean: 0.12 } as const
export const LOW_POSE = { stretch: 0, lift: 0.45, lean: 0 } as const
/** LOW's rise at full reaction, in metres. */
export const LOW_RISE_M = LOW_POSE.lift

/** Spectators ranked just above the cheering ones stand fully (a band this wide); the rest only lean. */
export const STAND_BAND = { full: 0.12, fade: 0.3, rest: 0.3 } as const

/**
 * Small fixed differences between the four stands so they do not move as one: strength, onset delay (replay seconds) and
 * the share of spectators who cheer. Deterministic; says nothing about which stand supports which team.
 */
export const STAND_VARIATION = [
  { strength: 1, delay: 0, cheer: 0.55 },
  { strength: 0.92, delay: 0.08, cheer: 0.5 },
  { strength: 0.85, delay: 0.15, cheer: 0.58 },
  { strength: 0.95, delay: 0.23, cheer: 0.52 },
] as const

export const variationOf = (stand: number) => STAND_VARIATION[stand % STAND_VARIATION.length]

/** Reactions run when wanted and affordable: any visible tier, no reduced motion, guard not tripped. OFF never reacts. */
export const crowdMotionAllowed = (tier: CrowdTier, reducedMotion: boolean, guardTripped: boolean) =>
  tier !== 'off' && !reducedMotion && !guardTripped

const tableOf = (tier: CrowdTier) => (tier === 'low' ? LOW_REACTIONS : CROWD_REACTIONS)

/** When a cohort slot starts and how long its response lasts, for one reaction; null if the slot does not take part. */
export function cohortTiming(r: CohortReaction, slot: number): { onset: number; duration: number } | null {
  if (slot >= r.cohorts) return null
  const last = Math.max(1, r.cohorts - 1)
  return { onset: r.cohorts > 1 ? (slot * r.spread) / last : 0, duration: r.duration * (1 - (HOLD_DROP * slot) / last) }
}

/** A quick rise, a hold, then an ease back down; 0 before the start and after the duration. */
export function envelope(duration: number, age: number) {
  if (age < 0 || age > duration) return 0
  const rise = Math.min(0.25, duration * 0.25)
  const hold = duration * 0.45
  return age < rise ? age / rise : age < hold ? 1 : 1 - smooth((age - hold) / (duration - hold))
}

const smooth = (x: number) => { const c = Math.min(1, Math.max(0, x)); return c * c * (3 - 2 * c) }

/**
 * The reaction driving one stand at clip-local replay time t (the stand's onset delay applied): the strongest event whose
 * response is still running in any cohort, and its age. Pure: reads labels and times only, so pause, seek, restart and
 * clip changes follow the replay clock exactly.
 */
export function standEvent(events: readonly { label: string; time: number }[], t: number, stand: number, tier: CrowdTier):
  { label: string; reaction: CohortReaction; age: number } | null {
  if (tier === 'off') return null
  const table = tableOf(tier)
  const age0 = t - variationOf(stand).delay
  let best: { label: string; reaction: CohortReaction; age: number } | null = null
  for (const e of events) {
    const r = table[e.label]
    if (!r) continue
    const age = age0 - e.time
    if (age < 0 || age > r.duration + r.spread) continue
    if (!best || r.amplitude > best.reaction.amplitude) best = { label: e.label, reaction: r, age }
  }
  return best
}

/** How strongly one spectator reacts (0..1) to a reaction at its age: its cohort's envelope, scaled by its rank band. */
export function spectatorReaction(seat: Pick<Spectator, 'rank' | 'slot'>, r: CohortReaction, age: number, stand: number): number {
  const timing = cohortTiming(r, seat.slot)
  if (!timing) return 0
  const v = variationOf(stand)
  const band = 1 - (1 - STAND_BAND.rest) * smoothBetween(v.cheer + STAND_BAND.full, v.cheer + STAND_BAND.fade, seat.rank)
  return v.strength * r.amplitude * envelope(timing.duration, age - timing.onset) * band
}

const smoothBetween = (a: number, b: number, x: number) => smooth((x - a) / (b - a))

/** Whether one cohort slot has its arms up at this age (NORMAL cheering reactions only). */
export function cohortCheering(r: CohortReaction, slot: number, age: number): boolean {
  const timing = cohortTiming(r, slot)
  if (!r.cheer || !timing) return false
  return age >= timing.onset + CHEER_WINDOW.start && age <= timing.onset + CHEER_WINDOW.end * timing.duration
}

/**
 * How many cohort slots are cheering, counted from slot 0. Earlier slots start sooner AND stop later, so the cheering slots
 * are always 0..m-1 - which lets the scene show them with one instance count (no per-spectator updates).
 */
export function cheeringSlots(r: CohortReaction, age: number): number {
  let m = 0
  while (m < r.cohorts && cohortCheering(r, m, age)) m++
  return m
}

/** Whether one spectator shows the cheering pose: a cheering slot and a rank inside the stand's cheering share. */
export const spectatorCheering = (seat: Pick<Spectator, 'rank' | 'slot'>, r: CohortReaction, age: number, stand: number) =>
  seat.rank < variationOf(stand).cheer && seat.slot < cheeringSlots(r, age)

/**
 * Expected share of a stand cheering (0..1) at clip-local replay time t: cheering slots out of all slots, times the stand's
 * cheering share. NORMAL only (LOW has no cheering pose). A summary for tests and debugging; the scene counts real seats.
 */
export function standCheer(events: readonly { label: string; time: number }[], t: number, stand: number, tier: CrowdTier = 'normal') {
  if (tier !== 'normal') return 0
  const e = standEvent(events, t, stand, tier)
  return e ? (cheeringSlots(e.reaction, e.age) / COHORTS.normal) * variationOf(stand).cheer : 0
}

/** The same for an unvaried stand (no onset delay; the first stand's cheering share). */
export const crowdCheer = (events: readonly { label: string; time: number }[], t: number) => standCheer(events, t, 0)

/** How many of a stand's spectators cheer when the first m cohort slots cheer, from its seats (the scene precomputes this). */
export const cheeringCount = (seats: readonly Pick<Spectator, 'rank' | 'slot'>[], m: number, stand: number) =>
  seats.filter(x => x.rank < variationOf(stand).cheer && x.slot < m).length

/** Group-level reaction (0..1) at clip-local replay time t: the first cohort's envelope. A summary for tests and debugging. */
export function crowdReaction(events: readonly { label: string; time: number }[], t: number,
  table: Record<string, CohortReaction> = CROWD_REACTIONS): number {
  let best = 0
  for (const e of events) {
    const r = table[e.label]
    if (r) best = Math.max(best, r.amplitude * envelope(r.duration, t - e.time))
  }
  return best
}

/** One stand's group-level reaction (0..1): its strongest event, first cohort, with the stand's variation. */
export const standReaction = (events: readonly { label: string; time: number }[], t: number, stand: number, tier: CrowdTier = 'normal') => {
  const e = standEvent(events, t, stand, tier)
  return e ? variationOf(stand).strength * e.reaction.amplitude * envelope(e.reaction.duration, e.age) : 0
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
