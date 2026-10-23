import * as THREE from 'three'
import { BALL_RADIUS } from './ballModel'
import { BALL_COLORS, BALL_SIZE_SCALE } from './ballVisualization'
import type { BallVisualizationSettings } from './ballVisualization'

/** Ball-attached graphics only. The source position and football geometry remain untouched. */
export class BallTrackingOverlay {
  readonly group = new THREE.Group()
  private readonly halo: THREE.Sprite
  private readonly ring: THREE.Mesh<THREE.RingGeometry, THREE.MeshBasicMaterial>
  private readonly texture: THREE.CanvasTexture
  private settings: BallVisualizationSettings

  constructor(settings: BallVisualizationSettings) {
    this.settings = settings
    const canvas = document.createElement('canvas')
    canvas.width = canvas.height = 128
    const ctx = canvas.getContext('2d')!
    const gradient = ctx.createRadialGradient(64, 64, 29, 64, 64, 63)
    gradient.addColorStop(0, 'rgba(255,255,255,0)')
    gradient.addColorStop(0.33, 'rgba(255,255,255,0)')
    gradient.addColorStop(0.52, 'rgba(255,255,255,0.9)')
    gradient.addColorStop(0.69, 'rgba(255,255,255,0.33)')
    gradient.addColorStop(1, 'rgba(255,255,255,0)')
    ctx.fillStyle = gradient
    ctx.fillRect(0, 0, 128, 128)
    this.texture = new THREE.CanvasTexture(canvas)
    this.halo = new THREE.Sprite(new THREE.SpriteMaterial({ map: this.texture, transparent: true, depthWrite: false, opacity: 0.48 }))
    this.ring = new THREE.Mesh(
      new THREE.RingGeometry(BALL_RADIUS * 1.38, BALL_RADIUS * 1.55, 40),
      new THREE.MeshBasicMaterial({ side: THREE.DoubleSide, transparent: true, depthWrite: false, opacity: 0.68 }),
    )
    this.group.add(this.halo, this.ring)
    this.configure(settings)
  }

  configure(settings: BallVisualizationSettings) {
    this.settings = settings
    const color = BALL_COLORS[settings.color]
    ;(this.halo.material as THREE.SpriteMaterial).color.set(color)
    this.ring.material.color.set(color)
    this.update(0, false)
  }

  update(pulse: number, steady: boolean) {
    const { highlight, size, intensity } = this.settings
    const strength = intensity === 'subtle' ? 0.72 : intensity === 'strong' ? 1.25 : 1
    const scale = BALL_SIZE_SCALE[size]
    const expansion = 1 + (steady ? 0 : pulse * 0.35)
    this.halo.visible = highlight === 'halo' || highlight === 'glow' || (pulse > 0 && this.settings.pulse)
    this.ring.visible = highlight === 'ring'
    this.halo.scale.setScalar(BALL_RADIUS * (highlight === 'glow' ? 6.8 : 4.8) * scale * expansion)
    this.ring.scale.setScalar(scale * expansion)
    ;(this.halo.material as THREE.SpriteMaterial).opacity = Math.min(0.9, ((highlight === 'glow' ? 0.34 : 0.52) + (steady ? 0 : pulse * 0.18)) * strength)
    this.ring.material.opacity = Math.min(0.94, (0.7 + (steady ? 0 : pulse * 0.2)) * strength)
  }

  ringFacing(camera: THREE.Camera) { this.ring.quaternion.copy(camera.quaternion) }

  dispose() {
    this.ring.geometry.dispose()
    this.ring.material.dispose()
    ;(this.halo.material as THREE.SpriteMaterial).dispose()
    this.texture.dispose()
  }
}
