import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import type { EnrolmentApiResult, EnrolmentEvaluateResponse, ExplanationResponse } from '../../api/contracts'
import ResultsPanel from '../../components/ResultsPanel.vue'

function explanation(value: string, label: string, rating: string): ExplanationResponse {
  return {
    subject: { value, label },
    rating,
    ratingCssClass: '',
    reason: '',
    baseRating: rating,
    baseReason: '',
    rule: '',
    predictedPoints: 0,
    entryEquivalentReason: null,
    overrides: [],
  }
}

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

  it('renders subject cards sorted green, then amber, then red, alphabetically within each colour', () => {
    const evaluation: EnrolmentEvaluateResponse = {
      validationErrors: [],
      result: apiResult({
        explanations: [
          explanation('physics', 'Physics', 'Green'),
          explanation('art', 'Art', 'Amber'),
          explanation('biology', 'Biology', 'Green'),
          explanation('further_maths', 'Further Maths', 'Red'),
          explanation('chemistry', 'Chemistry', 'Amber'),
        ],
      }),
    }
    const wrapper = mount(ResultsPanel, { props: { evaluation, chosenALevels: [], hasFacts: true } })

    const headings = wrapper.findAll('.card-title').map((heading) => heading.text())

    expect(headings).toEqual([
      expect.stringContaining('Biology'),
      expect.stringContaining('Physics'),
      expect.stringContaining('Art'),
      expect.stringContaining('Chemistry'),
      expect.stringContaining('Further Maths'),
    ])
  })
})
