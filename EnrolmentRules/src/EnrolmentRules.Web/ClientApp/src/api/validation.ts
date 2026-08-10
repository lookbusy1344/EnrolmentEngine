// Narrows the `unknown` JSON a `fetch` response yields into our typed contracts (see
// api/contracts.ts) without ever trusting an `any`. The backend contract is covered end-to-end by
// WebApplicationFactory tests, so this stays a shape check (right fields, right primitive types),
// not a full business-rule revalidation.
import type {
  AdjustmentResponse,
  EnrolmentApiResult,
  EnrolmentEvaluateResponse,
  EnrolmentOptionsResponse,
  ExplanationResponse,
  OptionItem,
  QualificationGradeOptions,
  QualificationSubjectGroup,
} from './contracts'

export function parseOptionsResponse(value: unknown): EnrolmentOptionsResponse | null {
  if (!isRecord(value)) {
    return null
  }

  const gcseSubjects = parseArray(value.gcseSubjects, parseOptionItem)
  const aLevelSubjects = parseArray(value.aLevelSubjects, parseOptionItem)
  const priorQualificationSubjects = parseArray(value.priorQualificationSubjects, parseQualificationSubjectGroup)
  const qualificationGrades = parseArray(value.qualificationGrades, parseQualificationGradeOptions)
  const hobbies = parseArray(value.hobbies, parseOptionItem)
  if (
    typeof value.defaultDateOfBirth !== 'string' ||
    typeof value.defaultAge !== 'number' ||
    typeof value.choiceLimit !== 'number' ||
    gcseSubjects === null ||
    aLevelSubjects === null ||
    priorQualificationSubjects === null ||
    qualificationGrades === null ||
    hobbies === null
  ) {
    return null
  }

  return {
    defaultDateOfBirth: value.defaultDateOfBirth,
    defaultAge: value.defaultAge,
    gcseSubjects,
    aLevelSubjects,
    priorQualificationSubjects,
    qualificationGrades,
    hobbies,
    choiceLimit: value.choiceLimit,
  }
}

export function parseEvaluateResponse(value: unknown): EnrolmentEvaluateResponse | null {
  if (!isRecord(value)) {
    return null
  }

  const validationErrors = parseArray(value.validationErrors, parseStringItem)
  const ejectedChoices = parseArray(value.ejectedChoices, parseOptionItem)
  if (validationErrors === null || ejectedChoices === null) {
    return null
  }

  if (value.result === null) {
    return { validationErrors, ejectedChoices, result: null }
  }

  const result = parseApiResult(value.result)
  return result === null ? null : { validationErrors, ejectedChoices, result }
}

function parseApiResult(value: unknown): EnrolmentApiResult | null {
  if (!isRecord(value)) {
    return null
  }

  const eligibilityReasons = parseArray(value.eligibilityReasons, parseStringItem)
  const explanations = parseArray(value.explanations, parseExplanation)
  if (
    typeof value.eligible !== 'boolean' ||
    (value.choiceLimitReason !== null && typeof value.choiceLimitReason !== 'string') ||
    eligibilityReasons === null ||
    explanations === null
  ) {
    return null
  }

  return {
    eligible: value.eligible,
    eligibilityReasons,
    choiceLimitReason: value.choiceLimitReason,
    explanations,
  }
}

function parseExplanation(value: unknown): ExplanationResponse | null {
  if (!isRecord(value)) {
    return null
  }

  const subject = parseOptionItem(value.subject)
  const overrides = parseArray(value.overrides, parseAdjustment)
  if (
    subject === null ||
    overrides === null ||
    typeof value.rating !== 'string' ||
    typeof value.ratingCssClass !== 'string' ||
    typeof value.reason !== 'string' ||
    typeof value.baseRating !== 'string' ||
    typeof value.baseReason !== 'string' ||
    typeof value.rule !== 'string' ||
    typeof value.predictedPoints !== 'number' ||
    (value.entryEquivalentReason !== null && typeof value.entryEquivalentReason !== 'string')
  ) {
    return null
  }

  return {
    subject,
    rating: value.rating,
    ratingCssClass: value.ratingCssClass,
    reason: value.reason,
    baseRating: value.baseRating,
    baseReason: value.baseReason,
    rule: value.rule,
    predictedPoints: value.predictedPoints,
    entryEquivalentReason: value.entryEquivalentReason,
    overrides,
  }
}

function parseAdjustment(value: unknown): AdjustmentResponse | null {
  if (!isRecord(value)) {
    return null
  }

  const { subject, from, to, reason } = value
  if (typeof subject !== 'string' || typeof from !== 'string' || typeof to !== 'string' || typeof reason !== 'string') {
    return null
  }

  return { subject, from, to, reason }
}

function parseOptionItem(value: unknown): OptionItem | null {
  if (!isRecord(value)) {
    return null
  }

  const { value: itemValue, label } = value
  return typeof itemValue === 'string' && typeof label === 'string' ? { value: itemValue, label } : null
}

function parseQualificationSubjectGroup(value: unknown): QualificationSubjectGroup | null {
  if (!isRecord(value) || typeof value.type !== 'string' || typeof value.label !== 'string') {
    return null
  }

  const subjects = parseArray(value.subjects, parseOptionItem)
  return subjects === null ? null : { type: value.type, label: value.label, subjects }
}

function parseQualificationGradeOptions(value: unknown): QualificationGradeOptions | null {
  if (!isRecord(value) || typeof value.type !== 'string') {
    return null
  }

  const grades = parseArray(value.grades, parseOptionItem)
  return grades === null ? null : { type: value.type, grades }
}

function parseStringItem(value: unknown): string | null {
  return typeof value === 'string' ? value : null
}

function parseArray<T>(value: unknown, parseItem: (item: unknown) => T | null): readonly T[] | null {
  if (!Array.isArray(value)) {
    return null
  }

  const result: T[] = []
  for (const item of value as unknown[]) {
    const parsed = parseItem(item)
    if (parsed === null) {
      return null
    }

    result.push(parsed)
  }

  return result
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}
