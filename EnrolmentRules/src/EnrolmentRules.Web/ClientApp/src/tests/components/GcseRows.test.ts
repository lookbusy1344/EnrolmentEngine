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

  // Normalising per keystroke would make "8.5" untypeable — "8." reads back as 8 and would be
  // rewritten before the user reaches the decimal — so the field only normalises on blur.
  it.each([
    ['47', 9],
    ['0', 1],
    ['7.6', 8],
    ['-3', 1],
  ])('normalises a grade of %s to %d when the field is committed', async (typed, expected) => {
    const rows: GcseRow[] = [{ subject: 'maths', grade: 8 }]
    const wrapper = mount(GcseRows, { props: { rows, subjectOptions, 'onUpdate:rows': () => undefined } })

    const grade = wrapper.get('input[type="number"]')
    await grade.setValue(typed)
    await grade.trigger('change')

    expect(rows[0]).toEqual({ subject: 'maths', grade: expected })
  })

  // setValue fires input *and* change, i.e. a commit — a keystroke is an input event on its own.
  it('leaves a part-typed grade alone while the field still has focus', async () => {
    const rows: GcseRow[] = [{ subject: 'maths', grade: null }]
    const wrapper = mount(GcseRows, { props: { rows, subjectOptions, 'onUpdate:rows': () => undefined } })

    const grade = wrapper.get('input[type="number"]')
    ;(grade.element as HTMLInputElement).value = '47'
    await grade.trigger('input')

    expect(rows[0].grade).toBe(47)
  })

  it('shows the 1-9 scale as placeholder text', () => {
    const wrapper = mount(GcseRows, { props: { rows: [], subjectOptions, 'onUpdate:rows': () => undefined } })

    expect(wrapper.get('input[type="number"]').attributes('placeholder')).toBe('1-9')
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
