import type { EnrolmentEvaluateRequest, EvaluateGcseRow, EvaluatePriorQualificationRow } from '../api/contracts'
import { normalizeGcseGrade } from './gcseGrade'

export interface GcseRow {
  readonly subject: string
  readonly grade: number | null
}

export interface PriorQualificationRow {
  readonly subject: string
  readonly type: string
  readonly grade: string
}

export interface EnrolmentSnapshot {
  readonly dateOfBirth: string | null
  readonly gcses: readonly GcseRow[]
  readonly priorQualifications: readonly PriorQualificationRow[]
  readonly hobbies: readonly string[]
  readonly chosenALevels: readonly string[]
}

export const emptySnapshot: EnrolmentSnapshot = {
  dateOfBirth: null,
  gcses: [],
  priorQualifications: [],
  hobbies: [],
  chosenALevels: [],
}

export function isEmptyGcseRow(row: GcseRow): boolean {
  return row.subject.trim().length === 0 && row.grade === null
}

/** The live basket tally over the student's graded GCSEs: count, summed points and mean on the 1-9 scale. */
export interface GcseScoreboard {
  readonly count: number
  readonly total: number
  readonly average: number
}

/**
 * Mirrors EnrolmentRules.Domain.GcseScoreboard.From — the same average as the enrolment decision uses, so
 * the scoreboard and the result never disagree. Grades are normalised onto the whole 1-9 scale first, and
 * only rows carrying both a subject and a grade score: a half-filled row is "not entered yet", not a zero.
 */
export function gcseScoreboard(gcses: readonly GcseRow[]): GcseScoreboard {
  const grades = gcses
    .filter((row) => row.subject.trim().length > 0)
    .map((row) => normalizeGcseGrade(row.grade))
    .filter((grade): grade is number => grade !== null)
  const total = grades.reduce((sum, grade) => sum + grade, 0)
  return { count: grades.length, total, average: grades.length === 0 ? 0 : total / grades.length }
}

export function isEmptyPriorQualificationRow(row: PriorQualificationRow): boolean {
  return row.subject.trim().length === 0 && row.type.trim().length === 0 && row.grade.trim().length === 0
}

/** Whether another GCSE row (not `excludingIndex`) already names `subjectKey` — mirrors RazorModel.IsGcseSubjectChosenElsewhere. */
export function isGcseSubjectChosenElsewhere(
  rows: readonly GcseRow[],
  excludingIndex: number,
  subjectKey: string,
): boolean {
  return rows.some((row, index) => index !== excludingIndex && row.subject === subjectKey)
}

/** Drops blank rows before posting — the API mapper filters them too, but the client shouldn't rely on that. */
export function toEvaluateRequest(snapshot: EnrolmentSnapshot): EnrolmentEvaluateRequest {
  const gcses: EvaluateGcseRow[] = snapshot.gcses
    .filter((row) => !isEmptyGcseRow(row))
    .map((row) => ({ subject: row.subject, grade: normalizeGcseGrade(row.grade) }))

  const priorQualifications: EvaluatePriorQualificationRow[] = snapshot.priorQualifications
    .filter((row) => !isEmptyPriorQualificationRow(row))
    .map((row) => ({ subject: row.subject, type: row.type, grade: row.grade }))

  return {
    dateOfBirth: snapshot.dateOfBirth,
    gcses,
    priorQualifications,
    hobbies: snapshot.hobbies.filter((hobby) => hobby.trim().length > 0),
    chosenALevels: snapshot.chosenALevels,
  }
}
