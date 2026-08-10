import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import type { OptionItem } from '../../api/contracts'
import GcseRows from '../../components/GcseRows.vue'
import type { GcseRow } from '../../state/enrolmentState'

const subjectOptions: readonly OptionItem[] = [
  { value: 'maths', label: 'Maths' },
  { value: 'physics', label: 'Physics' },
  { value: 'chemistry', label: 'Chemistry' },
]

describe('GcseRows', () => {
  it('always keeps one trailing blank row', () => {
    const wrapper = mount(GcseRows, { props: { rows: [], subjectOptions, 'onUpdate:rows': () => undefined } })

    expect(wrapper.findAll('select').length).toBe(1)
  })

  it('adding a subject to the blank row appends a new blank row', async () => {
    const rows: GcseRow[] = []
    const wrapper = mount(GcseRows, { props: { rows, subjectOptions, 'onUpdate:rows': () => undefined } })

    await wrapper.get('select').setValue('maths')

    expect(rows).toEqual([
      { subject: 'maths', grade: null },
      { subject: '', grade: null },
    ])
  })

  it('removing a row preserves neighbouring row values', async () => {
    const rows: GcseRow[] = [
      { subject: 'maths', grade: 8 },
      { subject: 'physics', grade: 7 },
    ]
    const wrapper = mount(GcseRows, { props: { rows, subjectOptions, 'onUpdate:rows': () => undefined } })

    await wrapper.get('button').trigger('click')

    expect(rows[0]).toEqual({ subject: 'physics', grade: 7 })
  })

  it('renders one grade toggle button per row for each grade on the 1-9 scale', () => {
    const wrapper = mount(GcseRows, { props: { rows: [], subjectOptions, 'onUpdate:rows': () => undefined } })

    const gradeInputs = wrapper.findAll('input[type="radio"]')
    expect(gradeInputs.map((input) => input.attributes('id'))).toEqual([
      'gcse-grade-0-1',
      'gcse-grade-0-2',
      'gcse-grade-0-3',
      'gcse-grade-0-4',
      'gcse-grade-0-5',
      'gcse-grade-0-6',
      'gcse-grade-0-7',
      'gcse-grade-0-8',
      'gcse-grade-0-9',
    ])
  })

  // site.css's .gcse-grade-picker gives each button a fixed fifth of the row below Bootstrap's md
  // breakpoint; without it the group wraps ragged on a phone. /razor carries the same class.
  it('marks the grade group with the shared phone-layout class', () => {
    const wrapper = mount(GcseRows, { props: { rows: [], subjectOptions, 'onUpdate:rows': () => undefined } })

    expect(wrapper.get('.btn-group').classes()).toContain('gcse-grade-picker')
  })

  it('clicking a grade button sets that row’s grade', async () => {
    const rows: GcseRow[] = [{ subject: 'maths', grade: null }]
    const wrapper = mount(GcseRows, { props: { rows, subjectOptions, 'onUpdate:rows': () => undefined } })

    await wrapper.get('#gcse-grade-0-7').setValue()

    expect(rows[0]).toEqual({ subject: 'maths', grade: 7 })
  })

  it('marks only the row’s current grade as checked', () => {
    const rows: GcseRow[] = [{ subject: 'maths', grade: 7 }]
    const wrapper = mount(GcseRows, { props: { rows, subjectOptions, 'onUpdate:rows': () => undefined } })

    const checked = wrapper
      .findAll('input[type="radio"]')
      .filter((input) => (input.element as HTMLInputElement).checked)

    expect(checked.map((input) => input.attributes('id'))).toEqual(['gcse-grade-0-7'])
  })

  it('hides a subject already chosen in another row from that row’s options', () => {
    const rows: GcseRow[] = [
      { subject: 'maths', grade: 8 },
      { subject: '', grade: null },
    ]
    const wrapper = mount(GcseRows, { props: { rows, subjectOptions, 'onUpdate:rows': () => undefined } })

    const selects = wrapper.findAll('select')
    expect(selects.length).toBeGreaterThanOrEqual(2)
    const secondRowOptions = selects[1]?.findAll('option').map((option) => option.attributes('value'))

    expect(secondRowOptions).not.toContain('maths')
  })
})
