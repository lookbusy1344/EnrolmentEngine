import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { EnrolmentSnapshot } from '../state/enrolmentState'
import { loadSnapshot, mirrorServerSnapshot, saveSnapshot } from '../state/localStorageSnapshot'

const emptySnapshot: EnrolmentSnapshot = {
  dateOfBirth: null,
  gcses: [],
  priorQualifications: [],
  hobbies: [],
  chosenALevels: [],
}

const sampleSnapshot: EnrolmentSnapshot = {
  dateOfBirth: '2009-09-01',
  gcses: [{ subject: 'maths', grade: 8 }],
  priorQualifications: [{ subject: 'applied_science', type: 'BtecDiploma', grade: 'Merit' }],
  hobbies: ['chess_club'],
  chosenALevels: ['french'],
}

/** What a still-live `enrolment.state` cookie renders after /app has since added subjects. */
const staleSnapshot: EnrolmentSnapshot = {
  dateOfBirth: '2009-09-01',
  gcses: [{ subject: 'biology', grade: 5 }],
  priorQualifications: [],
  hobbies: [],
  chosenALevels: [],
}

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

function renderDom(options: {
  snapshot: EnrolmentSnapshot
  selectedPolicyId: string | null
  empty: boolean
  cleared: boolean
}): void {
  document.body.innerHTML = `
    <script type="application/json" id="enrolment-snapshot">${JSON.stringify(options.snapshot)}</script>
    <div id="enrolment-sync-flags" hidden
         data-selected-policy="${options.selectedPolicyId ?? ''}"
         data-empty="${String(options.empty)}"
         data-cleared="${String(options.cleared)}"></div>
    <form id="hydrate-form" method="post" action="https://example.test/razor?handler=Hydrate"></form>
  `
}

async function loadRazorSync(): Promise<void> {
  vi.resetModules()
  await import('../razor-sync')
}

// jsdom does not implement form submission navigation; the module only cares that it was asked to
// submit. Captured as its own reference (rather than reading `form.submit` back out later) so
// assertions never pass an unbound method to `expect`.
let submitMock: ReturnType<typeof vi.fn<() => void>>

describe('razor-sync', () => {
  beforeEach(() => {
    vi.stubGlobal('localStorage', createFakeStorage())
    submitMock = vi.fn<() => void>()
    HTMLFormElement.prototype.submit = submitMock
  })

  afterEach(() => {
    document.body.innerHTML = ''
    vi.unstubAllGlobals()
  })

  it('rehydrates from localStorage when the server rendered nothing but localStorage has a snapshot', async () => {
    saveSnapshot(sampleSnapshot, 'elite', localStorage)
    renderDom({ snapshot: emptySnapshot, selectedPolicyId: null, empty: true, cleared: false })

    await loadRazorSync()

    const form = document.getElementById('hydrate-form') as HTMLFormElement
    expect(submitMock).toHaveBeenCalledOnce()
    expect(form.querySelector('input[name="DateOfBirth"]')).toHaveProperty('value', '2009-09-01')
    expect(form.querySelector('input[name="Gcses[0].Subject"]')).toHaveProperty('value', 'maths')
    expect(form.querySelector('input[name="Gcses[0].Grade"]')).toHaveProperty('value', '8')
    expect(form.querySelector('input[name="PriorQualifications[0].Subject"]')).toHaveProperty(
      'value',
      'applied_science',
    )
    expect(form.querySelector('input[name="Hobbies[0]"]')).toHaveProperty('value', 'chess_club')
    expect(form.querySelector('input[name="chosenALevels[0]"]')).toHaveProperty('value', 'french')
    expect(new URL(form.action).searchParams.get('policy')).toBe('elite')
  })

  it('syncs the server-rendered snapshot into localStorage when it is not empty', async () => {
    renderDom({ snapshot: sampleSnapshot, selectedPolicyId: 'elite', empty: false, cleared: false })

    await loadRazorSync()

    expect(loadSnapshot(localStorage)).toEqual({
      snapshot: sampleSnapshot,
      selectedPolicyId: 'elite',
      pendingSync: false,
    })
    expect(submitMock).not.toHaveBeenCalled()
  })

  // The bug this guards: a still-live enrolment.state cookie makes the render non-empty, so an
  // emptiness check alone concludes the render is authoritative — silently dropping every fact
  // /app added since that cookie was written, and overwriting localStorage with the stale copy.
  it('rehydrates when localStorage carries /app edits a still-live state cookie predates', async () => {
    saveSnapshot(sampleSnapshot, 'elite', localStorage)
    renderDom({ snapshot: staleSnapshot, selectedPolicyId: 'elite', empty: false, cleared: false })

    await loadRazorSync()

    const form = document.getElementById('hydrate-form') as HTMLFormElement
    expect(submitMock).toHaveBeenCalledOnce()
    expect(form.querySelector('input[name="Gcses[0].Subject"]')).toHaveProperty('value', 'maths')
    expect(form.querySelector('input[name="chosenALevels[0]"]')).toHaveProperty('value', 'french')
    expect(loadSnapshot(localStorage).snapshot).toEqual(sampleSnapshot)
  })

  // Without this the hydrate POST's own redirect would still look pending and hydrate again, forever.
  it('stops looking pending once hydration has been posted, so the redirect settles', async () => {
    saveSnapshot(sampleSnapshot, 'elite', localStorage)
    renderDom({ snapshot: staleSnapshot, selectedPolicyId: 'elite', empty: false, cleared: false })
    await loadRazorSync()
    expect(submitMock).toHaveBeenCalledOnce()

    // The redirect that follows: the server now renders exactly what was hydrated.
    submitMock.mockClear()
    renderDom({ snapshot: sampleSnapshot, selectedPolicyId: 'elite', empty: false, cleared: false })
    await loadRazorSync()

    expect(submitMock).not.toHaveBeenCalled()
    expect(loadSnapshot(localStorage)).toEqual({
      snapshot: sampleSnapshot,
      selectedPolicyId: 'elite',
      pendingSync: false,
    })
  })

  // "Start over" on /app empties localStorage while the /razor cookie still holds the old facts.
  it('propagates an /app Start over instead of letting the stale cookie restore the old facts', async () => {
    saveSnapshot(emptySnapshot, 'elite', localStorage)
    renderDom({ snapshot: staleSnapshot, selectedPolicyId: 'elite', empty: false, cleared: false })

    await loadRazorSync()

    expect(submitMock).toHaveBeenCalledOnce()
    expect(loadSnapshot(localStorage).snapshot).toEqual(emptySnapshot)
  })

  it('treats a mirrored render as settled, never as pending edits to post back', async () => {
    mirrorServerSnapshot(sampleSnapshot, 'elite', localStorage)
    renderDom({ snapshot: sampleSnapshot, selectedPolicyId: 'elite', empty: false, cleared: false })

    await loadRazorSync()

    expect(submitMock).not.toHaveBeenCalled()
  })

  it('clears localStorage on an explicit Start over instead of rehydrating stale data', async () => {
    saveSnapshot(sampleSnapshot, 'elite', localStorage)
    renderDom({ snapshot: emptySnapshot, selectedPolicyId: 'elite', empty: true, cleared: true })

    await loadRazorSync()

    expect(loadSnapshot(localStorage)).toEqual({
      snapshot: emptySnapshot,
      selectedPolicyId: 'elite',
      pendingSync: false,
    })
    expect(submitMock).not.toHaveBeenCalled()
  })

  it('does not rehydrate on a genuine first visit where both the render and localStorage are empty', async () => {
    renderDom({ snapshot: emptySnapshot, selectedPolicyId: null, empty: true, cleared: false })

    await loadRazorSync()

    expect(submitMock).not.toHaveBeenCalled()
    expect(loadSnapshot(localStorage)).toEqual({
      snapshot: emptySnapshot,
      selectedPolicyId: null,
      pendingSync: false,
    })
  })
})
