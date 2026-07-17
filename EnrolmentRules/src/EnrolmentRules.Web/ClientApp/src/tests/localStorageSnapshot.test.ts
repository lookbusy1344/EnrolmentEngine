import { describe, expect, it } from 'vitest'
import type { EnrolmentSnapshot } from '../state/enrolmentState'
import { clearSnapshot, loadSnapshot, saveSnapshot } from '../state/localStorageSnapshot'

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
  it('round-trips a saved snapshot', () => {
    const storage = createFakeStorage()

    saveSnapshot(sampleSnapshot, storage)

    expect(loadSnapshot(storage)).toEqual(sampleSnapshot)
  })

  it('returns an empty snapshot when nothing is stored', () => {
    const storage = createFakeStorage()

    expect(loadSnapshot(storage)).toEqual({
      dateOfBirth: null,
      gcses: [],
      priorQualifications: [],
      hobbies: [],
      chosenALevels: [],
    })
  })

  it('returns an empty snapshot for malformed JSON', () => {
    const storage = createFakeStorage()
    storage.setItem('enrolmentRules.vue.snapshot.v1', '{not valid json')

    expect(loadSnapshot(storage).gcses).toEqual([])
  })

  it('returns an empty snapshot for an unrecognised schema version', () => {
    const storage = createFakeStorage()
    storage.setItem(
      'enrolmentRules.vue.snapshot.v1',
      JSON.stringify({ schemaVersion: 2, savedAt: new Date().toISOString(), snapshot: sampleSnapshot }),
    )

    expect(loadSnapshot(storage).gcses).toEqual([])
  })

  it('returns an empty snapshot when a row has an invalid shape', () => {
    const storage = createFakeStorage()
    storage.setItem(
      'enrolmentRules.vue.snapshot.v1',
      JSON.stringify({
        schemaVersion: 1,
        savedAt: new Date().toISOString(),
        snapshot: { ...sampleSnapshot, gcses: [{ subject: 'maths', grade: 'not-a-number' }] },
      }),
    )

    expect(loadSnapshot(storage)).toEqual({
      dateOfBirth: null,
      gcses: [],
      priorQualifications: [],
      hobbies: [],
      chosenALevels: [],
    })
  })

  it('stores only the editable snapshot, not API results', () => {
    const storage = createFakeStorage()

    saveSnapshot(sampleSnapshot, storage)

    const raw = storage.getItem('enrolmentRules.vue.snapshot.v1')
    expect(raw).not.toBeNull()
    const persisted: unknown = JSON.parse(raw ?? '')
    expect(persisted).toEqual({
      schemaVersion: 1,
      savedAt: expect.any(String) as unknown,
      snapshot: sampleSnapshot,
    })
  })

  it('clearSnapshot removes the stored value', () => {
    const storage = createFakeStorage()
    saveSnapshot(sampleSnapshot, storage)

    clearSnapshot(storage)

    expect(storage.getItem('enrolmentRules.vue.snapshot.v1')).toBeNull()
  })
})
