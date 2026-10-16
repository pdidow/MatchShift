import { describe, expect, it } from 'vitest'
import * as THREE from 'three'
import { BALL_RADIUS, buildFootballGeometry, footballPanels, orientFootball } from './ballModel'
import { buildPlayerGeometry } from './playerModel'
import { REPLAY_MODEL_SCALE } from './modelScale'

describe('football geometry', () => {
  it('has the closed 12-pentagon / 20-hexagon topology, with every edge shared twice', () => {
    const panels = footballPanels()
    expect(panels.filter(p => p.length === 5)).toHaveLength(12)
    expect(panels.filter(p => p.length === 6)).toHaveLength(20)
    const key = (p: THREE.Vector3) => p.toArray().map(v => v.toFixed(5)).join(',')
    const edges = new Map<string, number>()
    for (const p of panels) for (let i = 0; i < p.length; i++) {
      const edge = [key(p[i]), key(p[(i + 1) % p.length])].sort().join('|')
      edges.set(edge, (edges.get(edge) ?? 0) + 1)
    }
    expect(edges.size).toBe(90)
    expect([...edges.values()].every(count => count === 2)).toBe(true)
  })

  it('keeps the ball round, correctly sized, and inside the triangle budget', () => {
    const geometry = buildFootballGeometry()
    const p = geometry.getAttribute('position')
    expect(p.count / 3).toBeLessThan(5000)
    expect(BALL_RADIUS * 2 / REPLAY_MODEL_SCALE).toBeCloseTo(0.22)
    const v = new THREE.Vector3()
    for (let i = 0; i < p.count; i++) {
      const radius = v.fromBufferAttribute(p, i).length()
      expect(radius).toBeGreaterThan(BALL_RADIUS * 0.99)
      expect(radius).toBeLessThan(BALL_RADIUS * 1.00001)
    }
    geometry.dispose()
  })

  it('rolls in the travel direction and reproduces orientation on pause or seek', () => {
    const ball = new THREE.Mesh()
    orientFootball(ball, BALL_RADIUS * Math.PI / 2, 0)
    const rolled = ball.quaternion.clone()
    expect(new THREE.Vector3(0, 1, 0).applyQuaternion(rolled).x).toBeCloseTo(1)
    orientFootball(ball, BALL_RADIUS * Math.PI / 2, 0)
    expect(ball.quaternion.equals(rolled)).toBe(true)
    orientFootball(ball, 0, 10)
    orientFootball(ball, BALL_RADIUS * Math.PI / 2, 0)
    expect(ball.quaternion.equals(rolled)).toBe(true)
    ball.geometry.dispose()
    ;(ball.material as THREE.Material).dispose()
  })
})

describe('footballer geometry', () => {
  it('preserves the animated anatomy in a much smaller distant model', () => {
    const near = buildPlayerGeometry(true)
    const far = buildPlayerGeometry(false)
    expect(near.index!.count / 3).toBeLessThan(6000)
    expect(far.index!.count).toBeLessThan(near.index!.count * 0.45)
    for (const geometry of [near, far]) {
      const weights = geometry.getAttribute('skinWeight')
      const joints = geometry.getAttribute('skinIndex')
      const positions = geometry.getAttribute('position')
      const used = new Set<number>()
      for (let i = 0; i < weights.count; i++) {
        const sum = weights.getX(i) + weights.getY(i) + weights.getZ(i) + weights.getW(i)
        expect(sum).toBeCloseTo(1)
        expect(weights.getX(i)).toBeGreaterThanOrEqual(0)
        expect(weights.getY(i)).toBeGreaterThanOrEqual(0)
        for (const index of [joints.getX(i), joints.getY(i)]) { expect(index).toBeLessThan(14); used.add(index) }
        expect(Number.isFinite(positions.getX(i) + positions.getY(i) + positions.getZ(i))).toBe(true)
      }
      expect(used.size).toBe(14)
      geometry.computeBoundingBox()
      expect(geometry.boundingBox!.min.y).toBeGreaterThanOrEqual(0)
      expect(geometry.boundingBox!.max.y).toBeCloseTo(1.87)
      geometry.dispose()
    }
  })
})
