import { describe, expect, it } from 'vitest'
import {
  type EnrolmentSnapshot,
  gcseScoreboard,
  isEmptyGcseRow,
  isEmptyPriorQualificationRow,
  isGcseSubjectChosenElsewhere,
  toEvaluateRequest,
} from '../state/enrolmentState'

describe('isEmptyGcseRow', () => {
  it('is empty with no subject and no grade', () => {
    expect(isEmptyGcseRow({ subject: '', grade: null })).toBe(true)
  })

  it('is not empty once either field is set', () => {
    expect(isEmptyGcseRow({ subject: 'maths', grade: null })).toBe(false)
    expect(isEmptyGcseRow({ subject: '', grade: 7 })).toBe(false)
  })
})

describe('isEmptyPriorQualificationRow', () => {
  it('is empty with no subject, type or grade', () => {
    expect(isEmptyPriorQualificationRow({ subject: '', type: '', grade: '' })).toBe(true)
  })

  it('is not empty once any field is set', () => {
    expect(isEmptyPriorQualificationRow({ subject: 'applied_science', type: '', grade: '' })).toBe(false)
  })
})

describe('isGcseSubjectChosenElsewhere', () => {
  const rows = [
    { subject: 'maths', grade: 8 },
    { subject: 'physics', grade: 7 },
  ]

  it('is true when another row already names the subject', () => {
    expect(isGcseSubjectChosenElsewhere(rows, 1, 'maths')).toBe(true)
  })

  it('excludes the row at the given index from the check', () => {
    expect(isGcseSubjectChosenElsewhere(rows, 0, 'maths')).toBe(false)
  })

  it('is false for a subject named nowhere', () => {
    expect(isGcseSubjectChosenElsewhere(rows, 0, 'chemistry')).toBe(false)
  })
})

describe('gcseScoreboard', () => {
  it('is all zero with no graded rows', () => {
    expect(gcseScoreboard([])).toEqual({ count: 0, total: 0, average: 0 })
  })

  it('sums and averages the graded rows', () => {
    const board = gcseScoreboard([
      { subject: 'maths', grade: 8 },
      { subject: 'physics', grade: 7 },
      { subject: 'chemistry', grade: 6 },
    ])

    expect(board).toEqual({ count: 3, total: 21, average: 7 })
  })

  it('normalises raw grades onto the 1-9 scale before scoring', () => {
    const board = gcseScoreboard([
      { subject: 'maths', grade: 8.6 },
      { subject: 'physics', grade: 12 },
    ])

    // 8.6 rounds to 9, 12 clamps to 9.
    expect(board).toEqual({ count: 2, total: 18, average: 9 })
  })

  it('ignores half-filled rows: no subject, or no grade', () => {
    const board = gcseScoreboard([
      { subject: 'maths', grade: 8 },
      { subject: '', grade: 7 },
      { subject: 'physics', grade: null },
    ])

    expect(board).toEqual({ count: 1, total: 8, average: 8 })
  })
})

describe('toEvaluateRequest', () => {
  it('filters blank rows before sending', () => {
    const snapshot: EnrolmentSnapshot = {
      dateOfBirth: '2009-09-01',
      gcses: [
        { subject: 'maths', grade: 8 },
        { subject: '', grade: null },
      ],
      priorQualifications: [
        { subject: 'applied_science', type: 'BtecDiploma', grade: 'Merit' },
        { subject: '', type: '', grade: '' },
      ],
      hobbies: ['chess_club', ''],
      chosenALevels: ['physics'],
    }

    const request = toEvaluateRequest(snapshot)

    expect(request.gcses).toEqual([{ subject: 'maths', grade: 8 }])
    expect(request.priorQualifications).toEqual([{ subject: 'applied_science', type: 'BtecDiploma', grade: 'Merit' }])
    expect(request.hobbies).toEqual(['chess_club'])
    expect(request.chosenALevels).toEqual(['physics'])
    expect(request.dateOfBirth).toBe('2009-09-01')
  })

  // The endpoint's contract is int?, and the domain validator rejects anything off the 1-9 scale, so a
  // grade still mid-edit (typed but not yet blurred, or restored from an older snapshot) must be
  // normalised here rather than 400-ing on a decimal or surfacing a validation error.
  it('normalises grades onto the 1-9 integer scale before sending', () => {
    const snapshot: EnrolmentSnapshot = {
      dateOfBirth: '2009-09-01',
      gcses: [
        { subject: 'maths', grade: 47 },
        { subject: 'physics', grade: 7.6 },
        { subject: 'chemistry', grade: 0 },
      ],
      priorQualifications: [],
      hobbies: [],
      chosenALevels: [],
    }

    const request = toEvaluateRequest(snapshot)

    expect(request.gcses).toEqual([
      { subject: 'maths', grade: 9 },
      { subject: 'physics', grade: 8 },
      { subject: 'chemistry', grade: 1 },
    ])
  })
})
