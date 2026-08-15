import { emptySnapshot, type EnrolmentSnapshot, type GcseRow, type PriorQualificationRow } from './enrolmentState'

const STORAGE_KEY = 'enrolmentRules.vue.snapshot.v1'
const SCHEMA_VERSION = 2

/** The only thing persisted: the editable input snapshot plus the last-viewed policy id. Never engine results — those are recomputed from the API after restore. */
export interface StoredEnrolmentSnapshot {
  readonly schemaVersion: typeof SCHEMA_VERSION
  readonly savedAt: string
  readonly selectedPolicyId: string | null
  /**
   * Whether this snapshot holds client-side edits /razor's server state has not seen yet. /razor
   * carries its own facts in a plain `enrolment.state` cookie that outlives a visit to /app, so a
   * later /razor load renders a perfectly non-empty — but stale — snapshot. Emptiness alone cannot
   * tell that apart from an authoritative render, hence this explicit marker: /app sets it on every
   * save, and `razor-sync.ts` clears it once it has posted the edits back.
   *
   * Optional on read: absent in records written by earlier builds, which read as settled (false).
   * That keeps the version at 2 — bumping it would discard those students' stored facts outright.
   */
  readonly pendingSync: boolean
  readonly snapshot: EnrolmentSnapshot
}

export interface LoadedSnapshot {
  readonly snapshot: EnrolmentSnapshot
  readonly selectedPolicyId: string | null
  readonly pendingSync: boolean
}

const emptyLoadedSnapshot: LoadedSnapshot = { snapshot: emptySnapshot, selectedPolicyId: null, pendingSync: false }

/** A client-side edit: marked pending, so a later /razor load posts it back instead of rendering stale cookie facts over it. */
export function saveSnapshot(snapshot: EnrolmentSnapshot, selectedPolicyId: string | null, storage: Storage): void {
  write(snapshot, selectedPolicyId, true, storage)
}

/** A server render mirrored back: authoritative by definition, so nothing is left to sync. */
export function mirrorServerSnapshot(
  snapshot: EnrolmentSnapshot,
  selectedPolicyId: string | null,
  storage: Storage,
): void {
  write(snapshot, selectedPolicyId, false, storage)
}

function write(
  snapshot: EnrolmentSnapshot,
  selectedPolicyId: string | null,
  pendingSync: boolean,
  storage: Storage,
): void {
  const stored: StoredEnrolmentSnapshot = {
    schemaVersion: SCHEMA_VERSION,
    savedAt: new Date().toISOString(),
    selectedPolicyId,
    pendingSync,
    snapshot,
  }
  storage.setItem(STORAGE_KEY, JSON.stringify(stored))
}

/** Missing, malformed, wrong-version, or structurally invalid stored data all resolve to an empty snapshot with no stored policy id, never a thrown error. */
export function loadSnapshot(storage: Storage): LoadedSnapshot {
  const raw = storage.getItem(STORAGE_KEY)
  if (raw === null) {
    return emptyLoadedSnapshot
  }

  let parsed: unknown
  try {
    parsed = JSON.parse(raw)
  } catch {
    return emptyLoadedSnapshot
  }

  return parseStoredSnapshot(parsed) ?? emptyLoadedSnapshot
}

function parseStoredSnapshot(value: unknown): LoadedSnapshot | null {
  if (!isRecord(value)) {
    return null
  }

  if (value.schemaVersion === 1 && typeof value.savedAt === 'string') {
    const snapshot = parseSnapshot(value.snapshot)
    return snapshot === null ? null : { snapshot, selectedPolicyId: null, pendingSync: false }
  }

  if (
    value.schemaVersion !== SCHEMA_VERSION ||
    typeof value.savedAt !== 'string' ||
    (value.selectedPolicyId !== null && typeof value.selectedPolicyId !== 'string') ||
    (value.pendingSync !== undefined && typeof value.pendingSync !== 'boolean')
  ) {
    return null
  }

  const snapshot = parseSnapshot(value.snapshot)
  return snapshot === null
    ? null
    : { snapshot, selectedPolicyId: value.selectedPolicyId, pendingSync: value.pendingSync === true }
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
