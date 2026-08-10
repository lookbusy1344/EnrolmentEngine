// Mirrors src/EnrolmentRules.Web/Api/EnrolmentApiContracts.cs. Maintained by hand for the first
// implementation (see the plan's "Contract and source generation" section) — keep both sides in
// sync when either changes.

export interface OptionItem {
  readonly value: string
  readonly label: string
}

export interface QualificationGradeOptions {
  readonly type: string
  readonly grades: readonly OptionItem[]
}

/**
 * One labelled section of the prior-qualification Subject dropdown, keyed by the exact qualification
 * type it represents (e.g. "BtecDiploma"). The client infers `type` for a row from whichever group the
 * chosen subject belongs to, rather than asking the student for it directly.
 */
export interface QualificationSubjectGroup {
  readonly type: string
  readonly label: string
  readonly subjects: readonly OptionItem[]
}

export interface EnrolmentOptionsResponse {
  readonly defaultDateOfBirth: string
  readonly defaultAge: number
  readonly gcseSubjects: readonly OptionItem[]
  readonly aLevelSubjects: readonly OptionItem[]
  readonly priorQualificationSubjects: readonly QualificationSubjectGroup[]
  readonly qualificationGrades: readonly QualificationGradeOptions[]
  readonly hobbies: readonly OptionItem[]
  readonly choiceLimit: number
}

export interface EvaluateGcseRow {
  readonly subject: string | null
  readonly grade: number | null
}

export interface EvaluatePriorQualificationRow {
  readonly subject: string | null
  readonly type: string | null
  readonly grade: string | null
}

export interface EnrolmentEvaluateRequest {
  readonly dateOfBirth: string | null
  readonly gcses: readonly EvaluateGcseRow[]
  readonly priorQualifications: readonly EvaluatePriorQualificationRow[]
  readonly hobbies: readonly string[]
  readonly chosenALevels: readonly string[]
}

export interface AdjustmentResponse {
  readonly subject: string
  readonly from: string
  readonly to: string
  readonly reason: string
}

export interface ExplanationResponse {
  readonly subject: OptionItem
  readonly rating: string
  readonly ratingCssClass: string
  readonly reason: string
  readonly baseRating: string
  readonly baseReason: string
  readonly rule: string
  readonly predictedPoints: number
  readonly entryEquivalentReason: string | null
  readonly overrides: readonly AdjustmentResponse[]
}

export interface EnrolmentApiResult {
  readonly eligible: boolean
  readonly eligibilityReasons: readonly string[]
  readonly choiceLimitReason: string | null
  readonly explanations: readonly ExplanationResponse[]
}

export interface EnrolmentEvaluateResponse {
  readonly validationErrors: readonly string[]
  /**
   * Chosen A-levels the engine now rates red, and so refuses to evaluate against. Non-empty only when
   * `result` is null; drop these from the basket and re-post rather than showing the errors to the student.
   */
  readonly ejectedChoices: readonly OptionItem[]
  readonly result: EnrolmentApiResult | null
}
