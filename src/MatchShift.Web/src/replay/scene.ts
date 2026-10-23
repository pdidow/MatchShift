// Three.js 3D tactical replay scene. Procedural geometry only: no external models, textures or branding.
import * as THREE from 'three'
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js'
import { BALL_RADIUS, createFootball, orientFootball } from './ballModel'
import { BallTrackingOverlay } from './BallTrackingOverlay'
import { BallTrail } from './BallTrail'
import { keyEventPulse, keyEventTimes } from './ballEffects'
import { BALL_SIZE_SCALE, DEFAULT_BALL_SETTINGS } from './ballVisualization'
import type { BallVisualizationSettings } from './ballVisualization'
import { PlayerModelLibrary } from './playerModel'
import type { Footballer } from './playerModel'
import { REPLAY_MODEL_SCALE } from './modelScale'
import { ballAt, cameraPose, layout, locate, samplePlayer, toWorld, PITCH_LENGTH, PITCH_WIDTH } from './timeline'
import type { Placed, PlayerState } from './timeline'
import type { CameraPreset, ReplayClip, ReplayEvent, ReplayOverlay, ReplayPlayer, ReplaySequence } from './types'
import { COHORTS, CROWD_PALETTE, CROWD_POSE, CrowdFrameGuard, HOLD_DROP, LOW_POSE, STAND_BAND, cheeringSlots, cohortTiming, crowdMotionAllowed, crowdStepDown, envelope, layoutStand, spectatorCheering, spectatorReaction, standEvent, standReaction, variationOf } from './crowd'
import { cheeringSpectator, lowSpectator, neutralSpectator, triangles } from './crowdGeometry'
import type { CohortReaction, CrowdTier, Spectator, StandShape } from './crowd'

export interface SceneColors { home: string; away: string; homeKeeper: string; awayKeeper: string; accent: string }

/**
 * Optional accessibility adaptations (all off by default).
 *  - reducedMotion: no fly-in, no smooth camera sweeps (direct cuts), markers appear without a pop-in.
 *    Player and ball movement is kept - it IS the replay.
 *  - steady: highlights do not pulse (reduce flashing / intense effects).
 *  - shapes: away players get diamond rings and square number badges, so teams differ by shape, not only colour.
 */
export interface SceneOptions { reducedMotion: boolean; steady: boolean; shapes: boolean }

export interface ReplayTick {
  time: number
  duration: number
  playing: boolean
  clipIndex: number
  local: number
  preset: CameraPreset
  userCamera: boolean
  introActive: boolean
  /** Increases on every seek and restart, so listeners can tell a jump from playback however small the time change is. */
  jumps: number
}

interface PlayerRig {
  player: ReplayPlayer
  root: THREE.Group
  model: Footballer
  ring: THREE.Mesh
  ringMat: THREE.MeshBasicMaterial
  glow: THREE.Mesh
  glowMat: THREE.MeshBasicMaterial
  label: THREE.Sprite
  heading: number
}

const RING_OPACITY: Record<string, number> = { ACTUAL_EVENT_POSITION: 0.95, INTERPOLATED_POSITION: 0.55, ESTIMATED_POSITION: 0.22 }

export class ReplayScene {
  private readonly renderer: THREE.WebGLRenderer
  private readonly scene = new THREE.Scene()
  private readonly camera: THREE.PerspectiveCamera
  private readonly controls: OrbitControls
  private readonly clock = new THREE.Clock()
  private readonly disposables: { dispose(): void }[] = []
  private playerResources: { dispose(): void }[] = []
  private readonly playerModels = new PlayerModelLibrary()
  private readonly clipLayer = new THREE.Group()
  private raf = 0

  private seq: ReplaySequence | null = null
  private placed: Placed[] = []
  private total = 0
  private time = 0
  private playing = false
  private speed = 1
  private preset: CameraPreset = 'tactical'
  private userCamera = false
  private intro = 0
  private introDuration = 0
  private currentClip = -1
  private readonly desiredPos = new THREE.Vector3()
  private readonly desiredTarget = new THREE.Vector3()
  private readonly rigs = new Map<string, PlayerRig>()
  private ball!: THREE.Mesh
  private ballTracking!: BallTrackingOverlay
  private ballSettings: BallVisualizationSettings = { ...DEFAULT_BALL_SETTINGS }
  private pulseTimes: number[] = []
  private ballShadow!: THREE.Mesh
  private trail!: BallTrail
  private overlayObjects: { overlay: ReplayOverlay; object: THREE.Object3D; material?: THREE.Material & { opacity: number } }[] = []
  private passLines: { event: ReplayEvent; line: THREE.Line; head: THREE.Mesh }[] = []
  private listeners: ((tick: ReplayTick) => void)[] = []
  private jumps = 0
  private lastNotify = 0
  private readonly onControlStart = () => { this.userCamera = true; this.intro = this.introDuration }

  private readonly canvas: HTMLCanvasElement
  private readonly colors: SceneColors
  private options: SceneOptions
  private cutPending = true

  // Optional decorative crowd, never part of the evidence or the clip layer. Per stand: a neutral InstancedMesh and (NORMAL)
  // a cheering InstancedMesh holding the same spectators in opposite rank order, so a reaction only changes two counts.
  private stands: { group: THREE.Group; shape: StandShape }[] = []
  private crowdMeshes: THREE.InstancedMesh[] = []
  private crowdStands: CrowdStand[] = []
  private crowdTier: CrowdTier = 'off'
  private crowdGuard: CrowdFrameGuard | null = null
  private crowdGuardTripped = false
  private crowdChangeListeners: ((tier: CrowdTier) => void)[] = []
  private reactionEvents: readonly ReplayEvent[] = []
  private reactionLocal = 0

  constructor(canvas: HTMLCanvasElement, colors: SceneColors, options: SceneOptions = { reducedMotion: false, steady: false, shapes: false }) {
    this.canvas = canvas
    this.colors = colors
    this.options = options
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' })
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2))
    this.renderer.outputColorSpace = THREE.SRGBColorSpace
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping
    this.renderer.toneMappingExposure = 1.05
    this.renderer.shadowMap.enabled = true
    this.renderer.shadowMap.type = THREE.PCFShadowMap

    this.scene.background = new THREE.Color('#070b14')
    this.scene.fog = new THREE.Fog('#070b14', 150, 320)

    this.camera = new THREE.PerspectiveCamera(42, 1, 0.5, 600)
    this.camera.position.set(0, 160, 190)
    this.controls = new OrbitControls(this.camera, canvas)
    this.controls.enableDamping = true
    this.controls.dampingFactor = 0.08
    this.controls.maxPolarAngle = Math.PI * 0.47
    this.controls.minDistance = 8
    this.controls.maxDistance = 220
    this.controls.addEventListener('start', this.onControlStart)

    this.buildEnvironment()
    this.buildBall()
    this.scene.add(this.clipLayer)
    this.resize()
    this.loop()
  }

  // ---------------------------------------------------------------- public API

  load(seq: ReplaySequence, flyIn: boolean) {
    this.seq = seq
    const l = layout(seq)
    this.placed = l.placed
    this.total = l.total
    this.time = 0
    this.currentClip = -1
    this.cutPending = true
    this.buildPlayers(seq.players)
    this.introDuration = flyIn && !this.options.reducedMotion ? 1.8 : 0
    this.intro = 0
    this.applyTime(true)
    if (!flyIn) this.snapCamera()
    this.playing = true
  }

  onTick(listener: (tick: ReplayTick) => void) {
    this.listeners.push(listener)
    return () => { this.listeners = this.listeners.filter(l => l !== listener) }
  }

  play() { if (this.time >= this.total - 1e-3) { this.time = 0; this.cutPending = true; this.trail.reset(); this.applyTime(true) } this.playing = true; this.notify(true) }
  pause() { this.playing = false; this.notify(true) }
  toggle() { if (this.playing) this.pause(); else this.play() }
  restart() { this.time = 0; this.playing = true; this.cutPending = true; this.jumps++; this.applyTime(true); this.notify(true) }
  setSpeed(speed: number) { this.speed = speed; this.notify(true) }
  setBallVisualization(settings: BallVisualizationSettings) {
    this.ballSettings = settings
    this.ballTracking.configure(settings)
    this.trail.configure(settings)
    this.applyTime(false)
  }
  seek(t: number) { this.time = Math.min(Math.max(0, t), this.total); this.cutPending = true; this.jumps++; this.applyTime(true); this.notify(true) }

  /** Update motion/flash options live (shape coding is fixed when players are built). */
  setMotion(options: Pick<SceneOptions, 'reducedMotion' | 'steady'>) {
    this.options = { ...this.options, ...options }
    if (options.reducedMotion && this.intro < this.introDuration) this.skipIntro()
    this.cutPending = true
  }

  getTime() { return this.time }

  /** WebGL facts for the crowd's capability check (no other use). */
  capabilities() {
    const gl = this.renderer.getContext()
    let gpu: string | undefined
    try {
      const info = gl.getExtension('WEBGL_debug_renderer_info')
      gpu = String(gl.getParameter(info ? info.UNMASKED_RENDERER_WEBGL : gl.RENDERER) ?? '') || undefined
    } catch { gpu = undefined }
    return { isWebGL2: this.renderer.capabilities.isWebGL2, maxTextureSize: this.renderer.capabilities.maxTextureSize, gpu }
  }

  /**
   * Show or hide the decorative crowd. `guard` runs the one-off frame-time check after the crowd appears: 'auto' for the
   * automatic default, 'explicit' (a lenient budget) when the viewer chose it. If frames are too slow the guard steps the
   * crowd down once (NORMAL → LOW, then LOW → OFF) and tells onCrowdAutoChange listeners.
   */
  setCrowd(tier: CrowdTier, guard: 'auto' | 'explicit' | null) {
    for (const m of this.crowdMeshes) { m.parent?.remove(m); m.geometry.dispose(); (m.material as THREE.Material).dispose() }
    this.crowdMeshes = []
    this.crowdStands = []
    this.crowdTier = tier
    if (guard === 'explicit') this.crowdGuardTripped = false     // the viewer's choice replaces an earlier guard decision
    this.crowdGuard = tier !== 'off' && guard && !this.crowdGuardTripped
      ? new CrowdFrameGuard(guard === 'explicit' ? CrowdFrameGuard.EXPLICIT_BUDGET_MS : CrowdFrameGuard.BUDGET_MS) : null
    this.crowdGuardBudget = guard
    if (tier === 'off') return
    const palette = CROWD_PALETTE.map(c => new THREE.Color(c))
    const neutralBase = tier === 'normal' ? neutralSpectator() : lowSpectator()
    const cheeringBase = tier === 'normal' ? cheeringSpectator() : null
    this.stands.forEach((stand, i) => {
      const seats = layoutStand(stand.shape, tier, 7001 + i)
      if (seats.length === 0) return
      const v = variationOf(i)
      const pose = tier === 'normal' ? CROWD_POSE : LOW_POSE
      const uniforms: CrowdUniforms = {
        uAge: { value: 0 }, uAmp: { value: 0 }, uDur: { value: 1 }, uSpread: { value: 0 }, uCohorts: { value: 0 },
        uCheerShare: { value: v.cheer }, uPose: { value: new THREE.Vector3(pose.stretch, pose.lift, pose.lean) },
      }
      // One fixed order per stand: the spectators who may cheer (rank inside the stand's cheering share), by cohort slot,
      // then everyone else. The cheering slots are always 0..m-1, so the cheering spectators are always the first c of this
      // order: the cheering mesh draws the first c, the neutral mesh (reverse order) the other n - c. Each spectator is
      // drawn exactly once and a reaction changes only two counts - nothing is allocated or updated per spectator.
      const candidates = seats.filter(x => x.rank < v.cheer).sort((a, b) => a.slot - b.slot || a.rank - b.rank)
      const order = [...candidates, ...seats.filter(x => x.rank >= v.cheer)]
      const cheeringUpTo = new Array<number>(COHORTS[tier] + 1).fill(0)
      for (const x of candidates) cheeringUpTo[x.slot + 1]++
      for (let k = 1; k < cheeringUpTo.length; k++) cheeringUpTo[k] += cheeringUpTo[k - 1]
      const neutral = this.spectatorMesh(neutralBase, [...order].reverse(), uniforms, palette, stand.group)
      const cheering = cheeringBase ? this.spectatorMesh(cheeringBase, order, uniforms, palette, stand.group) : null
      if (cheering) { cheering.count = 0; cheering.visible = false }
      this.crowdStands.push({ neutral, cheering, uniforms, n: seats.length, cheeringUpTo, seats, event: null })
    })
    neutralBase.dispose()
    cheeringBase?.dispose()
  }

  private spectatorMesh(base: THREE.BufferGeometry, seats: Spectator[], uniforms: CrowdUniforms, palette: THREE.Color[], parent: THREE.Group) {
    const geometry = base.clone()
    geometry.setAttribute('aRank', new THREE.InstancedBufferAttribute(Float32Array.from(seats, s => s.rank), 1))
    geometry.setAttribute('aSlot', new THREE.InstancedBufferAttribute(Float32Array.from(seats, s => s.slot), 1))
    const mesh = new THREE.InstancedMesh(geometry, crowdMaterial(uniforms), seats.length)
    const matrix = new THREE.Matrix4()
    const quat = new THREE.Quaternion()
    const scale = new THREE.Vector3()
    const pos = new THREE.Vector3()
    seats.forEach((seat, k) => {
      pos.set(seat.x, seat.y, seat.z)
      scale.set(seat.scale, seat.scale, seat.scale)
      mesh.setMatrixAt(k, matrix.compose(pos, quat, scale))
      mesh.setColorAt(k, palette[seat.color])
    })
    mesh.instanceMatrix.needsUpdate = true
    if (mesh.instanceColor) mesh.instanceColor.needsUpdate = true
    mesh.computeBoundingSphere()      // over every seat, so changing the visible count never affects culling
    mesh.castShadow = false
    mesh.receiveShadow = false
    mesh.userData.decorative = true
    parent.add(mesh)
    this.crowdMeshes.push(mesh)
    return mesh
  }

  private crowdGuardBudget: 'auto' | 'explicit' | null = null

  /** Called with the new tier when the frame-time guard reduces the crowd (to LOW or OFF). */
  onCrowdAutoChange(listener: (tier: CrowdTier) => void) {
    this.crowdChangeListeners.push(listener)
    return () => { this.crowdChangeListeners = this.crowdChangeListeners.filter(l => l !== listener) }
  }

  /** Read-only figures for measuring the crowd's cost and reaction (?replayDebug=1). Counts are computed only when asked. */
  crowdStats() {
    const stands = this.crowdStands.map((c, i) => {
      const e = c.event
      let standing = 0
      if (e) for (const seat of c.seats) if (!spectatorCheering(seat, e.reaction, e.age, i) && spectatorReaction(seat, e.reaction, e.age, i) > 0.5) standing++
      const slots = e ? Array.from({ length: e.reaction.cohorts }, (_, k) => k) : []
      const timing = e ? slots.map(k => cohortTiming(e.reaction, k)!) : []
      return {
        event: e?.label ?? null,
        phase: e ? Math.round(e.age * 100) / 100 : null,
        cohorts: COHORTS[this.crowdTier as 'low' | 'normal'] ?? 0,
        reacting: e ? slots.length : 0,
        active: e ? timing.filter(x => envelope(x.duration, e.age - x.onset) > 0).length : 0,
        settling: e ? timing.filter(x => e.age - x.onset > 0.45 * x.duration && e.age - x.onset <= x.duration).length : 0,
        earliestOnset: e ? Math.round((variationOf(i).delay + timing[0].onset) * 1000) : null,
        latestOnset: e ? Math.round((variationOf(i).delay + timing[timing.length - 1].onset) * 1000) : null,
        cheering: c.cheering?.visible ? c.cheering.count : 0,
        standing,
      }
    })
    const cheering = stands.reduce((n, x) => n + x.cheering, 0)
    const standing = stands.reduce((n, x) => n + x.standing, 0)
    const spectators = this.crowdStands.reduce((n, c) => n + c.n, 0)
    return {
      tier: this.crowdTier,
      spectators,
      cheering,
      standing,
      neutral: spectators - cheering - standing,
      stands,
      crowdDrawCalls: this.crowdMeshes.filter(m => m.visible && m.count > 0).length,
      crowdTriangles: this.crowdMeshes.reduce((n, m) => n + (m.visible ? m.count * triangles(m.geometry) : 0), 0),
      reaction: this.crowdStands.map((_, i) => Math.round(standReaction(this.reactionEvents, this.reactionLocal, i, this.crowdTier) * 1000) / 1000),
      cheer: this.crowdStands.map(c => Math.round(((c.cheering?.visible ? c.cheering.count : 0) / c.n) * 1000) / 1000),
      replayTime: this.time,
      frameDrawCalls: this.renderer.info.render.calls,
      triangles: this.renderer.info.render.triangles,
    }
  }
  skipIntro() { this.intro = this.introDuration; this.applyTime(false); this.snapCamera(); this.notify(true) }

  setCamera(preset: CameraPreset) {
    this.preset = preset
    this.userCamera = false
    this.cutPending = true
    this.applyTime(false)
    this.notify(true)
  }

  resetCamera() { this.userCamera = false; this.cutPending = true; this.notify(true) }

  resize() {
    const parent = this.canvas.parentElement
    const w = parent?.clientWidth ?? this.canvas.clientWidth
    const h = parent?.clientHeight ?? this.canvas.clientHeight
    if (w === 0 || h === 0) return
    this.renderer.setSize(w, h, false)
    this.camera.aspect = w / h
    this.camera.updateProjectionMatrix()
  }

  dispose() {
    cancelAnimationFrame(this.raf)
    this.controls.removeEventListener('start', this.onControlStart)
    this.controls.dispose()
    this.listeners = []
    this.clearClipLayer()
    this.clearPlayers()
    this.playerModels.dispose()
    this.scene.remove(this.ballTracking.group)
    this.ballTracking.dispose()
    this.scene.remove(this.trail.line)
    this.trail.dispose()
    this.scene.traverse((o) => {
      const mesh = o as THREE.Mesh
      if (mesh.geometry) mesh.geometry.dispose()
      const mats = Array.isArray(mesh.material) ? mesh.material : mesh.material ? [mesh.material] : []
      for (const m of mats) {
        for (const value of Object.values(m)) if (value instanceof THREE.Texture) value.dispose()
        m.dispose()
      }
    })
    for (const d of this.disposables) d.dispose()
    this.renderer.dispose()
    this.renderer.forceContextLoss()
  }

  // ---------------------------------------------------------------- environment

  private buildEnvironment() {
    this.scene.add(new THREE.HemisphereLight('#c7dcff', '#0d1a12', 0.85))
    const sun = new THREE.DirectionalLight('#ffffff', 1.9)
    sun.position.set(35, 90, 45)
    sun.castShadow = true
    sun.shadow.mapSize.set(2048, 2048)
    const sc = sun.shadow.camera
    sc.left = -70; sc.right = 70; sc.top = 50; sc.bottom = -50; sc.near = 10; sc.far = 220
    sun.shadow.bias = -0.0005
    this.scene.add(sun)

    // Surround and pitch.
    const apron = new THREE.Mesh(new THREE.PlaneGeometry(PITCH_LENGTH + 24, PITCH_WIDTH + 20), new THREE.MeshStandardMaterial({ color: '#123a24', roughness: 1 }))
    apron.rotation.x = -Math.PI / 2
    apron.position.y = -0.02
    apron.receiveShadow = true
    this.scene.add(apron)

    const pitchTexture = new THREE.CanvasTexture(drawPitchCanvas())
    pitchTexture.colorSpace = THREE.SRGBColorSpace
    pitchTexture.anisotropy = this.renderer.capabilities.getMaxAnisotropy()
    const pitch = new THREE.Mesh(new THREE.PlaneGeometry(PITCH_LENGTH + 4, PITCH_WIDTH + 4), new THREE.MeshStandardMaterial({ map: pitchTexture, roughness: 0.95 }))
    pitch.rotation.x = -Math.PI / 2
    pitch.receiveShadow = true
    this.scene.add(pitch)

    for (const side of [-1, 1] as const) this.scene.add(buildGoal(side))
    for (const sx of [-1, 1]) for (const sz of [-1, 1]) this.scene.add(buildCornerFlag(sx * PITCH_LENGTH / 2, sz * PITCH_WIDTH / 2))
    const stadium = buildStadium()
    this.stands = stadium.stands
    this.scene.add(stadium.group)
  }

  private buildBall() {
    this.ball = createFootball()
    this.scene.add(this.ball)
    this.ballTracking = new BallTrackingOverlay(this.ballSettings)
    this.scene.add(this.ballTracking.group)

    this.ballShadow = new THREE.Mesh(new THREE.CircleGeometry(BALL_RADIUS * 1.25, 20), new THREE.MeshBasicMaterial({ color: '#000000', transparent: true, opacity: 0.35, depthWrite: false }))
    this.ballShadow.rotation.x = -Math.PI / 2
    this.ballShadow.position.y = 0.03
    this.scene.add(this.ballShadow)

    this.trail = new BallTrail(this.ballSettings)
    this.scene.add(this.trail.line)
  }

  // ---------------------------------------------------------------- players

  private clearPlayers() {
    for (const rig of this.rigs.values()) {
      this.scene.remove(rig.root)
      rig.root.remove(rig.model.root)
      rig.model.dispose()
      disposeObject(rig.root)
    }
    this.rigs.clear()
    for (const r of this.playerResources) r.dispose()
    this.playerResources = []
  }

  private buildPlayers(players: ReplayPlayer[]) {
    this.clearPlayers()
    const ringGeo = new THREE.RingGeometry(0.62, 0.82, 28)
    // Diamond ring for the away team when shape coding is on (4 segments = a square turned 45°).
    const awayRingGeo = this.options.shapes ? new THREE.RingGeometry(0.66, 0.92, 4) : ringGeo
    const glowGeo = new THREE.RingGeometry(0.9, 1.35, 28)
    this.playerResources.push(ringGeo, glowGeo)
    if (awayRingGeo !== ringGeo) this.playerResources.push(awayRingGeo)

    for (const p of players) {
      const kit = p.isGoalkeeper ? (p.isHome ? this.colors.homeKeeper : this.colors.awayKeeper) : p.isHome ? this.colors.home : this.colors.away
      const root = new THREE.Group()
      const model = this.playerModels.create(p.id, kit, p.shirtNumber, p.isGoalkeeper)
      const ringMat = new THREE.MeshBasicMaterial({ color: kit, transparent: true, opacity: 0.3, depthWrite: false })
      const ring = new THREE.Mesh(p.isHome ? ringGeo : awayRingGeo, ringMat)
      ring.rotation.x = -Math.PI / 2
      ring.position.y = 0.04
      const glowMat = new THREE.MeshBasicMaterial({ color: this.colors.accent, transparent: true, opacity: 0, depthWrite: false })
      const glow = new THREE.Mesh(glowGeo, glowMat)
      glow.rotation.x = -Math.PI / 2
      glow.position.y = 0.05
      const label = makeLabelSprite(String(p.shirtNumber), kit, this.options.shapes && !p.isHome)
      label.position.y = 2.35
      root.add(model.root, ring, glow, label)
      // Slightly larger than life so players read clearly from the tactical camera (broadcast-graphics convention).
      root.scale.setScalar(REPLAY_MODEL_SCALE)
      this.scene.add(root)
      this.rigs.set(p.id, { player: p, root, model, ring, ringMat, glow, glowMat, label, heading: p.isHome ? Math.PI / 2 : -Math.PI / 2 })
    }
    for (const r of this.playerResources) sharedResources.add(r)
  }

  // ---------------------------------------------------------------- per-clip overlays

  private clearClipLayer() {
    for (const child of [...this.clipLayer.children]) {
      this.clipLayer.remove(child)
      disposeObject(child)
    }
    this.overlayObjects = []
    this.passLines = []
  }

  private buildClipLayer(clip: ReplayClip) {
    this.clearClipLayer()
    for (const rig of this.rigs.values()) rig.root.visible = false
    for (const track of clip.tracks) {
      const rig = this.rigs.get(track.playerId)
      if (rig) rig.root.visible = true
    }

    const teamColor = (teamId?: string) => (teamId === this.seq?.homeTeamId ? this.colors.home : this.colors.away)
    for (const overlay of clip.overlays) {
      if ((overlay.kind === 'Channel' || overlay.kind === 'Zone') && overlay.x0 != null && overlay.x1 != null && overlay.y0 != null && overlay.y1 != null) {
        const a = toWorld(overlay.x0, overlay.y0)
        const b = toWorld(overlay.x1, overlay.y1)
        const w = Math.abs(b.x - a.x)
        const d = Math.abs(b.z - a.z)
        const material = new THREE.MeshBasicMaterial({
          color: overlay.kind === 'Channel' ? this.colors.accent : teamColor(overlay.teamId),
          transparent: true, opacity: 0, depthWrite: false,
        })
        const plane = new THREE.Mesh(new THREE.PlaneGeometry(w, d), material)
        plane.rotation.x = -Math.PI / 2
        plane.position.set((a.x + b.x) / 2, overlay.kind === 'Channel' ? 0.06 : 0.05, (a.z + b.z) / 2)
        const edges = new THREE.LineSegments(new THREE.EdgesGeometry(new THREE.PlaneGeometry(w, d)),
          new THREE.LineBasicMaterial({ color: overlay.kind === 'Channel' ? this.colors.accent : '#ffffff', transparent: true, opacity: 0.6 }))
        edges.rotation.x = -Math.PI / 2
        edges.position.copy(plane.position)
        edges.position.y += 0.01
        const group = new THREE.Group()
        group.add(plane, edges)
        this.clipLayer.add(group)
        this.overlayObjects.push({ overlay, object: group, material })
      } else if (overlay.kind === 'Marker' && overlay.x0 != null && overlay.y0 != null) {
        const p = toWorld(overlay.x0, overlay.y0)
        const color = overlay.label === 'GOAL' ? '#ffffff' : this.colors.accent
        const material = new THREE.MeshBasicMaterial({ color, transparent: true, opacity: 0.9, depthWrite: false, side: THREE.DoubleSide })
        const ring = new THREE.Mesh(new THREE.RingGeometry(1.2, 1.55, 36), material)
        ring.rotation.x = -Math.PI / 2
        ring.position.set(0, 0.07, 0)
        const beam = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.06, 6, 8), new THREE.MeshBasicMaterial({ color, transparent: true, opacity: 0.55 }))
        beam.position.set(0, 3, 0)
        // The group sits at the marker so the pop-in scale is about the marker, not the scene origin.
        const group = new THREE.Group()
        group.position.set(p.x, 0, p.z)
        group.add(ring, beam)
        group.visible = false
        this.clipLayer.add(group)
        this.overlayObjects.push({ overlay, object: group, material })
      } else if (overlay.kind === 'PassPath') {
        const color = teamColor(overlay.teamId)
        for (const id of overlay.eventIds) {
          const event = clip.events.find(e => e.eventId === id)
          if (!event || event.x == null || event.y == null || event.endX == null || event.endY == null) continue
          const geometry = new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(), new THREE.Vector3()])
          const line = new THREE.Line(geometry, new THREE.LineBasicMaterial({ color, transparent: true, opacity: 0.9 }))
          line.frustumCulled = false
          const head = new THREE.Mesh(new THREE.ConeGeometry(0.45, 1.1, 10), new THREE.MeshBasicMaterial({ color }))
          head.visible = false
          line.visible = false
          this.clipLayer.add(line, head)
          this.passLines.push({ event, line, head })
        }
      }
    }
  }

  // ---------------------------------------------------------------- animation

  private loop = () => {
    this.raf = requestAnimationFrame(this.loop)
    // Playback follows the real clock even at low frame rates; only very long gaps (tab switches) are capped.
    const elapsed = Math.min(this.clock.getDelta(), 0.5)
    const dt = Math.min(elapsed, 0.1)
    if (this.seq) {
      if (this.intro < this.introDuration) this.intro += elapsed
      else if (this.playing) {
        this.time += elapsed * this.speed
        if (this.time >= this.total) { this.time = this.total; this.playing = false }
      }
      this.applyTime(false, dt)
      this.updateCamera(dt)
      this.notify(false)
    }
    this.updateCrowd(elapsed)
    this.controls.update()
    this.ballTracking.ringFacing(this.camera)
    this.renderer.render(this.scene, this.camera)
  }

  private updateCrowd(elapsed: number) {
    if (this.crowdMeshes.length === 0) return   // OFF: no spectators and no crowd work at all
    if (this.crowdGuard && !(typeof document !== 'undefined' && document.hidden) && this.crowdGuard.feed(elapsed)) {
      // Too slow: step down once (NORMAL → LOW, re-checked once; LOW → OFF). Never upward, so it cannot oscillate.
      const next = crowdStepDown(this.crowdTier)
      const budget = this.crowdGuardBudget
      this.crowdGuard = null
      if (next === 'off') this.crowdGuardTripped = true
      this.setCrowd(next, next === 'off' ? null : budget)
      for (const l of this.crowdChangeListeners) l(next)
      return
    }
    // Reactions from the replay clock: per stand, the driving event and its age go to the GPU, which works out every
    // spectator's own cohort phase (onset, rise, hold, staggered settle). NORMAL also shows the cheering cohorts by changing
    // two instance counts. Reduced motion and a tripped guard skip all of it: everyone stays seated and neutral.
    const moving = crowdMotionAllowed(this.crowdTier, this.options.reducedMotion, this.crowdGuardTripped)
    for (let i = 0; i < this.crowdStands.length; i++) {
      const c = this.crowdStands[i]
      const e = moving ? standEvent(this.reactionEvents, this.reactionLocal, i, this.crowdTier) : null
      c.event = e
      const u = c.uniforms
      u.uAge.value = e ? e.age : 0
      u.uAmp.value = e ? variationOf(i).strength * e.reaction.amplitude : 0
      u.uDur.value = e ? e.reaction.duration : 1
      u.uSpread.value = e ? e.reaction.spread : 0
      u.uCohorts.value = e ? e.reaction.cohorts : 0
      const cheering = c.cheering && e ? c.cheeringUpTo[Math.min(cheeringSlots(e.reaction, e.age), c.cheeringUpTo.length - 1)] : 0
      c.neutral.count = c.n - cheering
      c.neutral.visible = c.neutral.count > 0
      if (c.cheering) { c.cheering.count = cheering; c.cheering.visible = cheering > 0 }
    }
  }

  private applyTime(force: boolean, dt = 0) {
    if (!this.seq || this.placed.length === 0) return
    const { placed, local } = locate(this.placed, this.time)
    const clip = placed.clip
    this.reactionEvents = clip.events
    this.reactionLocal = local
    if (placed.index !== this.currentClip) {
      this.currentClip = placed.index
      this.buildClipLayer(clip)
      this.pulseTimes = keyEventTimes(this.seq, clip)
      this.trail.reset()
      this.cutPending = true
      force = true
    }

    const states = new Map<string, PlayerState>()
    for (const track of clip.tracks) states.set(track.playerId, samplePlayer(track, local))

    const ball = ballAt(clip, local, id => states.get(id))
    const bw = toWorld(ball.x, ball.y)
    const ballScale = BALL_SIZE_SCALE[this.ballSettings.size]
    this.ball.position.set(bw.x, BALL_RADIUS * ballScale + ball.z, bw.z)
    this.ball.scale.setScalar(ballScale)
    this.ballTracking.group.position.copy(this.ball.position)
    const eventPulse = this.ballSettings.pulse && !force && !this.options.steady && !this.options.reducedMotion ? keyEventPulse(this.pulseTimes, local) : 0
    this.ballTracking.update(eventPulse, this.options.steady || this.options.reducedMotion)
    if (ball.segment) {
      const origin = toWorld(ball.segment.x0, ball.segment.y0)
      orientFootball(this.ball, bw.x - origin.x, bw.z - origin.z, ballScale)
    } else this.ball.quaternion.identity()
    this.ballShadow.position.set(bw.x, 0.03, bw.z)
    const shadowScale = 1 + ball.z * 0.15
    this.ballShadow.scale.set(shadowScale, shadowScale, shadowScale)

    // Time-bounded history sampled only from the replay's already-reached ball segments.
    this.trail.update(clip, local, this.playing, force)

    // Current actor and receiver.
    const active = [...clip.events].reverse().find(e => e.time <= local + 1e-6 && local - Math.max(e.time, e.endTime) < 1.2)
    const pressing = new Set(clip.overlays.filter(o => o.kind === 'PressingPlayers' && local >= o.fromTime).flatMap(o => o.playerIds))
    // Reduce flashing: a steady highlight instead of a pulse.
    const pulse = this.options.steady ? 0.7 : 0.5 + 0.5 * Math.sin(performance.now() / 220)

    for (const track of clip.tracks) {
      const rig = this.rigs.get(track.playerId)
      const s = states.get(track.playerId)
      if (!rig || !s) continue
      const w = toWorld(s.x, s.y)
      rig.root.position.set(w.x, 0, w.z)
      // Velocity in world metres per second.
      const vx = s.vx * PITCH_LENGTH / 100
      const vz = s.vy * PITCH_WIDTH / 100
      const speed = Math.hypot(vx, vz)
      let heading = rig.heading
      if (speed > 0.4) heading = Math.atan2(vx, vz)
      else heading = Math.atan2(bw.x - w.x, bw.z - w.z)
      rig.heading = lerpAngle(rig.heading, heading, force ? 1 : Math.min(1, dt * 8))
      rig.root.rotation.y = rig.heading
      this.playerModels.update(rig.model, local, speed, this.camera.position.distanceTo(rig.root.position))

      const isActor = active && (active.playerId === rig.player.id || (active.relatedPlayerId === rig.player.id && active.type === 'Pass' && active.outcome === 'Complete'))
      rig.ringMat.opacity = isActor ? 1 : RING_OPACITY[s.provenance] ?? 0.3
      rig.ringMat.color.set(isActor ? '#ffffff' : rig.player.isGoalkeeper ? (rig.player.isHome ? this.colors.homeKeeper : this.colors.awayKeeper) : rig.player.isHome ? this.colors.home : this.colors.away)
      const highlighted = pressing.has(rig.player.id) || isActor
      rig.glowMat.opacity = highlighted ? 0.35 + 0.45 * pulse : 0
      rig.glowMat.color.set(isActor ? '#ffffff' : this.colors.accent)
      ;(rig.label.material as THREE.SpriteMaterial).opacity = isActor || this.seq.relevantPlayerIds.includes(rig.player.id) ? 1 : 0.55
    }

    // Overlays fade in; markers appear at their event.
    for (const o of this.overlayObjects) {
      if (o.overlay.kind === 'Marker') {
        o.object.visible = local >= o.overlay.fromTime
        const k = this.options.reducedMotion ? 1 : Math.min(1, (local - o.overlay.fromTime) * 2.5)
        o.object.scale.setScalar(Math.max(0.01, k))
      } else if (o.material) {
        const base = o.overlay.kind === 'Channel' ? 0.2 : 0.09
        o.material.opacity = base * Math.min(1, local / 0.8) * (0.85 + 0.15 * pulse)
      }
    }

    // Pass routes are drawn as the ball travels and stay on the pitch afterwards.
    for (const { event, line, head } of this.passLines) {
      if (local < event.time) { line.visible = false; head.visible = false; continue }
      const f = event.endTime <= event.time ? 1 : Math.min(1, (local - event.time) / (event.endTime - event.time))
      const a = toWorld(event.x!, event.y!)
      const b = toWorld(event.endX!, event.endY!)
      const end = new THREE.Vector3(a.x + (b.x - a.x) * f, 0.09, a.z + (b.z - a.z) * f)
      line.geometry.setFromPoints([new THREE.Vector3(a.x, 0.09, a.z), end])
      line.visible = true
      head.visible = f >= 1
      if (f >= 1) {
        // Cone points +Y; tip it onto the ground (+Z) and turn it towards the pass direction.
        head.position.set(b.x, 0.12, b.z)
        head.rotation.order = 'YXZ'
        head.rotation.set(Math.PI / 2, Math.atan2(b.x - a.x, b.z - a.z), 0)
      }
    }

    // Camera desired pose for the current preset.
    const focusId = clip.camera.focusPlayerId
    const focus = focusId && states.get(focusId) ? toWorld(states.get(focusId)!.x, states.get(focusId)!.y) : undefined
    const pose = cameraPose(this.preset, { x: bw.x, z: bw.z }, clip.camera.attackDirection, focus)
    this.desiredPos.set(pose.position.x, pose.position.y, pose.position.z)
    this.desiredTarget.set(pose.target.x, pose.target.y, pose.target.z)
  }

  private snapCamera() {
    this.camera.position.copy(this.desiredPos)
    this.controls.target.copy(this.desiredTarget)
  }

  private updateCamera(dt: number) {
    if (this.userCamera) return
    if (this.options.reducedMotion) {
      // Direct cuts instead of sweeping pans: hold the shot, and cut only on a preset/clip change, a seek, or when
      // the action is about to leave the frame.
      if (this.cutPending || this.camera.position.distanceTo(this.desiredPos) > 22 || this.controls.target.distanceTo(this.desiredTarget) > 18) {
        this.snapCamera()
        this.cutPending = false
      }
      return
    }
    if (this.preset === 'ballCam' && this.cutPending) {
      this.snapCamera()
      this.cutPending = false
      return
    }
    if (this.intro < this.introDuration) {
      // Fly-in: ease from the high establishing shot to the selected preset.
      const k = easeInOut(Math.min(1, this.intro / this.introDuration))
      this.camera.position.lerpVectors(new THREE.Vector3(0, 160, 190), this.desiredPos, k)
      this.controls.target.lerpVectors(new THREE.Vector3(0, 0, 0), this.desiredTarget, k)
      return
    }
    const positionEase = 1 - Math.exp(-dt * (this.preset === 'ballCam' ? 4.1 : 2.6))
    const targetEase = 1 - Math.exp(-dt * (this.preset === 'ballCam' ? 5 : 2.6))
    this.camera.position.lerp(this.desiredPos, positionEase)
    this.controls.target.lerp(this.desiredTarget, targetEase)
  }

  private notify(force: boolean) {
    const now = performance.now()
    if (!force && now - this.lastNotify < 90) return
    this.lastNotify = now
    if (!this.seq || this.placed.length === 0) return
    const { placed, local } = locate(this.placed, this.time)
    const tick: ReplayTick = {
      time: this.time, duration: this.total, playing: this.playing, clipIndex: placed.index, local,
      preset: this.preset, userCamera: this.userCamera, introActive: this.intro < this.introDuration, jumps: this.jumps,
    }
    for (const l of this.listeners) l(tick)
  }
}

// ------------------------------------------------------------------ helpers

function easeInOut(t: number) { return t < 0.5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2 }

function lerpAngle(a: number, b: number, t: number) {
  let d = b - a
  while (d > Math.PI) d -= Math.PI * 2
  while (d < -Math.PI) d += Math.PI * 2
  return a + d * t
}

function disposeObject(root: THREE.Object3D) {
  root.traverse((o) => {
    const mesh = o as THREE.Mesh
    if (mesh.geometry && !isShared(mesh.geometry)) mesh.geometry.dispose()
    const mats = Array.isArray(mesh.material) ? mesh.material : mesh.material ? [mesh.material] : []
    for (const m of mats) {
      if (isShared(m)) continue
      for (const value of Object.values(m)) if (value instanceof THREE.Texture) value.dispose()
      m.dispose()
    }
  })
}

// Shared geometries/materials are registered on the scene's disposables and freed on dispose().
const sharedResources = new WeakSet<object>()
function isShared(o: object) { return sharedResources.has(o) }

/** Shirt-number badge rendered to a small canvas texture. */
function makeLabelSprite(text: string, color: string, square = false) {
  const canvas = document.createElement('canvas')
  canvas.width = 128
  canvas.height = 128
  const ctx = canvas.getContext('2d')!
  ctx.fillStyle = 'rgba(8,12,20,0.85)'
  ctx.beginPath()
  if (square) ctx.roundRect(14, 14, 100, 100, 10)
  else ctx.arc(64, 64, 52, 0, Math.PI * 2)
  ctx.fill()
  ctx.lineWidth = 8
  ctx.strokeStyle = color
  ctx.stroke()
  ctx.fillStyle = '#ffffff'
  ctx.font = 'bold 60px "Barlow Condensed", Arial, sans-serif'
  ctx.textAlign = 'center'
  ctx.textBaseline = 'middle'
  ctx.fillText(text, 64, 68)
  const texture = new THREE.CanvasTexture(canvas)
  texture.colorSpace = THREE.SRGBColorSpace
  const sprite = new THREE.Sprite(new THREE.SpriteMaterial({ map: texture, transparent: true, depthWrite: false }))
  sprite.scale.set(1.25, 1.25, 1.25)
  sprite.renderOrder = 10
  return sprite
}

/** Pitch markings drawn to a canvas at 20 px per metre (stripes, lines, arcs, spots). */
function drawPitchCanvas() {
  const ppm = 20
  const margin = 2
  const W = (PITCH_LENGTH + margin * 2) * ppm
  const H = (PITCH_WIDTH + margin * 2) * ppm
  const canvas = document.createElement('canvas')
  canvas.width = W
  canvas.height = H
  const c = canvas.getContext('2d')!
  const stripes = 12
  for (let i = 0; i < stripes; i++) {
    c.fillStyle = i % 2 === 0 ? '#1f6b3b' : '#1a5f34'
    c.fillRect((margin + (i * PITCH_LENGTH) / stripes) * ppm, 0, (PITCH_LENGTH / stripes) * ppm + 1, H)
  }
  c.fillStyle = '#1a5f34'
  c.fillRect(0, 0, margin * ppm, H)
  c.fillRect(W - margin * ppm, 0, margin * ppm, H)

  const X = (m: number) => (margin + m) * ppm
  const Y = (m: number) => (margin + m) * ppm
  c.strokeStyle = 'rgba(245,250,245,0.92)'
  c.fillStyle = 'rgba(245,250,245,0.92)'
  c.lineWidth = 0.12 * ppm
  const L = PITCH_LENGTH
  const Wd = PITCH_WIDTH
  c.strokeRect(X(0), Y(0), L * ppm, Wd * ppm)
  c.beginPath(); c.moveTo(X(L / 2), Y(0)); c.lineTo(X(L / 2), Y(Wd)); c.stroke()
  c.beginPath(); c.arc(X(L / 2), Y(Wd / 2), 9.15 * ppm, 0, Math.PI * 2); c.stroke()
  const spot = (x: number, y: number) => { c.beginPath(); c.arc(X(x), Y(y), 0.22 * ppm, 0, Math.PI * 2); c.fill() }
  spot(L / 2, Wd / 2)
  for (const side of [0, 1]) {
    const dir = side === 0 ? 1 : -1
    const gx = side === 0 ? 0 : L
    const box = (depth: number, width: number) => {
      const x = side === 0 ? gx : gx - depth
      c.strokeRect(X(x), Y((Wd - width) / 2), depth * ppm, width * ppm)
    }
    box(16.5, 40.32)
    box(5.5, 18.32)
    spot(gx + dir * 11, Wd / 2)
    // Penalty arc: the part of the 9.15 m circle outside the area.
    const a = Math.acos(5.5 / 9.15)
    c.beginPath()
    if (side === 0) c.arc(X(11), Y(Wd / 2), 9.15 * ppm, -a, a)
    else c.arc(X(L - 11), Y(Wd / 2), 9.15 * ppm, Math.PI - a, Math.PI + a)
    c.stroke()
  }
  // Corner arcs.
  for (const [cx, cy, s0] of [[0, 0, 0], [L, 0, Math.PI / 2], [L, Wd, Math.PI], [0, Wd, Math.PI * 1.5]] as const) {
    c.beginPath(); c.arc(X(cx), Y(cy), 1 * ppm, s0, s0 + Math.PI / 2); c.stroke()
  }
  return canvas
}

function buildGoal(side: -1 | 1) {
  const group = new THREE.Group()
  const post = new THREE.MeshStandardMaterial({ color: '#f4f6f8', roughness: 0.4 })
  const half = 7.32 / 2
  const h = 2.44
  const x = side * PITCH_LENGTH / 2
  const postGeo = new THREE.CylinderGeometry(0.07, 0.07, h, 10)
  for (const z of [-half, half]) {
    const m = new THREE.Mesh(postGeo, post)
    m.position.set(x, h / 2, z)
    m.castShadow = true
    group.add(m)
  }
  const bar = new THREE.Mesh(new THREE.CylinderGeometry(0.07, 0.07, half * 2, 10), post)
  bar.rotation.x = Math.PI / 2
  bar.position.set(x, h, 0)
  bar.castShadow = true
  group.add(bar)

  // Net: a light wire frame behind the goal.
  const depth = 2
  const points: THREE.Vector3[] = []
  const back = x + side * depth
  for (let i = 0; i <= 12; i++) {
    const z = -half + (i / 12) * half * 2
    points.push(new THREE.Vector3(x, h, z), new THREE.Vector3(back, h * 0.75, z))
    points.push(new THREE.Vector3(back, h * 0.75, z), new THREE.Vector3(back, 0, z))
  }
  for (let j = 0; j <= 5; j++) {
    const y = (j / 5) * h * 0.75
    points.push(new THREE.Vector3(back, y, -half), new THREE.Vector3(back, y, half))
  }
  for (const z of [-half, half]) points.push(new THREE.Vector3(x, h, z), new THREE.Vector3(back, h * 0.75, z), new THREE.Vector3(back, 0, z), new THREE.Vector3(x, 0, z))
  const net = new THREE.LineSegments(new THREE.BufferGeometry().setFromPoints(points), new THREE.LineBasicMaterial({ color: '#dfe6ee', transparent: true, opacity: 0.45 }))
  group.add(net)
  return group
}

function buildCornerFlag(x: number, z: number) {
  const group = new THREE.Group()
  const pole = new THREE.Mesh(new THREE.CylinderGeometry(0.03, 0.03, 1.5, 6), new THREE.MeshStandardMaterial({ color: '#f2f2f2' }))
  pole.position.set(x, 0.75, z)
  const flag = new THREE.Mesh(new THREE.PlaneGeometry(0.45, 0.32), new THREE.MeshStandardMaterial({ color: '#39e6b0', side: THREE.DoubleSide }))
  flag.position.set(x + 0.22 * Math.sign(-x || 1), 1.32, z)
  group.add(pole, flag)
  return group
}

/** Lightweight generic stadium: four stands with a speckled crowd texture and floodlight towers. No likeness of any real venue. */
function buildStadium(): { group: THREE.Group; stands: { group: THREE.Group; shape: StandShape }[] } {
  const group = new THREE.Group()
  const stands: { group: THREE.Group; shape: StandShape }[] = []
  const crowdCanvas = document.createElement('canvas')
  crowdCanvas.width = 512
  crowdCanvas.height = 128
  const cc = crowdCanvas.getContext('2d')!
  cc.fillStyle = '#151b27'
  cc.fillRect(0, 0, 512, 128)
  for (let i = 0; i < 2600; i++) {
    const shade = 40 + Math.floor(Math.random() * 90)
    cc.fillStyle = `rgb(${shade},${shade + 6},${shade + 18})`
    cc.fillRect(Math.random() * 512, Math.random() * 128, 2, 2)
  }
  const crowd = new THREE.CanvasTexture(crowdCanvas)
  crowd.colorSpace = THREE.SRGBColorSpace
  crowd.wrapS = THREE.RepeatWrapping
  const standMat = new THREE.MeshStandardMaterial({ map: crowd, roughness: 1 })
  const roofMat = new THREE.MeshStandardMaterial({ color: '#0c111b', roughness: 1 })

  const stand = (length: number, depth: number, height: number) => {
    const shape = new THREE.Shape()
    shape.moveTo(0, 0)
    shape.lineTo(depth, height)
    shape.lineTo(depth, height + 1.5)
    shape.lineTo(0, 1.2)
    shape.lineTo(0, 0)
    const geo = new THREE.ExtrudeGeometry(shape, { depth: length, bevelEnabled: false })
    geo.translate(0, 0, -length / 2)
    const mesh = new THREE.Mesh(geo, standMat)
    const roof = new THREE.Mesh(new THREE.BoxGeometry(depth * 0.8, 0.5, length), roofMat)
    roof.position.set(depth * 0.8, height + 4, 0)
    const rim = new THREE.Mesh(new THREE.BoxGeometry(0.3, 0.35, length), new THREE.MeshBasicMaterial({ color: '#1d6b55' }))
    rim.position.set(0.2, 1.4, 0)
    const g = new THREE.Group()
    g.add(mesh, roof, rim)
    stands.push({ group: g, shape: { length, depth, height } })
    return g
  }
  const sideStand = (zSign: number) => {
    const s = stand(PITCH_LENGTH + 20, 18, 9)
    s.rotation.y = zSign > 0 ? -Math.PI / 2 : Math.PI / 2
    s.position.set(0, 0, zSign * (PITCH_WIDTH / 2 + 10))
    return s
  }
  const endStand = (xSign: number) => {
    const s = stand(PITCH_WIDTH + 14, 15, 8)
    s.rotation.y = xSign > 0 ? 0 : Math.PI
    s.position.set(xSign * (PITCH_LENGTH / 2 + 12), 0, 0)
    return s
  }
  group.add(sideStand(-1), sideStand(1), endStand(-1), endStand(1))

  const towerMat = new THREE.MeshStandardMaterial({ color: '#2a3344' })
  const lampMat = new THREE.MeshBasicMaterial({ color: '#fffbe8' })
  for (const sx of [-1, 1]) for (const sz of [-1, 1]) {
    const tower = new THREE.Mesh(new THREE.CylinderGeometry(0.5, 0.7, 34, 8), towerMat)
    tower.position.set(sx * (PITCH_LENGTH / 2 + 20), 17, sz * (PITCH_WIDTH / 2 + 20))
    const lamp = new THREE.Mesh(new THREE.BoxGeometry(5, 3, 1), lampMat)
    lamp.position.set(tower.position.x, 35, tower.position.z)
    lamp.lookAt(0, 0, 0)
    group.add(tower, lamp)
  }
  return { group, stands }
}

interface CrowdUniforms {
  uAge: { value: number }; uAmp: { value: number }; uDur: { value: number }; uSpread: { value: number }; uCohorts: { value: number }
  uCheerShare: { value: number }; uPose: { value: THREE.Vector3 }
}
interface CrowdStand {
  neutral: THREE.InstancedMesh; cheering: THREE.InstancedMesh | null; uniforms: CrowdUniforms; n: number
  /** cheeringUpTo[m] = how many of the stand's spectators cheer when slots 0..m-1 cheer. */
  cheeringUpTo: number[]
  seats: Spectator[]
  event: { label: string; reaction: CohortReaction; age: number } | null
}

/**
 * Spectator material (cheap Lambert). The vertex shader computes each spectator's own reaction from its cohort slot and
 * rank plus a few numbers per stand (the driving event's age, strength, duration, onset spread and cohort count) - the same
 * arithmetic as cohortTiming/envelope/spectatorReaction in crowd.ts. So cohorts start at different times and settle at
 * different times with no per-spectator CPU work. Heads and raised arms get a skin tone. No textures, shadows or emissive.
 */
function crowdMaterial(uniforms: CrowdUniforms): THREE.MeshLambertMaterial {
  const material = new THREE.MeshLambertMaterial({ color: '#ffffff' })
  material.onBeforeCompile = (shader) => {
    Object.assign(shader.uniforms, uniforms)
    shader.vertexShader = `uniform float uAge;
uniform float uAmp;
uniform float uDur;
uniform float uSpread;
uniform float uCohorts;
uniform float uCheerShare;
uniform vec3 uPose;
attribute float aRank;
attribute float aSlot;
attribute float aHead;
float crowdEnvelope(float d, float a) {
  if (a < 0.0 || a > d) return 0.0;
  float rise = min(0.25, d * 0.25);
  float hold = d * 0.45;
  if (a < rise) return a / rise;
  if (a < hold) return 1.0;
  float x = clamp((a - hold) / (d - hold), 0.0, 1.0);
  return 1.0 - x * x * (3.0 - 2.0 * x);
}
` + shader.vertexShader
      .replace('#include <color_vertex>', `#include <color_vertex>
      #if defined( USE_COLOR ) || defined( USE_INSTANCING_COLOR )
        vColor.xyz = mix(vColor.xyz, mix(vec3(0.86, 0.70, 0.58), vec3(0.42, 0.30, 0.22), fract(aRank * 13.7)), aHead);
      #endif`)
      .replace('#include <begin_vertex>', `#include <begin_vertex>
      float crowdLast = max(1.0, uCohorts - 1.0);
      float crowdTakesPart = step(aSlot + 0.5, uCohorts);
      float crowdOnset = uCohorts > 1.0 ? aSlot * uSpread / crowdLast : 0.0;
      float crowdDur = uDur * (1.0 - ${HOLD_DROP.toFixed(3)} * aSlot / crowdLast);
      float crowdBand = 1.0 - ${(1 - STAND_BAND.rest).toFixed(3)} * smoothstep(uCheerShare + ${STAND_BAND.full.toFixed(3)}, uCheerShare + ${STAND_BAND.fade.toFixed(3)}, aRank);
      float crowdStand = uAmp * crowdTakesPart * crowdEnvelope(crowdDur, uAge - crowdOnset) * crowdBand;
      float crowdH = max(transformed.y, 0.0);
      transformed.y = crowdH * (1.0 + uPose.x * crowdStand) + uPose.y * crowdStand;
      transformed.x -= uPose.z * crowdStand * crowdH;`)
  }
  material.customProgramCacheKey = () => 'matchshift-crowd-v3'
  return material
}

