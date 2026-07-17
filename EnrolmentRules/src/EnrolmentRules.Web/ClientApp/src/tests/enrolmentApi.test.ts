import { afterEach, describe, expect, it, vi } from 'vitest'
import type { EnrolmentEvaluateRequest, EnrolmentEvaluateResponse, EnrolmentOptionsResponse } from '../api/contracts'
import { EnrolmentApiError, evaluateEnrolment, EvaluationRequester, fetchOptions } from '../api/enrolmentApi'

const sampleOptions: EnrolmentOptionsResponse = {
  defaultDateOfBirth: '2010-09-01',
  defaultAge: 16,
  gcseSubjects: [{ value: 'english_language', label: 'English Language' }],
  aLevelSubjects: [{ value: 'mathematics', label: 'Mathematics' }],
  priorQualificationSubjects: [{ value: 'applied_science', label: 'Applied Science' }],
  qualificationTypes: [{ value: 'BtecDiploma', label: 'BTEC Diploma' }],
  hobbies: [{ value: 'chess_club', label: 'Chess Club' }],
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
  ejectedChoices: [],
  result: {
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
