// Mirrors EnrolmentRules.Domain.Thresholds.NormalizeGcseGrade — keep both sides in sync; the same
// cases are asserted in gcseGrade.test.ts and GcseGradeNormalisationTests.cs.

/** Lowest GCSE grade on the 1-9 scale. Mirrors Thresholds.MinGcseGrade. */
const MIN_GCSE_GRADE = 1

/** Highest GCSE grade on the 1-9 scale. Mirrors Thresholds.MaxGcseGrade. */
const MAX_GCSE_GRADE = 9

/**
 * Fit a raw typed grade onto the 1-9 integer scale: round to the nearest whole grade, then clamp.
 * Rounding happens first so 9.6 lands on 9 rather than off the scale. Math.round rounds halves away
 * from zero for positives, matching the C# side's MidpointRounding.AwayFromZero.
 *
 * A null or non-numeric entry stays null — "no grade yet" is a real state the form represents, and
 * inventing a grade for it would score a subject the student never entered.
 */
export function normalizeGcseGrade(raw: number | null): number | null {
  if (raw === null || Number.isNaN(raw)) {
    return null
  }

  return Math.min(Math.max(Math.round(raw), MIN_GCSE_GRADE), MAX_GCSE_GRADE)
}
