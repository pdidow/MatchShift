import * as THREE from 'three'
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js'

// Stadium-scale low-poly spectators: a figure-icon, not a character model. Two shared poses, both merged into one
// geometry each and drawn with InstancedMesh:
//  - neutral: a torso wider at the shoulders, closed on top, and a round head on a short neck gap - 32 triangles
//  - cheering: the same figure with both arms raised above the head in a V - 44 triangles
//  - LOW: the neutral figure with a simpler block head - 24 triangles
// Local frame: feet at y = 0; the pitch is towards -x; the stand runs along z. No legs (hidden by the rows in front),
// no faces, no textures. Each vertex carries `aHead` (1 on the head and raised arms) so the shader gives them a skin tone.

const TORSO_HEIGHT = 0.7
const SHOULDER_Y = 0.64
const HEAD_Y = 0.87
const HEAD_RADIUS = 0.135
const ARM_LENGTH = 0.75
const ARM_SPREAD = 0.3    // radians from vertical

function part(geometry: THREE.BufferGeometry, head: number): THREE.BufferGeometry {
  const g = geometry.index ? geometry.toNonIndexed() : geometry
  if (g !== geometry) geometry.dispose()
  g.deleteAttribute('uv')
  g.setAttribute('aHead', new THREE.Float32BufferAttribute(new Array(g.attributes.position.count).fill(head), 1))
  return g
}

function torso() {
  // Four-sided, wider at the top so it reads as shoulders. Closed on top (the tactical camera looks down on the
  // shoulders); no bottom cap, which the rows in front always hide.
  const sides = new THREE.CylinderGeometry(0.26, 0.19, TORSO_HEIGHT, 4, 1, true)
  const top = new THREE.CircleGeometry(0.26, 4)
  top.rotateX(-Math.PI / 2)
  top.translate(0, TORSO_HEIGHT / 2, 0)
  const merged = mergeGeometries([part(sides, 0), part(top, 0)])!
  merged.rotateY(Math.PI / 4)
  merged.scale(0.72, 1, 1)          // shallower front-to-back than shoulder-to-shoulder
  merged.translate(0, TORSO_HEIGHT / 2, 0)
  return merged
}

function head() {
  // A round head (icosahedron), separated from the shoulders by a small gap so the figure never reads as a peg.
  const g = new THREE.IcosahedronGeometry(HEAD_RADIUS, 0)
  g.translate(0, HEAD_Y, 0)
  return part(g, 1)
}

function raisedArm(side: 1 | -1) {
  // Bare arms (skin-tinted like the head): light strokes that read against the dark stands even from the tactical camera.
  const g = new THREE.CylinderGeometry(0.075, 0.095, ARM_LENGTH, 3, 1, true)
  g.translate(0, ARM_LENGTH / 2, 0)                 // pivot at the shoulder
  g.rotateX(side * ARM_SPREAD)                     // out to the side along the stand (z), not towards the pitch
  g.translate(0, SHOULDER_Y, side * 0.2)
  return part(g, 1)
}

function merge(parts: THREE.BufferGeometry[]) {
  const merged = mergeGeometries(parts)!
  for (const p of parts) p.dispose()
  merged.computeBoundingBox()
  merged.computeBoundingSphere()
  return merged
}

function simpleHead() {
  // A small block head: cheaper than the round one and, unlike a pointed head, never reads as a pencil tip.
  const g = new THREE.BoxGeometry(HEAD_RADIUS * 1.7, HEAD_RADIUS * 1.85, HEAD_RADIUS * 1.7)
  g.translate(0, HEAD_Y, 0)
  return part(g, 1)
}

/** Neutral NORMAL spectator. */
export const neutralSpectator = () => merge([torso(), head()])

/** LOW spectator: the same figure with a 12-triangle block head (24 triangles) - still a person, never a peg. No cheering pose. */
export const lowSpectator = () => merge([torso(), simpleHead()])

/** Cheering spectator: the neutral figure with both arms raised in a V above the head. */
export const cheeringSpectator = () => merge([torso(), head(), raisedArm(1), raisedArm(-1)])

export const triangles = (g: THREE.BufferGeometry) => (g.index ? g.index.count : g.attributes.position.count) / 3
