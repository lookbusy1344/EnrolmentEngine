import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import type { PolicyDescriptor } from '../../api/contracts'
import HeroSection from '../../components/HeroSection.vue'

const standard: PolicyDescriptor = { id: 'standard', displayName: 'Standard' }
const elite: PolicyDescriptor = { id: 'elite', displayName: 'Elite' }

describe('HeroSection', () => {
  it('renders the eyebrow, heading and lede', () => {
    const wrapper = mount(HeroSection, { props: { selectedPolicy: null, availablePolicies: [] } })

    expect(wrapper.find('.hero-eyebrow').text()).toContain('GCSEs in → A-Levels out')
    expect(wrapper.get('#hero-heading').text()).toContain('See how your skills can')
    expect(wrapper.find('.hero-lede').text()).toContain('enrolment engine')
  })

  it('shows the current policy label', () => {
    const wrapper = mount(HeroSection, { props: { selectedPolicy: standard, availablePolicies: [standard, elite] } })

    expect(wrapper.get('.policy-switch').text()).toContain('Standard')
    expect(wrapper.get('.policy-switch').text()).toContain('Switch to Elite')
  })

  it('emits switch-policy with the other policy id when the switch link is clicked', async () => {
    const wrapper = mount(HeroSection, { props: { selectedPolicy: standard, availablePolicies: [standard, elite] } })

    await wrapper.get('.policy-switch a').trigger('click')

    expect(wrapper.emitted('switch-policy')).toEqual([['elite']])
  })

  it('shows no switch link when only one policy is available', () => {
    const wrapper = mount(HeroSection, { props: { selectedPolicy: standard, availablePolicies: [standard] } })

    expect(wrapper.find('.policy-switch a').exists()).toBe(false)
  })

  it('renders the animated sprout SVG', () => {
    const wrapper = mount(HeroSection, { props: { selectedPolicy: null, availablePolicies: [] } })

    const sprout = wrapper.find('svg.sprout')
    expect(sprout.exists()).toBe(true)
    expect(sprout.find('.stem').exists()).toBe(true)
    expect(sprout.find('.leaf-a').exists()).toBe(true)
    expect(sprout.find('.leaf-b').exists()).toBe(true)
    expect(sprout.find('.leaf-c').exists()).toBe(true)
    expect(sprout.find('.soil').exists()).toBe(true)
  })
})
