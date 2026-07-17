import { emptySnapshot, type EnrolmentSnapshot, type GcseRow, type PriorQualificationRow } from './enrolmentState'

const STORAGE_KEY = 'enrolmentRules.vue.snapshot.v1'
const SCHEMA_VERSION = 1

/** The only thing persisted: the editable input snapshot. Never engine results — those are recomputed from the API after restore. */
export interface StoredEnrolmentSnapshot {
  readonly schemaVersion: typeof SCHEMA_VERSION
  readonly savedAt: string
  readonly snapshot: EnrolmentSnapshot
}

export function saveSnapshot(snapshot: EnrolmentSnapshot, storage: Storage): void {
  const stored: StoredEnrolmentSnapshot = { schemaVersion: SCHEMA_VERSION, savedAt: new Date().toISOString(), snapshot }
  storage.setItem(STORAGE_KEY, JSON.stringify(stored))
}

/** Missing, malformed, wrong-version, or structurally invalid stored data all resolve to `emptySnapshot`, never a thrown error. */
export function loadSnapshot(storage: Storage): EnrolmentSnapshot {
  const raw = storage.getItem(STORAGE_KEY)
  if (raw === null) {
    return emptySnapshot
  }

  let parsed: unknown
  try {
    parsed = JSON.parse(raw)
  } catch {
    return emptySnapshot
  }

  return parseStoredSnapshot(parsed) ?? emptySnapshot
}

export function clearSnapshot(storage: Storage): void {
  storage.removeItem(STORAGE_KEY)
}

function parseStoredSnapshot(value: unknown): EnrolmentSnapshot | null {
  if (!isRecord(value)) {
    return null
  }

  if (value.schemaVersion !== SCHEMA_VERSION || typeof value.savedAt !== 'string') {
    return null
  }

  return parseSnapshot(value.snapshot)
}

function parseSnapshot(value: unknown): EnrolmentSnapshot | null {
  if (!isRecord(value)) {
    return null
  }

  const { dateOfBirth, gcses, priorQualifications, hobbies, chosenALevels } = value
  if (dateOfBirth !== null && typeof dateOfBirth !== 'string') {
    return null
  }

  const parsedGcses = parseArray(gcses, parseGcseRow)
  const parsedPriorQualifications = parseArray(priorQualifications, parsePriorQualificationRow)
  const parsedHobbies = parseArray(hobbies, parseStringItem)
  const parsedChosenALevels = parseArray(chosenALevels, parseStringItem)
  if (
    parsedGcses === null ||
    parsedPriorQualifications === null ||
    parsedHobbies === null ||
    parsedChosenALevels === null
  ) {
    return null
  }

  return {
    dateOfBirth,
    gcses: parsedGcses,
    priorQualifications: parsedPriorQualifications,
    hobbies: parsedHobbies,
    chosenALevels: parsedChosenALevels,
  }
}

function parseGcseRow(value: unknown): GcseRow | null {
  if (!isRecord(value)) {
    return null
  }

  const { subject, grade } = value
  if (typeof subject !== 'string' || (grade !== null && typeof grade !== 'number')) {
    return null
  }

  return { subject, grade }
}

function parsePriorQualificationRow(value: unknown): PriorQualificationRow | null {
  if (!isRecord(value)) {
    return null
  }

  const { subject, type, grade } = value
  if (typeof subject !== 'string' || typeof type !== 'string' || typeof grade !== 'string') {
    return null
  }

  return { subject, type, grade }
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
