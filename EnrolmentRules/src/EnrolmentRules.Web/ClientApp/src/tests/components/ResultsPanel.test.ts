import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import type { EnrolmentApiResult, EnrolmentEvaluateResponse } from '../../api/contracts'
import ResultsPanel from '../../components/ResultsPanel.vue'

const policy = { id: 'standard', displayName: 'Standard' }

function apiResult(overrides: Partial<EnrolmentApiResult>): EnrolmentApiResult {
  return {
    policy,
    eligible: true,
    eligibilityReasons: [],
    choiceLimitReason: null,
    explanations: [],
    choiceStatuses: [],
    minChoices: 0,
    maxChoices: 3,
    ...overrides,
  }
}

describe('ResultsPanel', () => {
  it('shows validation errors as visible text, not colour alone', () => {
    const evaluation: EnrolmentEvaluateResponse = {
      validationErrors: ['Date of birth is required.'],
      result: null,
    }
    const wrapper = mount(ResultsPanel, { props: { evaluation, chosenALevels: [], hasFacts: true } })

    expect(wrapper.text()).toContain('Date of birth is required.')
  })

  it('shows an ineligible eligibility reason as visible text', () => {
    const evaluation: EnrolmentEvaluateResponse = {
      validationErrors: [],
      result: apiResult({ eligible: false, eligibilityReasons: ['Too young.'] }),
    }
    const wrapper = mount(ResultsPanel, { props: { evaluation, chosenALevels: [], hasFacts: true } })

    expect(wrapper.text()).toContain('Not eligible.')
    expect(wrapper.text()).toContain('Too young.')
  })

  it('shows the choice limit notice when present', () => {
    const evaluation: EnrolmentEvaluateResponse = {
      validationErrors: [],
      result: apiResult({ choiceLimitReason: 'Already chosen 4 subjects.' }),
    }
    const wrapper = mount(ResultsPanel, { props: { evaluation, chosenALevels: [], hasFacts: true } })

    expect(wrapper.find('#choice-limit-notice').text()).toContain('Already chosen 4 subjects.')
  })
})
