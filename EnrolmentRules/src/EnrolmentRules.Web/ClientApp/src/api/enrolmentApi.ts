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

function optionsUrl(policyId?: string): string {
  return policyId ? `/api/enrolment/options?policy=${encodeURIComponent(policyId)}` : '/api/enrolment/options'
}

function evaluateUrl(policyId?: string): string {
  return policyId ? `/api/enrolment/evaluate?policy=${encodeURIComponent(policyId)}` : '/api/enrolment/evaluate'
}

export async function fetchOptions(policyId?: string, signal?: AbortSignal): Promise<EnrolmentOptionsResponse> {
  const url = optionsUrl(policyId)
  const response = await fetch(url, { signal })
  if (!response.ok) {
    throw new EnrolmentApiError(`GET ${url} failed with status ${String(response.status)}`, response.status)
  }

  const body: unknown = await response.json()
  const options = parseOptionsResponse(body)
  if (options === null) {
    throw new EnrolmentApiError(`GET ${url} returned an unrecognised response shape.`, response.status)
  }

  return options
}

/**
 * Serialises policy-options intent: a newer selection aborts an older request, whose caller receives
 * `null` and therefore cannot overwrite the options chosen by the newer interaction.
 */
export class OptionsRequester {
  private controller: AbortController | null = null

  async fetch(policyId?: string): Promise<EnrolmentOptionsResponse | null> {
    this.controller?.abort()
    const controller = new AbortController()
    this.controller = controller

    try {
      return await fetchOptions(policyId, controller.signal)
    } catch (error) {
      if (controller.signal.aborted) {
        return null
      }

      throw error
    }
  }
}

/** Posts the full snapshot every time — never a partial "choose subject" mutation. The API is stateless: every field it needs travels with the request. */
export async function evaluateEnrolment(
  request: EnrolmentEvaluateRequest,
  policyId?: string,
  signal?: AbortSignal,
): Promise<EnrolmentEvaluateResponse> {
  const url = evaluateUrl(policyId)
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
    signal,
  })
  if (!response.ok) {
    throw new EnrolmentApiError(`POST ${url} failed with status ${String(response.status)}`, response.status)
  }

  const body: unknown = await response.json()
  const evaluation = parseEvaluateResponse(body)
  if (evaluation === null) {
    throw new EnrolmentApiError(`POST ${url} returned an unrecognised response shape.`, response.status)
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

  async evaluate(request: EnrolmentEvaluateRequest, policyId?: string): Promise<EnrolmentEvaluateResponse | null> {
    this.controller?.abort()
    const controller = new AbortController()
    this.controller = controller

    try {
      return await evaluateEnrolment(request, policyId, controller.signal)
    } catch (error) {
      if (controller.signal.aborted) {
        return null
      }

      throw error
    }
  }
}
