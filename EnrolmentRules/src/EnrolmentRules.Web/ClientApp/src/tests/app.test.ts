import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { EnrolmentEvaluateResponse, EnrolmentOptionsResponse } from '../api/contracts'
import App from '../App.vue'
import { emptySnapshot } from '../state/enrolmentState'
import { saveSnapshot } from '../state/localStorageSnapshot'

const sampleOptions: EnrolmentOptionsResponse = {
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
  choiceLimit: 3,
}

const sampleEvaluateResponse: EnrolmentEvaluateResponse = { validationErrors: [], ejectedChoices: [], result: null }

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

function stubFetch() {
  const fetch = vi.fn((input: string | URL | Request, _init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
    if (url.endsWith('/api/enrolment/options')) {
      return Promise.resolve(jsonResponse(sampleOptions))
    }

    return Promise.resolve(jsonResponse(sampleEvaluateResponse))
  })

  vi.stubGlobal('fetch', fetch)
  return fetch
}

afterEach(() => {
  vi.unstubAllGlobals()
})

beforeEach(() => {
  vi.stubGlobal('localStorage', createFakeStorage())
})

describe('App', () => {
  it('loads options and renders the facts form and chosen basket', async () => {
    stubFetch()

    const wrapper = mount(App)
    await flushPromises()

    expect(wrapper.text()).toContain('About you')
    expect(wrapper.text()).toContain('None chosen yet.')
  })

  it('uses the API default date of birth for a fresh empty snapshot', async () => {
    const fetch = stubFetch()

    const wrapper = mount(App)
    await flushPromises()

    expect((wrapper.get('#date-of-birth').element as HTMLInputElement).value).toBe(sampleOptions.defaultDateOfBirth)

    const evaluateCall = fetch.mock.calls.find(([url]) => url === '/api/enrolment/evaluate')
    expect(evaluateCall).toBeDefined()
    expect(JSON.parse(evaluateCall?.[1]?.body as string)).toMatchObject({
      dateOfBirth: sampleOptions.defaultDateOfBirth,
    })
  })

  it('start over resets to the API default date of birth', async () => {
    stubFetch()

    const wrapper = mount(App)
    await flushPromises()
    await wrapper.get('#date-of-birth').setValue('2009-09-01')

    await wrapper.get('button.btn-outline-secondary').trigger('click')
    await flushPromises()

    expect((wrapper.get('#date-of-birth').element as HTMLInputElement).value).toBe(sampleOptions.defaultDateOfBirth)
  })

  it('ejects a rejected choice from the basket and re-evaluates without it', async () => {
    // The engine refuses a snapshot naming a choice it now rates red and names the choice; the app drops
    // it and re-posts, so the student sees a verdict rather than the refusal.
    const rejection: EnrolmentEvaluateResponse = {
      validationErrors: ["chosen_a_levels entry 'physics' is no longer available: barred"],
      ejectedChoices: [{ value: 'physics', label: 'Physics' }],
      result: null,
    }
    const bodies: string[] = []
    const fetch = vi.fn((input: string | URL | Request, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
      if (url.endsWith('/api/enrolment/options')) {
        return Promise.resolve(jsonResponse(sampleOptions))
      }

      bodies.push(init?.body as string)
      const posted = JSON.parse(init?.body as string) as { chosenALevels: string[] }
      return Promise.resolve(
        jsonResponse(posted.chosenALevels.includes('physics') ? rejection : sampleEvaluateResponse),
      )
    })
    vi.stubGlobal('fetch', fetch)
    saveSnapshot({ ...emptySnapshot, chosenALevels: ['physics'] }, localStorage)

    const wrapper = mount(App)
    await flushPromises()
    await flushPromises()

    // Re-posted once without the rejected choice, and the second body is the one that stuck.
    const posted = bodies.map((body) => (JSON.parse(body) as { chosenALevels: string[] }).chosenALevels)
    expect(posted[0]).toEqual(['physics'])
    expect(posted[posted.length - 1]).toEqual([])
    expect(wrapper.text()).toContain('None chosen yet.')
    expect(wrapper.text()).toContain('no longer available with your current grades')
    expect(wrapper.text()).toContain('Physics')
    // The refusal itself is never shown to the student.
    expect(wrapper.text()).not.toContain('chosen_a_levels')
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
