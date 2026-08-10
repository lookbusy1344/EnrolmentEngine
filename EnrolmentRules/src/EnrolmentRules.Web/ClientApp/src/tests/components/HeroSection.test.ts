import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import HeroSection from '../../components/HeroSection.vue'

describe('HeroSection', () => {
  it('renders the eyebrow, heading and lede matching the Razor hero section', () => {
    const wrapper = mount(HeroSection)

    expect(wrapper.find('.hero-eyebrow').text()).toContain('GCSEs in → A-Levels out')
    expect(wrapper.get('#hero-heading').text()).toContain('See how your skills can')
    expect(wrapper.find('.hero-lede').text()).toContain('enrolment engine')
  })

  it('marks itself as the dynamic front-end and links to the server-rendered one', () => {
    const wrapper = mount(HeroSection)

    expect(wrapper.get('.mode-tag').text()).toBe('Dynamic')
    expect(wrapper.get('.mode-switch').attributes('href')).toBe('/razor')
  })

  it('renders the animated sprout SVG', () => {
    const wrapper = mount(HeroSection)

    const sprout = wrapper.find('svg.sprout')
    expect(sprout.exists()).toBe(true)
    expect(sprout.find('.stem').exists()).toBe(true)
    expect(sprout.find('.leaf-a').exists()).toBe(true)
    expect(sprout.find('.leaf-b').exists()).toBe(true)
    expect(sprout.find('.leaf-c').exists()).toBe(true)
    expect(sprout.find('.soil').exists()).toBe(true)
  })
})
