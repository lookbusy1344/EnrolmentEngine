import { describe, expect, it } from 'vitest'
import type { ExplanationResponse } from '../api/contracts'
import { orderForCards, prettify, wholeYears } from '../display/formatting'

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

describe('orderForCards', () => {
  it('sorts green before amber before red, then alphabetically by label within each colour', () => {
    const explanations = [
      explanation('physics', 'Physics', 'Green'),
      explanation('art', 'Art', 'Amber'),
      explanation('biology', 'Biology', 'Green'),
      explanation('further_maths', 'Further Maths', 'Red'),
      explanation('chemistry', 'Chemistry', 'Amber'),
    ]

    const ordered = orderForCards(explanations).map((e) => e.subject.value)

    expect(ordered).toEqual(['biology', 'physics', 'art', 'chemistry', 'further_maths'])
  })

  it('does not mutate the input array', () => {
    const explanations = [explanation('physics', 'Physics', 'Green'), explanation('art', 'Art', 'Amber')]
    const original = [...explanations]

    orderForCards(explanations)

    expect(explanations).toEqual(original)
  })
})
