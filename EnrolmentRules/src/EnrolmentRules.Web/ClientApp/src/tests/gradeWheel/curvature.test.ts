import { describe, expect, it } from 'vitest'
import { CURVE, curveStyle, nearestIndexFromCentres } from '../../gradeWheel/curvature'

describe('nearestIndexFromCentres', () => {
  const centres = [10, 30, 50, 70, 90]

  it('returns the index whose centre is closest to the viewport centre', () => {
    expect(nearestIndexFromCentres(31, centres)).toBe(1)
    expect(nearestIndexFromCentres(69, centres)).toBe(3)
  })

  it('clamps to the ends when the viewport centre sits past either edge', () => {
    expect(nearestIndexFromCentres(-100, centres)).toBe(0)
    expect(nearestIndexFromCentres(1000, centres)).toBe(centres.length - 1)
  })

  it('breaks an exact tie toward the lower index', () => {
    expect(nearestIndexFromCentres(20, centres)).toBe(0)
  })
})

describe('curveStyle', () => {
  it('leaves the centred cell upright, full size and opaque', () => {
    const s = curveStyle(0)
    expect(s.rotateY).toBeCloseTo(0)
    expect(s.translateZ).toBeCloseTo(0)
    expect(s.scale).toBe(1)
    expect(s.opacity).toBe(1)
  })

  it('rotates and recedes symmetrically either side of centre', () => {
    const left = curveStyle(-1)
    const right = curveStyle(1)
    expect(left.rotateY).toBeCloseTo(-right.rotateY)
    expect(left.scale).toBeCloseTo(right.scale)
    expect(left.opacity).toBeCloseTo(right.opacity)
    expect(right.rotateY).toBeCloseTo(-CURVE.rotateDegPerCell)
    expect(right.scale).toBeLessThan(1)
    expect(right.opacity).toBeLessThan(1)
    expect(right.translateZ).toBeLessThan(0)
  })

  it('floors scale and opacity so far cells never vanish or invert', () => {
    const far = curveStyle(99)
    expect(far.scale).toBeCloseTo(1 - CURVE.maxScaleDrop)
    expect(far.opacity).toBeCloseTo(1 - CURVE.maxOpacityDrop)
    expect(far.scale).toBeGreaterThan(0)
    expect(far.opacity).toBeGreaterThan(0)
  })
})
