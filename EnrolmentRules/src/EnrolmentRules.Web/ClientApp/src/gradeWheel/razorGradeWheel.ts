// /razor progressive enhancement: upgrade each server-rendered GCSE grade button group into the
// same spin wheel /app renders natively. The server markup is a plain radio+label button group that
// posts a grade with no script (the no-JS state); this script restructures it in place and drives
// the radios, so the form still posts exactly the grade under the lens. The scroll/keyboard/drag
// behaviour is the shared controller.ts, so both front ends behave identically.

import { applyTrackAria, createWheelController } from './controller'

interface WheelCell {
  el: HTMLElement
  /** null for the "Not set" cell, else the grade this cell selects. */
  grade: number | null
  /** The radio this cell posts through, absent on the "Not set" cell. */
  radio: HTMLInputElement | null
}

function enhance(container: HTMLElement): void {
  const radios = Array.from(container.querySelectorAll<HTMLInputElement>('input[type="radio"]'))
  if (radios.length === 0) {
    return
  }
  const labelledBy = container.getAttribute('aria-labelledby')

  container.classList.add('gwheel')
  // The button group's own layout classes go with it: flex-grow-1 is !important and would stretch
  // the drum past its fixed width, showing far more than the five grades /app shows.
  container.classList.remove('btn-group', 'btn-group-sm', 'flex-wrap', 'flex-grow-1')

  const track = document.createElement('div')
  track.className = 'gwheel__track'
  track.setAttribute('role', 'slider')
  track.setAttribute('tabindex', '0')
  track.setAttribute('aria-valuemin', '1')
  track.setAttribute('aria-valuemax', '9')
  if (labelledBy !== null) {
    track.setAttribute('aria-labelledby', labelledBy)
  }
  const readout =
    labelledBy === null
      ? null
      : (document.getElementById(labelledBy)?.querySelector<HTMLElement>('[data-grade-readout]') ?? null)

  track.append(makePad())
  const cells: WheelCell[] = [buildUnsetCell()]
  track.append(cells[0].el)
  for (const radio of radios) {
    const cell = buildGradeCell(container, radio)
    cells.push(cell)
    radio.classList.add('gwheel__radio')
    radio.tabIndex = -1
    track.append(radio, cell.el)
  }
  track.append(makePad())

  const lens = document.createElement('div')
  lens.className = 'gwheel__lens'
  lens.setAttribute('aria-hidden', 'true')
  const shade = document.createElement('div')
  shade.className = 'gwheel__shade'
  shade.setAttribute('aria-hidden', 'true')

  container.replaceChildren(track, lens, shade)

  wire(track, cells, readout)
}

function writeReadout(readout: HTMLElement | null, grade: number | null): void {
  if (readout === null) {
    return
  }
  readout.textContent = grade === null ? 'Not set' : String(grade)
  readout.classList.toggle('gwheel-readout--unset', grade === null)
}

function makePad(): HTMLElement {
  const pad = document.createElement('div')
  pad.className = 'gwheel__pad'
  pad.setAttribute('aria-hidden', 'true')
  return pad
}

function buildUnsetCell(): WheelCell {
  const el = document.createElement('span')
  el.className = 'gwheel__cell gwheel__cell--unset'
  el.setAttribute('aria-hidden', 'true')
  el.tabIndex = -1
  el.textContent = '–'
  return { el, grade: null, radio: null }
}

function buildGradeCell(container: HTMLElement, radio: HTMLInputElement): WheelCell {
  const existing = container.querySelector<HTMLLabelElement>(`label[for="${radio.id}"]`)
  const el = existing ?? document.createElement('label')
  el.className = 'gwheel__cell'
  el.textContent = radio.value
  if (existing === null) {
    el.setAttribute('for', radio.id)
  }
  return { el, grade: Number(radio.value), radio }
}

function wire(track: HTMLElement, cells: WheelCell[], readout: HTMLElement | null): void {
  const controller = createWheelController(
    track,
    cells.map((cell) => cell.el),
    (index) => {
      const chosen = cells[index]
      cells.forEach((cell) => {
        if (cell.radio !== null) {
          cell.radio.checked = cell === chosen
        }
      })
      applyTrackAria(track, chosen.grade)
      writeReadout(readout, chosen.grade)
    },
  )

  const initial = cells.findIndex((cell) => cell.radio?.checked === true)
  const startIndex = initial === -1 ? 0 : initial
  applyTrackAria(track, cells[startIndex].grade)
  controller.setIndex(startIndex, false)
}

function main(): void {
  for (const container of document.querySelectorAll<HTMLElement>('.gcse-grade-picker:not(.gwheel)')) {
    enhance(container)
  }
}

main()
