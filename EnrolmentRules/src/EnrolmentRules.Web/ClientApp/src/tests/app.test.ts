import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { EnrolmentApiResult, EnrolmentEvaluateResponse, EnrolmentOptionsResponse } from '../api/contracts'
import App from '../App.vue'
import { emptySnapshot } from '../state/enrolmentState'
import { loadSnapshot, saveSnapshot } from '../state/localStorageSnapshot'

const standardPolicy = { id: 'standard', displayName: 'Standard' }
const elitePolicy = { id: 'elite', displayName: 'Elite' }

function makeOptions(policyId: 'standard' | 'elite'): EnrolmentOptionsResponse {
  return {
    selectedPolicy: policyId === 'standard' ? standardPolicy : elitePolicy,
    availablePolicies: [standardPolicy, elitePolicy],
    defaultDateOfBirth: '2010-09-01',
    defaultAge: 16,
    gcseSubjects: [{ value: 'maths', label: 'Maths' }],
    aLevelSubjects: [{ value: 'physics', label: 'Physics' }],
    priorQualificationSubjects: [
      {
        type: 'BtecDiploma',
        label: 'BTEC Diploma examples',
        subjects: [{ value: 'applied_science', label: 'Applied Science' }],
      },
    ],
    qualificationGrades: [
      {
        type: 'BtecDiploma',
        grades: [
          { value: 'pass', label: 'Pass' },
          { value: 'merit', label: 'Merit' },
        ],
      },
    ],
    hobbies: [{ value: 'chess_club', label: 'Chess Club' }],
    minChoices: 0,
    choiceLimit: 3,
  }
}

const sampleEvaluateResponse: EnrolmentEvaluateResponse = { validationErrors: [], result: null }

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
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

function requestUrl(input: string | URL | Request): string {
  return typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
}

function stubFetch() {
  const fetch = vi.fn((input: string | URL | Request, _init?: RequestInit) => {
    const url = requestUrl(input)
    if (url.startsWith('/api/enrolment/options')) {
      return Promise.resolve(jsonResponse(makeOptions(url.includes('policy=elite') ? 'elite' : 'standard')))
    }

    return Promise.resolve(jsonResponse(sampleEvaluateResponse))
  })

  vi.stubGlobal('fetch', fetch)
  return fetch
}

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

beforeEach(() => {
  vi.stubGlobal('localStorage', createFakeStorage())
  window.history.replaceState(null, '', '/app')
})

describe('App', () => {
  it('loads options and renders the facts form and chosen basket', async () => {
    stubFetch()

    const wrapper = mount(App)
    await flushPromises()

    expect(wrapper.text()).toContain('About you')
    expect(wrapper.text()).toContain('None chosen yet.')
    expect(wrapper.text()).toContain('Standard')
  })

  it('uses the API default date of birth for a fresh empty snapshot', async () => {
    const fetch = stubFetch()

    const wrapper = mount(App)
    await flushPromises()

    expect((wrapper.get('#date-of-birth').element as HTMLInputElement).value).toBe('2010-09-01')

    const evaluateCall = fetch.mock.calls.find(([url]) => requestUrl(url).startsWith('/api/enrolment/evaluate'))
    expect(evaluateCall).toBeDefined()
    expect(JSON.parse(evaluateCall?.[1]?.body as string)).toMatchObject({ dateOfBirth: '2010-09-01' })
  })

  it('start over resets to the API default date of birth but keeps the selected policy', async () => {
    stubFetch()

    const wrapper = mount(App)
    await flushPromises()
    await wrapper.get('#date-of-birth').setValue('2009-09-01')

    await wrapper.get('button.btn-outline-secondary').trigger('click')
    await flushPromises()

    expect((wrapper.get('#date-of-birth').element as HTMLInputElement).value).toBe('2010-09-01')
    expect(wrapper.text()).toContain('Standard')
  })

  it('resolves the policy from a ?policy= URL value on initial load', async () => {
    window.history.replaceState(null, '', '/app?policy=elite')
    stubFetch()

    const wrapper = mount(App)
    await flushPromises()

    expect(wrapper.text()).toContain('Elite')
  })

  it('falls back to the stored policy id when the URL carries none', async () => {
    saveSnapshot(emptySnapshot, 'elite', localStorage)
    stubFetch()

    const wrapper = mount(App)
    await flushPromises()

    expect(wrapper.text()).toContain('Elite')
  })

  // Facts must be durable the moment they change. Deferring the write behind the evaluate debounce
  // left a window where a reload or tab close dropped the edit.
  it('persists a fact edit immediately, without waiting on the evaluate debounce', async () => {
    const fetch = stubFetch()

    const wrapper = mount(App)
    await flushPromises()
    const evaluateCallsBefore = fetch.mock.calls.filter(([url]) =>
      requestUrl(url).startsWith('/api/enrolment/evaluate'),
    ).length

    await wrapper.get('#date-of-birth').setValue('2009-09-01')
    await flushPromises()

    expect(loadSnapshot(localStorage).snapshot.dateOfBirth).toBe('2009-09-01')
    // ...while the network call it shares a watcher with is still coalescing.
    expect(fetch.mock.calls.filter(([url]) => requestUrl(url).startsWith('/api/enrolment/evaluate')).length).toBe(
      evaluateCallsBefore,
    )
  })

  it('switching policy re-evaluates the same basket under the new policy without clearing it', async () => {
    const evaluateBodies: unknown[] = []
    const fetch = vi.fn((input: string | URL | Request, init?: RequestInit) => {
      const url = requestUrl(input)
      if (url.startsWith('/api/enrolment/options')) {
        return Promise.resolve(jsonResponse(makeOptions(url.includes('policy=elite') ? 'elite' : 'standard')))
      }

      evaluateBodies.push(JSON.parse(init?.body as string))
      return Promise.resolve(jsonResponse(sampleEvaluateResponse))
    })
    vi.stubGlobal('fetch', fetch)
    saveSnapshot({ ...emptySnapshot, chosenALevels: ['physics'] }, 'standard', localStorage)

    const wrapper = mount(App)
    await flushPromises()
    expect(wrapper.text()).toContain('Standard')

    await wrapper.get('.policy-switch a').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('Elite')
    expect(evaluateBodies.at(-1)).toMatchObject({ chosenALevels: ['physics'] })
  })

  it('recovers from a failed policy-options request when the switch is retried', async () => {
    let eliteAttempts = 0
    const fetch = vi.fn((input: string | URL | Request) => {
      const url = requestUrl(input)
      if (url.startsWith('/api/enrolment/options')) {
        if (url.includes('policy=elite') && ++eliteAttempts === 1) {
          return Promise.resolve(jsonResponse({ error: 'temporary failure' }, 500))
        }

        return Promise.resolve(jsonResponse(makeOptions(url.includes('policy=elite') ? 'elite' : 'standard')))
      }

      return Promise.resolve(jsonResponse(sampleEvaluateResponse))
    })
    vi.stubGlobal('fetch', fetch)

    const wrapper = mount(App)
    await flushPromises()

    await wrapper.get('.policy-switch a').trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('GET /api/enrolment/options?policy=elite failed with status 500')

    await wrapper.get('.policy-switch a').trigger('click')
    await flushPromises()

    expect(wrapper.text()).not.toContain('GET /api/enrolment/options?policy=elite failed with status 500')
    expect(wrapper.text()).toContain('Elite')
    expect(wrapper.text()).toContain('About you')
  })

  it('keeps the pending indicator visible while a superseded call unwinds and a newer one is still in flight', async () => {
    // EvaluationRequester aborts the first evaluate call when the second starts; the abort rejects the
    // first call's fetch, whose own `finally` used to clear `pending` unconditionally even though the
    // second call (the one whose result the student will see) is still awaiting its own response.
    const evaluateCalls: { resolve: (response: Response) => void }[] = []
    const fetch = vi.fn((input: string | URL | Request, init?: RequestInit) => {
      const url = requestUrl(input)
      if (url.startsWith('/api/enrolment/options')) {
        return Promise.resolve(jsonResponse(makeOptions('standard')))
      }

      return new Promise<Response>((resolve, reject) => {
        evaluateCalls.push({ resolve })
        init?.signal?.addEventListener('abort', () => {
          reject(new DOMException('Aborted', 'AbortError'))
        })
      })
    })
    vi.stubGlobal('fetch', fetch)

    const wrapper = mount(App)
    await flushPromises()
    expect(evaluateCalls).toHaveLength(1)
    expect(wrapper.find('[role="status"]').exists()).toBe(true)

    // "Start over" triggers a second, immediate (non-debounced) evaluate call, superseding the first.
    await wrapper.get('button.btn-outline-secondary').trigger('click')
    await flushPromises()

    // The first call has unwound (aborted → resolved to null internally), but the second is still
    // in flight — the pending indicator must still be visible.
    expect(evaluateCalls).toHaveLength(2)
    expect(wrapper.find('[role="status"]').exists()).toBe(true)

    evaluateCalls[1].resolve(jsonResponse(sampleEvaluateResponse))
    await flushPromises()

    expect(wrapper.find('[role="status"]').exists()).toBe(false)
  })

  it('keeps a now-unavailable choice in the basket, flagged, instead of dropping it', async () => {
    // A subject was green or amber when it was chosen; the engine no longer rating it as such shows up as
    // an "Unavailable" choice status rather than a refusal — the app must never eject it from the basket.
    const unavailableResult: EnrolmentApiResult = {
      policy: standardPolicy,
      eligible: true,
      eligibilityReasons: [],
      choiceLimitReason: null,
      explanations: [],
      choiceStatuses: [{ subject: { value: 'physics', label: 'Physics' }, status: 'Unavailable', reason: 'barred' }],
      minChoices: 0,
      maxChoices: 3,
    }
    const fetch = vi.fn((input: string | URL | Request) => {
      const url = requestUrl(input)
      if (url.startsWith('/api/enrolment/options')) {
        return Promise.resolve(jsonResponse(makeOptions('standard')))
      }

      return Promise.resolve(jsonResponse({ validationErrors: [], result: unavailableResult }))
    })
    vi.stubGlobal('fetch', fetch)
    saveSnapshot({ ...emptySnapshot, chosenALevels: ['physics'] }, 'standard', localStorage)

    const wrapper = mount(App)
    await flushPromises()

    expect(wrapper.text()).toContain('Physics')
    expect(wrapper.text()).toContain('Unavailable')
  })

  it('keeps the last comparison statuses while a partial GCSE row has validation errors', async () => {
    vi.useFakeTimers()
    const unavailableResult: EnrolmentApiResult = {
      policy: elitePolicy,
      eligible: true,
      eligibilityReasons: [],
      choiceLimitReason: null,
      explanations: [],
      choiceStatuses: [
        { subject: { value: 'physics', label: 'Physics' }, status: 'Unavailable', reason: 'barred' },
        { subject: { value: 'sociology', label: 'Sociology' }, status: 'NotOffered', reason: 'not offered' },
      ],
      minChoices: 3,
      maxChoices: 4,
    }
    let evaluationCount = 0
    const fetch = vi.fn((input: string | URL | Request) => {
      const url = requestUrl(input)
      if (url.startsWith('/api/enrolment/options')) {
        const eliteOptions = makeOptions('elite')
        return Promise.resolve(
          jsonResponse({
            ...eliteOptions,
            gcseSubjects: [...eliteOptions.gcseSubjects, { value: 'english_literature', label: 'English Literature' }],
          }),
        )
      }

      evaluationCount++
      return Promise.resolve(
        jsonResponse(
          evaluationCount === 1
            ? { validationErrors: [], result: unavailableResult }
            : {
                validationErrors: [
                  "GCSE 'english_literature' grade 0 is out of range (1–9)",
                  'date_of_birth is required',
                ],
                result: null,
              },
        ),
      )
    })
    vi.stubGlobal('fetch', fetch)
    saveSnapshot({ ...emptySnapshot, chosenALevels: ['physics', 'sociology'] }, 'elite', localStorage)

    const wrapper = mount(App)
    await flushPromises()
    await wrapper.get('#gcse-subject-0').setValue('english_literature')
    await vi.advanceTimersByTimeAsync(400)
    await flushPromises()

    expect(wrapper.text()).toContain("GCSE 'english_literature' grade 0 is out of range (1–9)")
    expect(wrapper.text()).toContain('date_of_birth is required')
    expect(wrapper.get('.basket-unavailable-tag').text()).toBe('- Unavailable')
    expect(wrapper.get('.basket-not-offered-tag').text()).toBe('- Not offered')
    expect(wrapper.findAll('.chosen-summary li.text-bg-danger')).toHaveLength(2)
  })

  it('shows an error message when options fail to load', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(jsonResponse({ error: 'boom' }, 500))),
    )

    const wrapper = mount(App)
    await flushPromises()

    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
  })
})
