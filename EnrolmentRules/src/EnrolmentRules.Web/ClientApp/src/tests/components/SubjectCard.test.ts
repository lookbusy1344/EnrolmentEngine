import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import type { ExplanationResponse } from '../../api/contracts'
import SubjectCard from '../../components/SubjectCard.vue'

const baseExplanation: ExplanationResponse = {
  subject: { value: 'further_maths', label: 'Further Maths' },
  rating: 'Red',
  ratingCssClass: 'text-bg-danger',
  message: 'Amber needs GCSE Maths at grade 7 or above (yours: 6).',
}

describe('SubjectCard', () => {
  it('renders a red subject as unavailable, with the rating visible as text', () => {
    const wrapper = mount(SubjectCard, { props: { explanation: baseExplanation, chosen: false } })

    const unavailable = wrapper.get('button')
    expect(unavailable.text()).toContain('Unavailable')
    expect(unavailable.attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('Red')
  })

  it('shows the message as the only explanation', () => {
    const wrapper = mount(SubjectCard, { props: { explanation: baseExplanation, chosen: false } })

    expect(wrapper.get('.card-text').text()).toBe('Amber needs GCSE Maths at grade 7 or above (yours: 6).')
    expect(wrapper.find('details').exists()).toBe(false)
  })

  it('describes the unavailable button by the message', () => {
    const wrapper = mount(SubjectCard, { props: { explanation: baseExplanation, chosen: false } })

    const describedBy = wrapper.get('button').attributes('aria-describedby')
    expect(wrapper.get(`#${describedBy ?? ''}`).text()).toBe('Amber needs GCSE Maths at grade 7 or above (yours: 6).')
  })

  it('emits choose for a green subject not yet chosen', async () => {
    const green: ExplanationResponse = { ...baseExplanation, rating: 'Green', ratingCssClass: 'text-bg-success' }
    const wrapper = mount(SubjectCard, { props: { explanation: green, chosen: false } })

    await wrapper.get('button').trigger('click')

    expect(wrapper.emitted('choose')).toEqual([['further_maths']])
  })

  it('emits remove for a chosen subject', async () => {
    const green: ExplanationResponse = { ...baseExplanation, rating: 'Green', ratingCssClass: 'text-bg-success' }
    const wrapper = mount(SubjectCard, { props: { explanation: green, chosen: true } })

    await wrapper.get('button').trigger('click')

    expect(wrapper.emitted('remove')).toEqual([['further_maths']])
  })
})
