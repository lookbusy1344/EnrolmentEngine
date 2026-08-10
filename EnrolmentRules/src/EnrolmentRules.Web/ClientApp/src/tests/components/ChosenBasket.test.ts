import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import type { ExplanationResponse } from '../../api/contracts'
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

describe('ChosenBasket', () => {
  it('says the basket is empty when nothing is chosen', () => {
    const wrapper = mount(ChosenBasket, { props: { chosenALevels: [], explanations: [] } })

    expect(wrapper.text()).toContain('None chosen yet.')
    expect(wrapper.find('#borderline-notice').exists()).toBe(false)
  })

  it('renders a green choice as a plain pill with no borderline notice', () => {
    const wrapper = mount(ChosenBasket, {
      props: { chosenALevels: ['history'], explanations: [explanation('history', 'History', 'Green')] },
    })

    const pill = wrapper.get('li')
    expect(pill.classes()).toContain('text-bg-primary')
    expect(pill.text()).toBe('History')
    expect(wrapper.find('#borderline-notice').exists()).toBe(false)
  })

  it('keeps an amber choice on an amber pill, marks it borderline and explains why once', () => {
    const wrapper = mount(ChosenBasket, {
      props: {
        chosenALevels: ['music', 'history'],
        explanations: [explanation('music', 'Music', 'Amber'), explanation('history', 'History', 'Green')],
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
    const wrapper = mount(ChosenBasket, { props: { chosenALevels: ['music'], explanations: [] } })

    const pill = wrapper.get('li')
    expect(pill.classes()).toContain('text-bg-primary')
    expect(pill.text()).toBe('Music')
    expect(wrapper.find('#borderline-notice').exists()).toBe(false)
  })
})
