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

  // A row with no subject has nothing to grade: the control is hidden in place (Bootstrap's
  // .invisible), so the column keeps its width and the rows stay in line.
  it('hides the grade control until the row has a subject', () => {
    const rows: GcseRow[] = [{ subject: 'maths', grade: null }]
    const wrapper = mount(GcseRows, { props: { rows, subjectOptions, 'onUpdate:rows': () => undefined } })

    const columns = wrapper.findAll('.gcse-grade-picker').map((wheel) => wheel.element.parentElement)

    expect(columns.map((column) => column?.classList.contains('invisible'))).toEqual([false, true])
  })

  // The grade control is the shared wheel (site.css .gwheel), driven through the same hidden radio
  // group /razor renders; the wheel container carries both classes. /razor carries them too.
  it('renders the grade as a wheel over the shared radio group', () => {
    const wrapper = mount(GcseRows, { props: { rows: [], subjectOptions, 'onUpdate:rows': () => undefined } })

    const wheel = wrapper.get('.gcse-grade-picker')
    expect(wheel.classes()).toContain('gwheel')
    expect(wheel.get('[role="slider"]').attributes('aria-valuemax')).toBe('9')
  })

  it('offers a rest cell and clearing a grade returns the row to “no grade”', async () => {
    const rows: GcseRow[] = [{ subject: 'maths', grade: 7 }]
    const wrapper = mount(GcseRows, { props: { rows, subjectOptions, 'onUpdate:rows': () => undefined } })

    await wrapper.get('.gwheel__cell--unset').trigger('click')

    expect(rows[0]).toEqual({ subject: 'maths', grade: null })
  })

  it('reads the chosen grade out beside the label, and shows “Not set” when empty', () => {
    const set = mount(GcseRows, {
      props: { rows: [{ subject: 'maths', grade: 7 }], subjectOptions, 'onUpdate:rows': () => undefined },
    })
    expect(set.get('.gwheel-readout').text()).toBe('7')

    const empty = mount(GcseRows, {
      props: { rows: [{ subject: 'maths', grade: null }], subjectOptions, 'onUpdate:rows': () => undefined },
    })
    expect(empty.get('.gwheel-readout').text()).toBe('Not set')
  })

  it('tapping a grade cell sets that row’s grade', async () => {
    const rows: GcseRow[] = [{ subject: 'maths', grade: null }]
    const wrapper = mount(GcseRows, { props: { rows, subjectOptions, 'onUpdate:rows': () => undefined } })

    await wrapper.get('label[for="gcse-grade-0-7"]').trigger('click')

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
