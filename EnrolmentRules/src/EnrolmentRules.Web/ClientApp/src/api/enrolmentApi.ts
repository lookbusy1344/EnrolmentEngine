import type { EnrolmentEvaluateRequest, EnrolmentEvaluateResponse, EnrolmentOptionsResponse } from './contracts'
import { parseEvaluateResponse, parseOptionsResponse } from './validation'

export class EnrolmentApiError extends Error {
  readonly status: number

  constructor(message: string, status: number) {
    super(message)
    this.name = 'EnrolmentApiError'
    this.status = status
  }
}

export async function fetchOptions(signal?: AbortSignal): Promise<EnrolmentOptionsResponse> {
  const response = await fetch('/api/enrolment/options', { signal })
  if (!response.ok) {
    throw new EnrolmentApiError(
      `GET /api/enrolment/options failed with status ${String(response.status)}`,
      response.status,
    )
  }

  const body: unknown = await response.json()
  const options = parseOptionsResponse(body)
  if (options === null) {
    throw new EnrolmentApiError('GET /api/enrolment/options returned an unrecognised response shape.', response.status)
  }

  return options
}

/** Posts the full snapshot every time — never a partial "choose subject" mutation. The API is stateless: every field it needs travels with the request. */
export async function evaluateEnrolment(
  request: EnrolmentEvaluateRequest,
  signal?: AbortSignal,
): Promise<EnrolmentEvaluateResponse> {
  const response = await fetch('/api/enrolment/evaluate', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
    signal,
  })
  if (!response.ok) {
    throw new EnrolmentApiError(
      `POST /api/enrolment/evaluate failed with status ${String(response.status)}`,
      response.status,
    )
  }

  const body: unknown = await response.json()
  const evaluation = parseEvaluateResponse(body)
  if (evaluation === null) {
    throw new EnrolmentApiError(
      'POST /api/enrolment/evaluate returned an unrecognised response shape.',
      response.status,
    )
  }

  return evaluation
}

/**
 * Wraps {@link evaluateEnrolment} so a newer call always supersedes an older one in flight: each
 * call aborts the previous request, and a request that turns out to have been superseded resolves
 * to `null` instead of throwing or returning stale data.
 */
export class EvaluationRequester {
  private controller: AbortController | null = null

  async evaluate(request: EnrolmentEvaluateRequest): Promise<EnrolmentEvaluateResponse | null> {
    this.controller?.abort()
    const controller = new AbortController()
    this.controller = controller

    try {
      return await evaluateEnrolment(request, controller.signal)
    } catch (error) {
      if (controller.signal.aborted) {
        return null
      }

      throw error
    }
  }
}
