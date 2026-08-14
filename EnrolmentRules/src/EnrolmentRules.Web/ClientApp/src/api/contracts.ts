// Mirrors src/EnrolmentRules.Web/Api/EnrolmentApiContracts.cs. Maintained by hand for the first
// implementation (see the plan's "Contract and source generation" section) — keep both sides in
// sync when either changes.

export interface OptionItem {
  readonly value: string
  readonly label: string
}

/** One registered policy's wire identifier and display label — the client's own list, never hard-coded. */
export interface PolicyDescriptor {
  readonly id: string
  readonly displayName: string
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
  readonly selectedPolicy: PolicyDescriptor
  readonly availablePolicies: readonly PolicyDescriptor[]
  readonly defaultDateOfBirth: string
  readonly defaultAge: number
  readonly gcseSubjects: readonly OptionItem[]
  readonly aLevelSubjects: readonly OptionItem[]
  readonly priorQualificationSubjects: readonly QualificationSubjectGroup[]
  readonly qualificationGrades: readonly QualificationGradeOptions[]
  readonly hobbies: readonly OptionItem[]
  readonly minChoices: number
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

/**
 * One `chosenALevels` entry's non-destructive comparison status under the selected policy:
 * "Available" (offered and currently green/amber), "Unavailable" (offered but currently red), or
 * "NotOffered" (absent from the selected policy's catalogue). Never a reason to drop the choice from
 * the client's basket.
 */
export type ChoiceStatusKind = 'Available' | 'Unavailable' | 'NotOffered'

export interface ChoiceStatus {
  readonly subject: OptionItem
  readonly status: ChoiceStatusKind
  readonly reason: string | null
}

export interface EnrolmentApiResult {
  readonly policy: PolicyDescriptor
  readonly eligible: boolean
  readonly eligibilityReasons: readonly string[]
  readonly choiceLimitReason: string | null
  readonly explanations: readonly ExplanationResponse[]
  readonly choiceStatuses: readonly ChoiceStatus[]
  readonly minChoices: number
  readonly maxChoices: number
}

export interface EnrolmentEvaluateResponse {
  readonly validationErrors: readonly string[]
  readonly result: EnrolmentApiResult | null
}
