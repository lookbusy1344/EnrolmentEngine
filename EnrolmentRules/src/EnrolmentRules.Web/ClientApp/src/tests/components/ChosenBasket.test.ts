import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import type { ChoiceStatus, ExplanationResponse } from '../../api/contracts'
import ChosenBasket from '../../components/ChosenBasket.vue'

function explanation(value: string, label: string, rating: string): ExplanationResponse {
  return {
    subject: { value, label },
    rating,
    ratingCssClass: '',
    reason: 'Reason',
    baseRating: rating,
    baseReason: 'Base reason',
    rule: `${value}.entry`,
    predictedPoints: 4,
    entryEquivalentReason: null,
    overrides: [],
  }
}

function available(value: string, label: string): ChoiceStatus {
  return { subject: { value, label }, status: 'Available', reason: null }
}

function unavailable(value: string, label: string, reason: string): ChoiceStatus {
  return { subject: { value, label }, status: 'Unavailable', reason }
}

function notOffered(value: string, label: string, reason: string): ChoiceStatus {
  return { subject: { value, label }, status: 'NotOffered', reason }
}

describe('ChosenBasket', () => {
  it('says the basket is empty when nothing is chosen', () => {
    const wrapper = mount(ChosenBasket, { props: { chosenALevels: [], explanations: [], choiceStatuses: [] } })

    expect(wrapper.text()).toContain('None chosen yet.')
    expect(wrapper.find('#borderline-notice').exists()).toBe(false)
  })

  it('renders a green available choice as a plain pill with no borderline notice', () => {
    const wrapper = mount(ChosenBasket, {
      props: {
        chosenALevels: ['history'],
        explanations: [explanation('history', 'History', 'Green')],
        choiceStatuses: [available('history', 'History')],
      },
    })

    const pill = wrapper.get('li')
    expect(pill.classes()).toContain('text-bg-primary')
    expect(pill.text()).toBe('History')
    expect(wrapper.find('#borderline-notice').exists()).toBe(false)
  })

  it('keeps an amber available choice on an amber pill, marks it borderline and explains why once', () => {
    const wrapper = mount(ChosenBasket, {
      props: {
        chosenALevels: ['music', 'history'],
        explanations: [explanation('music', 'Music', 'Amber'), explanation('history', 'History', 'Green')],
        choiceStatuses: [available('music', 'Music'), available('history', 'History')],
      },
    })

    const [amber, green] = wrapper.findAll('li')
    expect(amber.classes()).toContain('text-bg-warning')
    expect(amber.text()).toContain('Music')
    expect(amber.text()).toContain('Borderline')
    expect(green.classes()).toContain('text-bg-primary')
    expect(green.text()).not.toContain('Borderline')

    const notice = wrapper.get('#borderline-notice')
    expect(notice.text()).toContain('additional authorisation')
  })

  it('falls back to a plain pill when the subject has no rating yet', () => {
    const wrapper = mount(ChosenBasket, {
      props: { chosenALevels: ['music'], explanations: [], choiceStatuses: [available('music', 'Music')] },
    })

    const pill = wrapper.get('li')
    expect(pill.classes()).toContain('text-bg-primary')
    expect(pill.text()).toBe('Music')
    expect(wrapper.find('#borderline-notice').exists()).toBe(false)
  })

  it('keeps an unavailable choice in the basket, flagged red, rather than dropping it', () => {
    const wrapper = mount(ChosenBasket, {
      props: {
        chosenALevels: ['music'],
        explanations: [explanation('music', 'Music', 'Red')],
        choiceStatuses: [unavailable('music', 'Music', 'GCSE grades no longer meet the entry rule.')],
      },
    })

    const pill = wrapper.get('li')
    expect(pill.classes()).toContain('text-bg-danger')
    expect(pill.text()).toBe('Music - Unavailable')
    expect(wrapper.find('#borderline-notice').exists()).toBe(false)
  })

  it('keeps a not-offered choice in the basket, flagged distinctly, when the policy has no catalogue entry for it', () => {
    const wrapper = mount(ChosenBasket, {
      props: {
        chosenALevels: ['art'],
        explanations: [],
        choiceStatuses: [notOffered('art', 'Art', 'Not offered under Elite.')],
      },
    })

    const pill = wrapper.get('li')
    expect(pill.classes()).toContain('text-bg-danger')
    expect(pill.text()).toBe('Art - Not offered')
  })

  it('marks the whole basket invalid when a policy switch makes too many choices available', async () => {
    const subjects = ['biology', 'chemistry', 'physics', 'maths']
    const wrapper = mount(ChosenBasket, {
      props: {
        chosenALevels: subjects,
        explanations: subjects.map((subject) => explanation(subject, subject, 'Green')),
        choiceStatuses: [
          available('biology', 'biology'),
          available('chemistry', 'chemistry'),
          unavailable('physics', 'physics', 'barred'),
          unavailable('maths', 'maths', 'barred'),
        ],
        maxChoices: 3,
      },
    })

    expect(wrapper.find('#basket-choice-limit-error').exists()).toBe(false)

    await wrapper.setProps({ choiceStatuses: subjects.map((subject) => available(subject, subject)) })

    expect(wrapper.get('.chosen-summary').classes()).toContain('bg-danger-subtle')
    expect(wrapper.get('#basket-choice-limit-error').text()).toBe(
      'Too many available choices: this policy allows at most 3, but your basket contains 4. Remove a choice.',
    )
  })
})
