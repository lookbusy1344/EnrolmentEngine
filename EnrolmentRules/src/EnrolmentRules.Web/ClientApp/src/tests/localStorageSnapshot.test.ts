import { describe, expect, it } from 'vitest'
import type { EnrolmentSnapshot } from '../state/enrolmentState'
import { loadSnapshot, saveSnapshot } from '../state/localStorageSnapshot'

function createFakeStorage(): Storage {
  const store = new Map<string, string>()
  return {
    getItem: (key: string) => store.get(key) ?? null,
    setItem: (key: string, value: string) => {
      store.set(key, value)
    },
    removeItem: (key: string) => {
      store.delete(key)
    },
    clear: () => {
      store.clear()
    },
    key: (index: number) => Array.from(store.keys())[index] ?? null,
    get length() {
      return store.size
    },
  }
}

const sampleSnapshot: EnrolmentSnapshot = {
  dateOfBirth: '2009-09-01',
  gcses: [{ subject: 'maths', grade: 8 }],
  priorQualifications: [{ subject: 'applied_science', type: 'BtecDiploma', grade: 'Merit' }],
  hobbies: ['chess_club'],
  chosenALevels: ['physics'],
}

describe('localStorageSnapshot', () => {
  it('round-trips a saved snapshot and its selected policy id', () => {
    const storage = createFakeStorage()

    saveSnapshot(sampleSnapshot, 'elite', storage)

    expect(loadSnapshot(storage)).toEqual({ snapshot: sampleSnapshot, selectedPolicyId: 'elite' })
  })

  it('round-trips a null selected policy id', () => {
    const storage = createFakeStorage()

    saveSnapshot(sampleSnapshot, null, storage)

    expect(loadSnapshot(storage).selectedPolicyId).toBeNull()
  })

  it('returns an empty snapshot and no policy id when nothing is stored', () => {
    const storage = createFakeStorage()

    expect(loadSnapshot(storage)).toEqual({
      snapshot: { dateOfBirth: null, gcses: [], priorQualifications: [], hobbies: [], chosenALevels: [] },
      selectedPolicyId: null,
    })
  })

  it('returns an empty snapshot for malformed JSON', () => {
    const storage = createFakeStorage()
    storage.setItem('enrolmentRules.vue.snapshot.v1', '{not valid json')

    expect(loadSnapshot(storage).snapshot.gcses).toEqual([])
  })

  it('migrates a v1 snapshot without losing facts or the chosen basket', () => {
    const storage = createFakeStorage()
    storage.setItem(
      'enrolmentRules.vue.snapshot.v1',
      JSON.stringify({ schemaVersion: 1, savedAt: new Date().toISOString(), snapshot: sampleSnapshot }),
    )

    expect(loadSnapshot(storage)).toEqual({ snapshot: sampleSnapshot, selectedPolicyId: null })
  })

  it('returns an empty snapshot for an unrecognised future schema version', () => {
    const storage = createFakeStorage()
    storage.setItem(
      'enrolmentRules.vue.snapshot.v1',
      JSON.stringify({
        schemaVersion: 3,
        savedAt: new Date().toISOString(),
        selectedPolicyId: 'elite',
        snapshot: sampleSnapshot,
      }),
    )

    expect(loadSnapshot(storage).snapshot.gcses).toEqual([])
  })

  it('returns an empty snapshot when a row has an invalid shape', () => {
    const storage = createFakeStorage()
    storage.setItem(
      'enrolmentRules.vue.snapshot.v1',
      JSON.stringify({
        schemaVersion: 2,
        savedAt: new Date().toISOString(),
        selectedPolicyId: 'standard',
        snapshot: { ...sampleSnapshot, gcses: [{ subject: 'maths', grade: 'not-a-number' }] },
      }),
    )

    expect(loadSnapshot(storage)).toEqual({
      snapshot: { dateOfBirth: null, gcses: [], priorQualifications: [], hobbies: [], chosenALevels: [] },
      selectedPolicyId: null,
    })
  })

  it('stores only the editable snapshot and policy id, not API results', () => {
    const storage = createFakeStorage()

    saveSnapshot(sampleSnapshot, 'elite', storage)

    const raw = storage.getItem('enrolmentRules.vue.snapshot.v1')
    expect(raw).not.toBeNull()
    const persisted: unknown = JSON.parse(raw ?? '')
    expect(persisted).toEqual({
      schemaVersion: 2,
      savedAt: expect.any(String) as unknown,
      selectedPolicyId: 'elite',
      snapshot: sampleSnapshot,
    })
  })

  it('saving an empty snapshot resets facts and basket but keeps the given policy id', () => {
    const storage = createFakeStorage()
    saveSnapshot(sampleSnapshot, 'elite', storage)

    saveSnapshot(
      { dateOfBirth: null, gcses: [], priorQualifications: [], hobbies: [], chosenALevels: [] },
      'elite',
      storage,
    )

    expect(loadSnapshot(storage)).toEqual({
      snapshot: { dateOfBirth: null, gcses: [], priorQualifications: [], hobbies: [], chosenALevels: [] },
      selectedPolicyId: 'elite',
    })
  })

  it('ignores a pendingSync field left over from an older stored record', () => {
    const storage = createFakeStorage()
    storage.setItem(
      'enrolmentRules.vue.snapshot.v1',
      JSON.stringify({
        schemaVersion: 2,
        savedAt: new Date().toISOString(),
        selectedPolicyId: 'elite',
        pendingSync: true,
        snapshot: sampleSnapshot,
      }),
    )

    expect(loadSnapshot(storage)).toEqual({ snapshot: sampleSnapshot, selectedPolicyId: 'elite' })
  })
})
