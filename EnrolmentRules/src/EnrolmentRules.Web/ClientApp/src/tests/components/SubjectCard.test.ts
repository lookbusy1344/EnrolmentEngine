import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import type { ExplanationResponse } from '../../api/contracts'
import SubjectCard from '../../components/SubjectCard.vue'

const baseExplanation: ExplanationResponse = {
  subject: { value: 'further_maths', label: 'Further Maths' },
  rating: 'Red',
  ratingCssClass: 'text-bg-danger',
  reason: 'Restudy bar applies.',
  baseRating: 'Green',
  baseReason: 'Base table reason',
  rule: 'further_maths.entry',
  predictedPoints: 3.5,
  entryEquivalentReason: null,
  overrides: [],
}

describe('SubjectCard', () => {
  it('renders a red subject as unavailable, with the rating visible as text', () => {
    const wrapper = mount(SubjectCard, { props: { explanation: baseExplanation, chosen: false } })

    const unavailable = wrapper.get('button')
    expect(unavailable.text()).toContain('Unavailable')
    expect(unavailable.attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('Red')
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
