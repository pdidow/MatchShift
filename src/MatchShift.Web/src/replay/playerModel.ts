import * as THREE from 'three'

// One skinned draw call per player. Both levels share a skeleton and a tiny kit atlas;
// all players share the two geometry buffers. Dimensions below are in metres.
const SLOT = { shirt: 0, shorts: 1, sock: 2, skin: 3, hair: 4, boot: 5, trim: 6, eye: 7, lip: 8, sole: 9, glove: 10 }
type Profile = [y: number, width: number, depth: number, x?: number, z?: number]
type Weight = [bone: number, other?: number, influence?: number]

const JOINTS: [name: string, parent: number, x: number, y: number, z: number][] = [
  ['hips', -1, 0, 0.97, 0], ['spine', 0, 0, 1.15, 0], ['chest', 1, 0, 1.42, 0], ['head', 2, 0, 1.62, 0],
  ['leftArm', 2, -0.215, 1.46, 0], ['leftForearm', 4, -0.29, 1.18, 0],
  ['rightArm', 2, 0.215, 1.46, 0], ['rightForearm', 6, 0.29, 1.18, 0],
  ['leftThigh', 0, -0.105, 0.94, 0], ['leftShin', 8, -0.105, 0.55, 0], ['leftFoot', 9, -0.105, 0.12, 0],
  ['rightThigh', 0, 0.105, 0.94, 0], ['rightShin', 11, 0.105, 0.55, 0], ['rightFoot', 12, 0.105, 0.12, 0],
]

/** Indexed, smooth surfaces with skin weights baked once at construction. */
class PlayerGeometry {
  positions: number[] = []
  uvs: number[] = []
  indices: number[] = []
  bones: number[] = []
  weights: number[] = []
  seams: [number, number][] = []

  surface(profile: Profile[], segments: number, slot: number | ((y: number) => number), weight: (y: number) => Weight, jersey = false) {
    const start = this.positions.length / 3
    for (let j = 0; j < profile.length; j++) {
      const [y, width, depth, x = 0, z = 0] = profile[j]
      const [bone, other = bone, blend = 0] = weight(y)
      const swatch = typeof slot === 'number' ? slot : slot(y)
      for (let i = 0; i <= segments; i++) {
        const angle = i / segments * Math.PI * 2
        this.positions.push(x + Math.sin(angle) * width, y, z + Math.cos(angle) * depth)
        this.uvs.push(jersey ? i / segments : (swatch + 0.5) / 16, jersey ? (y - 0.96) / 0.59 * 0.75 : 0.9375)
        this.bones.push(bone, other, 0, 0)
        this.weights.push(1 - blend, blend, 0, 0)
        if (j && i) {
          const a = start + j * (segments + 1) + i - 1
          const b = a - segments - 1
          this.indices.push(a, b, a + 1, b, b + 1, a + 1)
        }
      }
      this.seams.push([start + j * (segments + 1), start + j * (segments + 1) + segments])
    }
  }

  ellipsoid(x: number, y: number, z: number, rx: number, ry: number, rz: number, slot: number, bone: number, segments = 10, rings = 6) {
    const profile: Profile[] = []
    for (let j = 0; j <= rings; j++) {
      const angle = j / rings * Math.PI
      const radial = Math.max(0.001, Math.sin(angle))
      profile.push([y - Math.cos(angle) * ry, rx * radial, rz * radial, x, z])
    }
    this.surface(profile, segments, slot, () => [bone])
  }

  finish() {
    const geometry = new THREE.BufferGeometry()
    geometry.setAttribute('position', new THREE.Float32BufferAttribute(this.positions, 3))
    geometry.setAttribute('uv', new THREE.Float32BufferAttribute(this.uvs, 2))
    geometry.setAttribute('skinIndex', new THREE.Uint16BufferAttribute(this.bones, 4))
    geometry.setAttribute('skinWeight', new THREE.Float32BufferAttribute(this.weights, 4))
    geometry.setIndex(this.indices)
    geometry.computeVertexNormals()
    const normals = geometry.getAttribute('normal')
    const n = new THREE.Vector3()
    for (const [a, b] of this.seams) {
      n.set(normals.getX(a) + normals.getX(b), normals.getY(a) + normals.getY(b), normals.getZ(a) + normals.getZ(b)).normalize()
      normals.setXYZ(a, n.x, n.y, n.z)
      normals.setXYZ(b, n.x, n.y, n.z)
    }
    geometry.computeBoundingSphere()
    return geometry
  }
}

const jointWeight = (y: number, joint: number, upper: number, lower: number, blend = 0.06): Weight =>
  [upper, lower, THREE.MathUtils.clamp((joint + blend - y) / (blend * 2), 0, 1)]

export function buildPlayerGeometry(detailed: boolean) {
  const g = new PlayerGeometry()
  const radial = detailed ? 16 : 8
  const limb = detailed ? 12 : 6
  const torso: Profile[] = [
    [0.96, 0.145, 0.095], [1.01, 0.16, 0.105], [1.1, 0.155, 0.095],
    [1.22, 0.18, 0.105], [1.36, 0.215, 0.12], [1.44, 0.225, 0.105],
    [1.49, 0.185, 0.085], [1.53, 0.075, 0.06],
  ]
  g.surface(torso, radial, SLOT.shirt, y => y < 1.2 ? jointWeight(y, 1.09, 1, 0, 0.12) : jointWeight(y, 1.32, 2, 1, 0.12), true)
  // Collar and neck close the shirt opening, with sloping shoulders instead of a capsule.
  g.surface([[1.515, 0.082, 0.067], [1.535, 0.079, 0.064]], radial, SLOT.trim, () => [2])
  g.surface([[1.51, 0.063, 0.055], [1.59, 0.055, 0.053], [1.65, 0.065, 0.06]], limb, SLOT.skin, () => [3])
  g.surface([[0.84, 0.14, 0.08], [0.89, 0.175, 0.107], [0.96, 0.177, 0.107], [1.005, 0.15, 0.10]], radial, SLOT.shorts, () => [0])

  for (const side of [-1, 1]) {
    const arm = side < 0 ? 4 : 6
    const thigh = side < 0 ? 8 : 11
    const legX = side * 0.105
    // Continuous arm skinning across the elbow; sleeves cover the deltoid and upper arm.
    g.surface([
      [0.91, 0.029, 0.033, side * 0.32], [0.99, 0.038, 0.04, side * 0.315],
      [1.08, 0.05, 0.048, side * 0.305], [1.16, 0.043, 0.045, side * 0.29],
      [1.2, 0.045, 0.047, side * 0.285], [1.29, 0.06, 0.06, side * 0.265],
      [1.4, 0.072, 0.075, side * 0.24], [1.46, 0.065, 0.069, side * 0.215],
      [1.49, 0.012, 0.015, side * 0.20],
    ], limb, SLOT.skin, y => jointWeight(y, 1.18, arm, arm + 1))
    g.surface([
      [1.285, 0.065, 0.068, side * 0.267], [1.32, 0.072, 0.074, side * 0.26],
      [1.41, 0.082, 0.083, side * 0.24], [1.46, 0.075, 0.075, side * 0.215],
      [1.495, 0.013, 0.016, side * 0.20],
    ], limb, SLOT.shirt, () => [arm])
    g.surface([[1.283, 0.066, 0.069, side * 0.267], [1.305, 0.07, 0.072, side * 0.263]], limb, SLOT.trim, () => [arm])
    g.ellipsoid(side * 0.323, 0.855, 0.008, 0.037, 0.075, 0.029, SLOT.glove, arm + 1, limb, 6)
    g.ellipsoid(side * 0.295, 0.868, 0.028, 0.018, 0.039, 0.021, SLOT.glove, arm + 1, detailed ? 8 : 5, 4)

    // Thigh, patella, calf and narrow ankle; both knees bend within the continuous surface.
    g.surface([
      [0.10, 0.036, 0.043, legX], [0.18, 0.04, 0.044, legX],
      [0.3, 0.059, 0.067, legX, -0.014], [0.4, 0.067, 0.072, legX, -0.012],
      [0.49, 0.049, 0.055, legX], [0.55, 0.055, 0.06, legX, 0.008],
      [0.6, 0.06, 0.063, legX], [0.72, 0.084, 0.087, legX],
      [0.84, 0.094, 0.092, legX], [0.94, 0.075, 0.075, legX],
    ], limb, SLOT.skin, y => jointWeight(y, 0.55, thigh, thigh + 1))
    g.surface([
      [0.105, 0.038, 0.045, legX], [0.18, 0.042, 0.047, legX], [0.3, 0.062, 0.07, legX, -0.014],
      [0.4, 0.07, 0.075, legX, -0.012], [0.475, 0.056, 0.063, legX],
    ], limb, SLOT.sock, () => [thigh + 1])
    g.surface([[0.46, 0.06, 0.066, legX], [0.48, 0.056, 0.062, legX]], limb, SLOT.trim, () => [thigh + 1])
    g.surface([
      [0.735, 0.088, 0.09, legX], [0.77, 0.098, 0.1, legX],
      [0.88, 0.105, 0.112, legX], [0.94, 0.088, 0.098, legX], [0.985, 0.057, 0.085, legX],
    ], limb, SLOT.shorts, y => jointWeight(y, 0.9, 0, thigh, 0.09))
    // Anatomical boots: low sole, rounded toe in +Z, fitted heel and ankle opening.
    g.surface([
      [0.025, 0.04, 0.106, legX, 0.057], [0.044, 0.054, 0.126, legX, 0.058],
      [0.075, 0.055, 0.12, legX, 0.06], [0.1, 0.047, 0.086, legX, 0.027],
      [0.145, 0.033, 0.045, legX], [0.16, 0.03, 0.038, legX],
    ], limb, SLOT.boot, () => [thigh + 2])
    g.surface([[0.022, 0.043, 0.108, legX, 0.057], [0.039, 0.055, 0.127, legX, 0.058]], limb, SLOT.sole, () => [thigh + 2])
    if (detailed) {
      for (const z of [0.015, 0.045, 0.075]) {
        g.ellipsoid(legX, 0.108 - z * 0.15, z, 0.028, 0.003, 0.004, SLOT.trim, thigh + 2, 6, 4)
      }
      for (const x of [-0.032, 0.032]) for (const z of [-0.02, 0.12]) {
        g.ellipsoid(legX + x, 0.018, z, 0.009, 0.014, 0.01, SLOT.sole, thigh + 2, 6, 4)
      }
    }
  }

  g.surface([
    [1.62, 0.04, 0.05, 0, 0.026], [1.64, 0.063, 0.063, 0, 0.022],
    [1.68, 0.083, 0.079, 0, 0.012], [1.73, 0.093, 0.087, 0, 0.003],
    [1.78, 0.094, 0.092], [1.82, 0.083, 0.082], [1.85, 0.053, 0.055], [1.865, 0.001, 0.001],
  ], radial, SLOT.skin, () => [3])
  // Close-cropped hair with a shaped hairline, ears, a bridge and tip to the nose.
  g.surface([[1.79, 0.094, 0.09, 0, -0.006], [1.82, 0.087, 0.086], [1.85, 0.058, 0.062], [1.87, 0.001, 0.001]], radial, SLOT.hair, () => [3])
  for (const side of [-1, 1]) {
    g.ellipsoid(side * 0.093, 1.727, -0.003, 0.018, 0.033, 0.021, SLOT.skin, 3, detailed ? 10 : 6, 6)
    if (detailed) {
      g.ellipsoid(side * 0.036, 1.746, 0.084, 0.02, 0.008, 0.006, SLOT.trim, 3, 8, 4)
      g.ellipsoid(side * 0.035, 1.746, 0.09, 0.007, 0.007, 0.003, SLOT.eye, 3, 8, 4)
      g.ellipsoid(side * 0.036, 1.762, 0.084, 0.022, 0.004, 0.005, SLOT.hair, 3, 8, 4)
    }
  }
  g.ellipsoid(0, 1.728, 0.09, 0.014, 0.032, 0.018, SLOT.skin, 3, detailed ? 10 : 6, 6)
  g.ellipsoid(0, 1.709, 0.103, 0.018, 0.011, 0.015, SLOT.skin, 3, detailed ? 10 : 6, 4)
  if (detailed) g.ellipsoid(0, 1.682, 0.088, 0.025, 0.004, 0.004, SLOT.lip, 3, 10, 4)
  return g.finish()
}

function identitySeed(id: string) {
  let hash = 2166136261
  for (let i = 0; i < id.length; i++) hash = Math.imul(hash ^ id.charCodeAt(i), 16777619)
  return hash >>> 0
}

function kitTexture(kit: string, number: number, seed: number, keeper: boolean) {
  const canvas = document.createElement('canvas')
  canvas.width = 256
  canvas.height = 256
  const ctx = canvas.getContext('2d')!
  const skin = ['#e0ae8a', '#bb825c', '#885634', '#633e2c', '#c79470', '#edc6a6'][seed % 6]
  const hair = ['#211c19', '#3c2920', '#63432b', '#ad8955'][(seed >>> 4) % 4]
  const palette = [kit, '#1c2534', kit, skin, hair, '#17202b', '#f0eee6', '#252526', '#8d5149', '#747e88', keeper ? '#ecebdc' : skin]
  ctx.fillStyle = kit
  ctx.fillRect(0, 0, 256, 256)
  // A quiet fabric weave is baked into the atlas; no extra normal-map samples.
  ctx.fillStyle = 'rgba(255,255,255,0.035)'
  for (let x = 0; x < 256; x += 4) ctx.fillRect(x, 64, 1, 192)
  const shade = ctx.createLinearGradient(0, 64, 0, 256)
  shade.addColorStop(0, 'rgba(0,0,0,0)')
  shade.addColorStop(1, 'rgba(0,0,0,0.2)')
  ctx.fillStyle = shade
  ctx.fillRect(0, 64, 256, 192)
  ctx.fillStyle = '#e6e9e8'
  ctx.fillRect(59, 70, 4, 184)
  ctx.fillRect(193, 70, 4, 184)
  ctx.font = 'bold 78px Arial'
  ctx.textAlign = 'center'
  ctx.textBaseline = 'middle'
  ctx.fillText(String(number), 128, 153)
  // Small unbranded chest crest.
  ctx.fillRect(16, 111, 8, 12)
  for (let i = 0; i < palette.length; i++) {
    ctx.fillStyle = palette[i]
    ctx.fillRect(i * 16, 0, 16, 32)
  }
  const texture = new THREE.CanvasTexture(canvas)
  texture.colorSpace = THREE.SRGBColorSpace
  // Constant swatch UVs select the base mip; the jersey uses mipmaps to avoid
  // shimmering numbers and weave in the tactical camera.
  texture.minFilter = THREE.LinearMipmapLinearFilter
  texture.magFilter = THREE.LinearFilter
  return texture
}

export interface Footballer {
  root: THREE.Group
  mesh: THREE.SkinnedMesh
  skeleton: THREE.Skeleton
  bones: THREE.Bone[]
  phase: number
  dispose(): void
}

export class PlayerModelLibrary {
  readonly near = buildPlayerGeometry(true)
  readonly far = buildPlayerGeometry(false)

  create(id: string, kit: string, number: number, keeper: boolean): Footballer {
    const seed = identitySeed(id)
    const texture = kitTexture(kit, number, seed, keeper)
    const material = new THREE.MeshStandardMaterial({ map: texture, roughness: 0.88, metalness: 0 })
    const root = new THREE.Group()
    root.name = `footballer:${id}`
    const mesh = new THREE.SkinnedMesh(this.near, material)
    const bones = JOINTS.map(([name]) => { const bone = new THREE.Bone(); bone.name = name; return bone })
    JOINTS.forEach(([, parent, x, y, z], i) => {
      const p = parent >= 0 ? JOINTS[parent] : null
      bones[i].position.set(x - (p?.[2] ?? 0), y - (p?.[3] ?? 0), z - (p?.[4] ?? 0))
      if (parent >= 0) bones[parent].add(bones[i])
    })
    mesh.add(bones[0])
    const skeleton = new THREE.Skeleton(bones)
    mesh.bind(skeleton)
    // Conservative animated bounds avoid scanning every skinned vertex each frame.
    mesh.boundingSphere = new THREE.Sphere(new THREE.Vector3(0, 0.95, 0), 1.35)
    mesh.castShadow = true
    mesh.receiveShadow = true
    root.add(mesh)
    root.scale.set(0.97 + (seed % 7) * 0.01, 0.97 + ((seed >>> 8) % 8) * 0.01, 1)
    return { root, mesh, skeleton, bones, phase: seed % 628 / 100, dispose: () => { skeleton.dispose(); material.dispose(); texture.dispose() } }
  }

  update(model: Footballer, time: number, speed: number, distance: number) {
    // Hysteresis prevents rapid geometry swaps at the camera-distance boundary.
    if (distance > 48) model.mesh.geometry = this.far
    else if (distance < 42) model.mesh.geometry = this.near
    const run = THREE.MathUtils.clamp(speed / 5, 0, 1)
    // Absolute replay time makes pause and scrubbing deterministic, including at 2x speed.
    const phase = time * 9 + model.phase
    const bones = model.bones
    const stride = Math.sin(phase)
    bones[0].position.y = 0.97 + Math.abs(Math.cos(phase)) * 0.025 * run
    bones[0].rotation.y = stride * 0.055 * run
    bones[1].rotation.x = 0.08 * run
    bones[2].rotation.y = -stride * 0.11 * run
    bones[3].rotation.x = -0.05 * run
    for (const [thigh, arm, offset] of [[8, 4, 0], [11, 6, Math.PI]]) {
      const s = Math.sin(phase + offset)
      bones[thigh].rotation.x = -s * 0.65 * run
      bones[thigh + 1].rotation.x = Math.max(0, -s) * 1.08 * run + 0.035
      bones[thigh + 2].rotation.x = -Math.max(0, s) * 0.16 * run
      bones[arm].rotation.x = s * 0.5 * run
      bones[arm].rotation.z = arm === 4 ? -0.07 : 0.07
      bones[arm + 1].rotation.x = -0.15 - (0.65 + s * 0.18) * run
    }
  }

  dispose() { this.near.dispose(); this.far.dispose() }
}
