import { describe, expect, it } from 'vitest'
import { prettify, wholeYears } from '../display/formatting'

describe('prettify', () => {
  it.each([
    ['english_language', 'English Language'],
    ['physics', 'Physics'],
    ['further_maths', 'Further Maths'],
    ['', ''],
  ])('formats %s as %s', (key, expected) => {
    expect(prettify(key)).toBe(expected)
  })
})

describe('wholeYears', () => {
  it('counts a birthday already passed this year', () => {
    expect(wholeYears('2009-09-01', new Date(2026, 8, 2))).toBe(17)
  })

  it('does not count a birthday not yet reached this year', () => {
    expect(wholeYears('2009-09-01', new Date(2026, 7, 31))).toBe(16)
  })

  it('counts the birthday itself', () => {
    expect(wholeYears('2009-09-01', new Date(2026, 8, 1))).toBe(17)
  })
})
