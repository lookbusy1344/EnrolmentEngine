import { afterEach, describe, expect, it, vi } from 'vitest'
import type { EnrolmentEvaluateRequest, EnrolmentEvaluateResponse, EnrolmentOptionsResponse } from '../api/contracts'
import {
  EnrolmentApiError,
  evaluateEnrolment,
  EvaluationRequester,
  fetchOptions,
  OptionsRequester,
} from '../api/enrolmentApi'

const sampleOptions: EnrolmentOptionsResponse = {
  selectedPolicy: { id: 'standard', displayName: 'Standard' },
  availablePolicies: [
    { id: 'standard', displayName: 'Standard' },
    { id: 'elite', displayName: 'Elite' },
  ],
  defaultDateOfBirth: '2010-09-01',
  defaultAge: 16,
  gcseSubjects: [{ value: 'english_language', label: 'English Language' }],
  aLevelSubjects: [{ value: 'mathematics', label: 'Mathematics' }],
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

const sampleRequest: EnrolmentEvaluateRequest = {
  dateOfBirth: '2009-09-01',
  gcses: [{ subject: 'maths', grade: 8 }],
  priorQualifications: [],
  hobbies: ['chess_club'],
  chosenALevels: ['physics'],
}

const sampleEvaluateResponse: EnrolmentEvaluateResponse = {
  validationErrors: [],
  result: {
    policy: { id: 'standard', displayName: 'Standard' },
    eligible: true,
    eligibilityReasons: [],
    choiceLimitReason: null,
    explanations: [
      {
        subject: { value: 'physics', label: 'Physics' },
        rating: 'Green',
        ratingCssClass: 'text-bg-success',
        reason: 'Meets entry requirements.',
        baseRating: 'Green',
        baseReason: 'Base table reason',
        rule: 'physics.entry',
        predictedPoints: 5.25,
        entryEquivalentReason: null,
        overrides: [],
      },
    ],
    choiceStatuses: [{ subject: { value: 'physics', label: 'Physics' }, status: 'Available', reason: null }],
    minChoices: 0,
    maxChoices: 3,
  },
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('fetchOptions', () => {
  it('maps a successful response into typed option state', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(jsonResponse(sampleOptions))),
    )

    const options = await fetchOptions()

    expect(options).toEqual(sampleOptions)
  })

  it('throws EnrolmentApiError on a non-2xx response', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(jsonResponse({ error: 'boom' }, 500))),
    )

    await expect(fetchOptions()).rejects.toBeInstanceOf(EnrolmentApiError)
  })

  it('appends the given policy id as a query parameter', async () => {
    const fetchMock = vi.fn((_url: string) => Promise.resolve(jsonResponse(sampleOptions)))
    vi.stubGlobal('fetch', fetchMock)

    await fetchOptions('elite')

    expect(fetchMock.mock.calls[0][0]).toBe('/api/enrolment/options?policy=elite')
  })

  it.each([
    [
      'duplicate policy ids',
      {
        ...sampleOptions,
        availablePolicies: [
          { id: 'standard', displayName: 'Standard' },
          { id: 'standard', displayName: 'Elite' },
        ],
      },
    ],
    [
      'duplicate policy display names',
      {
        ...sampleOptions,
        availablePolicies: [
          { id: 'standard', displayName: 'Standard' },
          { id: 'elite', displayName: 'Standard' },
        ],
      },
    ],
    ['an empty policy id', { ...sampleOptions, selectedPolicy: { id: '', displayName: 'Standard' } }],
    [
      'a selected policy absent from the available policies',
      { ...sampleOptions, selectedPolicy: { id: 'other', displayName: 'Other' } },
    ],
    [
      'a selected policy whose display name disagrees with the registry',
      { ...sampleOptions, selectedPolicy: { id: 'standard', displayName: 'Different' } },
    ],
  ])('rejects %s', async (_description, body) => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(jsonResponse(body))),
    )

    await expect(fetchOptions()).rejects.toBeInstanceOf(EnrolmentApiError)
  })
})

describe('evaluateEnrolment', () => {
  it('sends the full snapshot, never a partial mutation command', async () => {
    const fetchMock = vi.fn((_url: string, _init?: RequestInit) =>
      Promise.resolve(jsonResponse(sampleEvaluateResponse)),
    )
    vi.stubGlobal('fetch', fetchMock)

    await evaluateEnrolment(sampleRequest)

    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/enrolment/evaluate')
    expect(init).toBeDefined()
    expect(init?.method).toBe('POST')
    expect(JSON.parse(init?.body as string)).toEqual(sampleRequest)
  })

  it('throws EnrolmentApiError on a non-2xx response', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(jsonResponse({ error: 'boom' }, 500))),
    )

    await expect(evaluateEnrolment(sampleRequest)).rejects.toBeInstanceOf(EnrolmentApiError)
  })

  it('appends the given policy id as a query parameter', async () => {
    const fetchMock = vi.fn((_url: string, _init?: RequestInit) =>
      Promise.resolve(jsonResponse(sampleEvaluateResponse)),
    )
    vi.stubGlobal('fetch', fetchMock)

    await evaluateEnrolment(sampleRequest, 'elite')

    expect(fetchMock.mock.calls[0][0]).toBe('/api/enrolment/evaluate?policy=elite')
  })
})

describe('EvaluationRequester', () => {
  it('supersedes an older in-flight evaluate call: the newer call wins, the older resolves to null', async () => {
    let call = 0
    const second = Promise.resolve(jsonResponse(sampleEvaluateResponse))
    const fetchMock = vi.fn((_url: string, init?: RequestInit) => {
      call += 1
      const signal = init?.signal
      if (call === 1) {
        return new Promise<Response>((_resolve, reject) => {
          signal?.addEventListener('abort', () => {
            reject(new DOMException('Aborted', 'AbortError'))
          })
        })
      }

      return second
    })
    vi.stubGlobal('fetch', fetchMock)

    const requester = new EvaluationRequester()
    const firstResultPromise = requester.evaluate(sampleRequest)
    const secondResultPromise = requester.evaluate(sampleRequest)

    const [firstResult, secondResult] = await Promise.all([firstResultPromise, secondResultPromise])

    expect(firstResult).toBeNull()
    expect(secondResult).toEqual(sampleEvaluateResponse)
  })
})

describe('OptionsRequester', () => {
  it('supersedes an older in-flight options call: the newer call wins, the older resolves to null', async () => {
    let call = 0
    const fetchMock = vi.fn((_url: string, init?: RequestInit) => {
      call += 1
      if (call === 1) {
        return new Promise<Response>((_resolve, reject) => {
          init?.signal?.addEventListener('abort', () => {
            reject(new DOMException('Aborted', 'AbortError'))
          })
        })
      }

      return Promise.resolve(jsonResponse({ ...sampleOptions, selectedPolicy: { id: 'elite', displayName: 'Elite' } }))
    })
    vi.stubGlobal('fetch', fetchMock)

    const requester = new OptionsRequester()
    const firstResultPromise = requester.fetch('standard')
    const secondResultPromise = requester.fetch('elite')

    const [firstResult, secondResult] = await Promise.all([firstResultPromise, secondResultPromise])

    expect(firstResult).toBeNull()
    expect(secondResult?.selectedPolicy.id).toBe('elite')
  })
})
