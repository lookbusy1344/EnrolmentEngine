import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import type { EnrolmentEvaluateResponse } from '../../api/contracts'
import ResultsPanel from '../../components/ResultsPanel.vue'

describe('ResultsPanel', () => {
  it('shows validation errors as visible text, not colour alone', () => {
    const evaluation: EnrolmentEvaluateResponse = {
      validationErrors: ['Date of birth is required.'],
      ejectedChoices: [],
      result: null,
    }
    const wrapper = mount(ResultsPanel, { props: { evaluation, chosenALevels: [], hasFacts: true } })

    expect(wrapper.text()).toContain('Date of birth is required.')
  })

  it('shows an ineligible eligibility reason as visible text', () => {
    const evaluation: EnrolmentEvaluateResponse = {
      validationErrors: [],
      ejectedChoices: [],
      result: { eligible: false, eligibilityReasons: ['Too young.'], choiceLimitReason: null, explanations: [] },
    }
    const wrapper = mount(ResultsPanel, { props: { evaluation, chosenALevels: [], hasFacts: true } })

    expect(wrapper.text()).toContain('Not eligible.')
    expect(wrapper.text()).toContain('Too young.')
  })

  it('shows the choice limit notice when present', () => {
    const evaluation: EnrolmentEvaluateResponse = {
      validationErrors: [],
      ejectedChoices: [],
      result: {
        eligible: true,
        eligibilityReasons: [],
        choiceLimitReason: 'Already chosen 4 subjects.',
        explanations: [],
      },
    }
    const wrapper = mount(ResultsPanel, { props: { evaluation, chosenALevels: [], hasFacts: true } })

    expect(wrapper.find('#choice-limit-notice').text()).toContain('Already chosen 4 subjects.')
  })
})
