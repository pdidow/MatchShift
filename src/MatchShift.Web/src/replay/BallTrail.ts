import * as THREE from 'three'
import { BALL_COLORS, BALL_SIZE_SCALE, BALL_TRAIL_SECONDS } from './ballVisualization'
import type { BallVisualizationSettings } from './ballVisualization'
import { BALL_RADIUS } from './ballModel'
import { ballAt, samplePlayer, toWorld } from './timeline'
import type { ReplayClip } from './types'

const CAPACITY = 80
const SAMPLE_STEP = 0.065

/** A bounded, reusable line buffer of already-reached replay positions. No future trajectory is drawn. */
export class BallTrail {
  readonly line: THREE.Line<THREE.BufferGeometry, THREE.ShaderMaterial>
  private readonly positions = new Float32Array(CAPACITY * 3)
  private readonly alphas = new Float32Array(CAPACITY)
  private readonly sampleTime = new Float32Array(CAPACITY)
  private readonly samplePosition = new Float32Array(CAPACITY * 3)
  private count = 0
  private lastLocal = -1
  private settings: BallVisualizationSettings

  constructor(settings: BallVisualizationSettings) {
    this.settings = settings
    const geometry = new THREE.BufferGeometry()
    geometry.setAttribute('position', new THREE.BufferAttribute(this.positions, 3).setUsage(THREE.DynamicDrawUsage))
    geometry.setAttribute('opacity', new THREE.BufferAttribute(this.alphas, 1).setUsage(THREE.DynamicDrawUsage))
    geometry.setDrawRange(0, 0)
    const material = new THREE.ShaderMaterial({
      transparent: true, depthWrite: false,
      uniforms: { tint: { value: new THREE.Color(BALL_COLORS[settings.color]) } },
      vertexShader: 'attribute float opacity; varying float vOpacity; void main() { vOpacity = opacity; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
      fragmentShader: 'uniform vec3 tint; varying float vOpacity; void main() { gl_FragColor = vec4(tint, vOpacity); }',
    })
    this.line = new THREE.Line(geometry, material)
    this.line.frustumCulled = false
    this.line.renderOrder = 3
  }

  configure(settings: BallVisualizationSettings) {
    if (settings.trail !== this.settings.trail || settings.size !== this.settings.size) this.reset()
    this.settings = settings
    this.line.material.uniforms.tint.value.set(BALL_COLORS[settings.color])
    this.line.visible = settings.trail !== 'off'
  }

  reset() {
    this.count = 0
    this.lastLocal = -1
    this.line.geometry.setDrawRange(0, 0)
  }

  update(clip: ReplayClip, local: number, playing: boolean, force: boolean) {
    const duration = BALL_TRAIL_SECONDS[this.settings.trail]
    if (force || local < this.lastLocal - 1e-4 || local - this.lastLocal > 0.35) this.reset()
    if (duration === 0) { this.lastLocal = local; return }
    if (playing && local > this.lastLocal + 1e-4) {
      const start = this.lastLocal < 0 ? local : this.lastLocal
      for (let t = start; t <= local + 1e-5; t += SAMPLE_STEP) this.addSample(clip, Math.min(t, local))
      if (local > start) this.addSample(clip, local)
    }
    this.lastLocal = local
    while (this.count && local - this.sampleTime[0] > duration) {
      this.sampleTime.copyWithin(0, 1, this.count)
      this.samplePosition.copyWithin(0, 3, this.count * 3)
      this.count--
    }
    for (let i = 0; i < this.count; i++) {
      const offset = i * 3
      this.positions[offset] = this.samplePosition[offset]
      this.positions[offset + 1] = this.samplePosition[offset + 1]
      this.positions[offset + 2] = this.samplePosition[offset + 2]
      this.alphas[i] = Math.max(0, (1 - (local - this.sampleTime[i]) / duration) * 0.6)
    }
    this.line.geometry.setDrawRange(0, Math.max(0, this.count))
    this.line.geometry.getAttribute('position').needsUpdate = true
    this.line.geometry.getAttribute('opacity').needsUpdate = true
  }

  private addSample(clip: ReplayClip, t: number) {
    const ball = ballAt(clip, t, id => {
      const track = clip.tracks.find(track => track.playerId === id)
      return track ? samplePlayer(track, t) : undefined
    })
    const world = toWorld(ball.x, ball.y)
    const x = world.x, y = BALL_RADIUS * BALL_SIZE_SCALE[this.settings.size] + ball.z, z = world.z
    if (this.count) {
      const prev = (this.count - 1) * 3
      const distance = Math.hypot(x - this.samplePosition[prev], y - this.samplePosition[prev + 1], z - this.samplePosition[prev + 2])
      if (distance > 7) this.count = 0 // a discontinuity must never draw across the pitch
      else if (distance < 0.035) return
    }
    if (this.count === CAPACITY) {
      this.sampleTime.copyWithin(0, 1)
      this.samplePosition.copyWithin(0, 3)
      this.count--
    }
    const at = this.count++
    this.sampleTime[at] = t
    this.samplePosition[at * 3] = x
    this.samplePosition[at * 3 + 1] = y
    this.samplePosition[at * 3 + 2] = z
  }

  dispose() { this.line.geometry.dispose(); this.line.material.dispose() }
}
