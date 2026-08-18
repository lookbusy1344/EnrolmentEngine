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

  it('hides the GCSE scoreboard until a graded GCSE is entered', () => {
    const wrapper = mount(ChosenBasket, {
      props: { chosenALevels: [], explanations: [], choiceStatuses: [], gcses: [{ subject: 'maths', grade: null }] },
    })

    expect(wrapper.find('#gcse-scoreboard').exists()).toBe(false)
  })

  it('shows GCSE count, total and average over the graded rows', () => {
    const wrapper = mount(ChosenBasket, {
      props: {
        chosenALevels: [],
        explanations: [],
        choiceStatuses: [],
        gcses: [
          { subject: 'maths', grade: 8 },
          { subject: 'physics', grade: 7 },
          { subject: 'chemistry', grade: null },
        ],
      },
    })

    expect(wrapper.find('#gcse-scoreboard').exists()).toBe(true)
    expect(wrapper.get('[data-testid="scoreboard-count"]').text()).toBe('2')
    expect(wrapper.get('[data-testid="scoreboard-total"]').text()).toBe('15')
    expect(wrapper.get('[data-testid="scoreboard-average"]').text()).toBe('7.5')
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
    expect(pill.text()).toContain('History')
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

    // Alphabetical, not insertion order: History (green) sorts before Music (amber).
    const [green, amber] = wrapper.findAll('li')
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
    expect(pill.text()).toContain('Music')
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
    expect(pill.text()).toContain('Music - Unavailable')
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
    expect(pill.text()).toContain('Art - Not offered')
  })

  it('lists valid choices before invalid ones, alphabetical within each group', () => {
    const wrapper = mount(ChosenBasket, {
      props: {
        // Chosen in an order that would be wrong under a naive alphabetical-only sort: an
        // alphabetically-early invalid choice (Art) must still land after the valid group.
        chosenALevels: ['art', 'music', 'biology', 'sociology'],
        explanations: [explanation('music', 'Music', 'Green'), explanation('biology', 'Biology', 'Green')],
        choiceStatuses: [
          notOffered('art', 'Art', 'Not offered under Elite.'),
          available('music', 'Music'),
          available('biology', 'Biology'),
          unavailable('sociology', 'Sociology', 'GCSE grades no longer meet the entry rule.'),
        ],
      },
    })

    const labels = wrapper.findAll('li').map((li) => li.text())
    expect(labels[0]).toContain('Biology')
    expect(labels[1]).toContain('Music')
    expect(labels[2]).toContain('Art')
    expect(labels[3]).toContain('Sociology')
  })

  it('lists green choices, then amber, then red, alphabetical within each colour', () => {
    const wrapper = mount(ChosenBasket, {
      props: {
        chosenALevels: ['sociology', 'art', 'music', 'biology'],
        explanations: [
          explanation('sociology', 'Sociology', 'Green'),
          explanation('art', 'Art', 'Amber'),
          explanation('music', 'Music', 'Amber'),
          explanation('biology', 'Biology', 'Green'),
        ],
        choiceStatuses: [
          available('sociology', 'Sociology'),
          available('art', 'Art'),
          available('music', 'Music'),
          available('biology', 'Biology'),
        ],
      },
    })

    const labels = wrapper.findAll('li').map((li) => li.text())
    expect(labels[0]).toContain('Biology')
    expect(labels[1]).toContain('Sociology')
    expect(labels[2]).toContain('Art')
    expect(labels[3]).toContain('Music')
  })

  it('emits remove with the subject when a pill x is clicked', async () => {
    const wrapper = mount(ChosenBasket, {
      props: {
        chosenALevels: ['music', 'history'],
        explanations: [explanation('music', 'Music', 'Green'), explanation('history', 'History', 'Green')],
        choiceStatuses: [available('music', 'Music'), available('history', 'History')],
      },
    })

    // Alphabetical, not insertion order: History sorts before Music, so the 2nd pill is Music.
    await wrapper.get('li:nth-child(2) button.basket-remove').trigger('click')

    expect(wrapper.emitted('remove')).toEqual([['music']])
    expect(wrapper.emitted('clear')).toBeUndefined()
  })

  it('empties the basket only after the two-step confirmation', async () => {
    const wrapper = mount(ChosenBasket, {
      props: {
        chosenALevels: ['music', 'history'],
        explanations: [explanation('music', 'Music', 'Green'), explanation('history', 'History', 'Green')],
        choiceStatuses: [available('music', 'Music'), available('history', 'History')],
      },
    })

    // No native confirm dialog; the confirm is inline and hidden until asked for.
    expect(wrapper.find('#basket-empty-confirm').exists()).toBe(false)

    await wrapper.get('#basket-empty').trigger('click')
    expect(wrapper.find('#basket-empty-confirm').exists()).toBe(true)
    expect(wrapper.emitted('clear')).toBeUndefined()

    await wrapper.get('#basket-empty-confirm button.btn-danger').trigger('click')
    expect(wrapper.emitted('clear')).toEqual([[]])
  })

  it('cancels the empty confirmation without emitting clear', async () => {
    const wrapper = mount(ChosenBasket, {
      props: {
        chosenALevels: ['music'],
        explanations: [explanation('music', 'Music', 'Green')],
        choiceStatuses: [available('music', 'Music')],
      },
    })

    await wrapper.get('#basket-empty').trigger('click')
    await wrapper.get('#basket-empty-confirm button.btn-outline-secondary').trigger('click')

    expect(wrapper.find('#basket-empty-confirm').exists()).toBe(false)
    expect(wrapper.get('#basket-empty').isVisible()).toBe(true)
    expect(wrapper.emitted('clear')).toBeUndefined()
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
      'Too many subjects chosen: this policy allows at most 3, but your basket contains 4. Remove a choice.',
    )
  })
})
