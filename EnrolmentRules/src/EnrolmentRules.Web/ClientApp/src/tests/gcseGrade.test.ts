import { describe, expect, it } from 'vitest'
import { normalizeGcseGrade } from '../state/gcseGrade'

// The C# side of this mirror is guarded by GcseGradeNormalisationTests with the same cases.
describe('normalizeGcseGrade', () => {
  it.each([
    [1, 1],
    [5, 5],
    [9, 9],
  ])('leaves %d, already on the scale, unchanged', (raw, expected) => {
    expect(normalizeGcseGrade(raw)).toBe(expected)
  })

  it.each([
    [10, 9],
    [47, 9],
    [0, 1],
    [-3, 1],
  ])('clamps %d to the nearest bound', (raw, expected) => {
    expect(normalizeGcseGrade(raw)).toBe(expected)
  })

  it.each([
    [7.4, 7],
    [7.6, 8],
    [8.5, 9],
    [6.5, 7],
    [1.2, 1],
  ])('rounds %d to the nearest integer', (raw, expected) => {
    expect(normalizeGcseGrade(raw)).toBe(expected)
  })

  it.each([9.6, 0.4])('rounds %d before clamping so the result never leaves the scale', (raw) => {
    expect(normalizeGcseGrade(raw)).toBeGreaterThanOrEqual(1)
    expect(normalizeGcseGrade(raw)).toBeLessThanOrEqual(9)
  })

  it('passes null through as "no grade entered"', () => {
    expect(normalizeGcseGrade(null)).toBeNull()
  })

  it('treats a non-numeric entry as no grade rather than inventing one', () => {
    expect(normalizeGcseGrade(Number.NaN)).toBeNull()
  })
})
