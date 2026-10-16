import * as THREE from 'three'
import { REPLAY_MODEL_SCALE } from './modelScale'

// A 22 cm diameter football, with the same display enlargement as the players.
export const BALL_RADIUS = 0.11 * REPLAY_MODEL_SCALE

/** A truncated icosahedron projected onto a sphere: 12 pentagons, 20 hexagons. */
export function footballPanels(): THREE.Vector3[][] {
  const ico = new THREE.IcosahedronGeometry(1, 0)
  const positions = ico.getAttribute('position')
  const vertices: THREE.Vector3[] = []
  const triangles: number[][] = []
  const neighbours = new Map<number, Set<number>>()
  for (let i = 0; i < positions.count; i += 3) {
    const face: number[] = []
    for (let j = 0; j < 3; j++) {
      const v = new THREE.Vector3().fromBufferAttribute(positions, i + j).normalize()
      let index = vertices.findIndex(p => p.distanceToSquared(v) < 1e-8)
      if (index < 0) { index = vertices.length; vertices.push(v) }
      face.push(index)
    }
    triangles.push(face)
    for (const a of face) for (const b of face) if (a !== b) {
      if (!neighbours.has(a)) neighbours.set(a, new Set())
      neighbours.get(a)!.add(b)
    }
  }
  ico.dispose()
  const cut = (a: number, b: number) => vertices[a].clone().lerp(vertices[b], 1 / 3).normalize()
  const panels = triangles.map(([a, b, c]) => [cut(a, b), cut(b, a), cut(b, c), cut(c, b), cut(c, a), cut(a, c)])
  vertices.forEach((normal, i) => {
    const points = [...neighbours.get(i)!].map(j => cut(i, j))
    const tangent = points[0].clone().addScaledVector(normal, -points[0].dot(normal)).normalize()
    const bitangent = normal.clone().cross(tangent)
    points.sort((a, b) => Math.atan2(a.dot(bitangent), a.dot(tangent)) - Math.atan2(b.dot(bitangent), b.dot(tangent)))
    panels.push(points)
  })
  return panels
}

/** Curved panel surfaces and recessed bevelled seams, merged into one draw call. */
export function buildFootballGeometry() {
  const positions: number[] = []
  const normals: number[] = []
  const colors: number[] = []
  const uvs: number[] = []
  const white = new THREE.Color('#f1f0e8')
  const black = new THREE.Color('#192025')
  const seam = new THREE.Color('#42474a')
  const edgePoint = (a: THREE.Vector3, b: THREE.Vector3, t: number) => {
    const angle = a.angleTo(b)
    return a.clone().multiplyScalar(Math.sin((1 - t) * angle) / Math.sin(angle))
      .addScaledVector(b, Math.sin(t * angle) / Math.sin(angle))
  }
  const triangle = (a: THREE.Vector3, b: THREE.Vector3, c: THREE.Vector3, color: THREE.Color, radius = BALL_RADIUS, smooth = true) => {
    const normal = b.clone().sub(a).cross(c.clone().sub(a)).normalize()
    if (normal.dot(a) < 0) { const swap = b; b = c; c = swap; normal.negate() }
    const points = [a, b, c]
    const us = points.map(v => 0.5 + Math.atan2(v.z, v.x) / (2 * Math.PI))
    if (Math.max(...us) - Math.min(...us) > 0.5) for (let i = 0; i < 3; i++) if (us[i] < 0.5) us[i] += 1
    points.forEach((p, i) => {
      const n = smooth ? p.clone().normalize() : normal
      positions.push(p.x * radius, p.y * radius, p.z * radius)
      normals.push(n.x, n.y, n.z)
      colors.push(color.r, color.g, color.b)
      uvs.push(us[i], 0.5 + Math.asin(THREE.MathUtils.clamp(p.clone().normalize().y, -1, 1)) / Math.PI)
    })
  }
  const curved = (a: THREE.Vector3, b: THREE.Vector3, c: THREE.Vector3, color: THREE.Color, depth: number) => {
    if (depth === 0) { triangle(a, b, c, color); return }
    const ab = a.clone().add(b).normalize()
    const bc = b.clone().add(c).normalize()
    const ca = c.clone().add(a).normalize()
    curved(a, ab, ca, color, depth - 1)
    curved(ab, b, bc, color, depth - 1)
    curved(ca, bc, c, color, depth - 1)
    curved(ab, bc, ca, color, depth - 1)
  }
  for (const panel of footballPanels()) {
    const center = panel.reduce((sum, p) => sum.add(p), new THREE.Vector3()).normalize()
    const color = panel.length === 5 ? black : white
    const inner = panel.map(p => p.clone().lerp(center, 0.016).normalize())
    for (let i = 0; i < panel.length; i++) {
      const next = (i + 1) % panel.length
      curved(center, inner[i], inner[next], color, 2)
      // Four bevel segments follow the spherical panel edge without floating decals.
      for (let j = 0; j < 4; j++) {
        const a = edgePoint(inner[i], inner[next], j / 4)
        const b = edgePoint(inner[i], inner[next], (j + 1) / 4)
        const c = edgePoint(panel[i], panel[next], j / 4).multiplyScalar(0.991)
        const d = edgePoint(panel[i], panel[next], (j + 1) / 4).multiplyScalar(0.991)
        triangle(a, c, b, seam, BALL_RADIUS, false)
        triangle(b, c, d, seam, BALL_RADIUS, false)
      }
    }
  }
  const geometry = new THREE.BufferGeometry()
  geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3))
  geometry.setAttribute('normal', new THREE.Float32BufferAttribute(normals, 3))
  geometry.setAttribute('color', new THREE.Float32BufferAttribute(colors, 3))
  geometry.setAttribute('uv', new THREE.Float32BufferAttribute(uvs, 2))
  geometry.computeBoundingSphere()
  return geometry
}

export function createFootball() {
  const canvas = document.createElement('canvas')
  canvas.width = canvas.height = 128
  const ctx = canvas.getContext('2d')!
  ctx.fillStyle = '#909090'
  ctx.fillRect(0, 0, 128, 128)
  // Small embossed leather grain. Deterministic and generated once per scene.
  for (let y = 0; y < 128; y += 4) for (let x = 0; x < 128; x += 4) {
    const shade = 115 + (x * 13 + y * 7) % 35
    ctx.fillStyle = `rgb(${shade},${shade},${shade})`
    ctx.beginPath()
    ctx.arc(x + 1 + (y % 8) / 4, y + 1, 0.8, 0, Math.PI * 2)
    ctx.fill()
  }
  const grain = new THREE.CanvasTexture(canvas)
  grain.wrapS = grain.wrapT = THREE.RepeatWrapping
  grain.repeat.set(4, 2)
  const material = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.57, metalness: 0, bumpMap: grain, bumpScale: 0.0007 })
  const ball = new THREE.Mesh(buildFootballGeometry(), material)
  ball.name = 'panelled-football'
  ball.castShadow = true
  ball.receiveShadow = true
  return ball
}

/** Roll about the travel axis, deterministically, so held/paused balls stay still. */
export function orientFootball(ball: THREE.Mesh, dx: number, dz: number) {
  const distance = Math.hypot(dx, dz)
  if (distance < 1e-5) ball.quaternion.identity()
  else ball.quaternion.setFromAxisAngle(new THREE.Vector3(dz / distance, 0, -dx / distance), distance / BALL_RADIUS)
}
