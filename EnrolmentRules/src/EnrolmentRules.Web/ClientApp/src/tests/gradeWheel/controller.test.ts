import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createWheelController } from '../../gradeWheel/controller'

const CELL_WIDTH = 40
const CELL_COUNT = 10
const TRACK_WIDTH = 200
/** Longer than either wait the controller uses to decide a scroll has ended. */
const SCROLL_SETTLE_MS = 1500

/** The ResizeObserver callbacks registered against a track, so a test can fire a resize. */
const resizeCallbacks: (() => void)[] = []

class StubResizeObserver {
  constructor(callback: () => void) {
    resizeCallbacks.push(callback)
  }

  observe(): void {}

  disconnect(): void {}
}

interface Rig {
  track: HTMLElement
  cells: HTMLElement[]
  selected: number[]
}

/** jsdom has no layout, so the wheel's geometry is stubbed: fixed-width cells laid end to end. */
function buildRig(scrollLeft: number): Rig {
  const track = document.createElement('div')
  Object.defineProperty(track, 'clientWidth', { value: TRACK_WIDTH })
  Object.defineProperty(track, 'scrollLeft', { value: scrollLeft, writable: true })
  track.getBoundingClientRect = () => new DOMRect(0, 0, TRACK_WIDTH, 45)
  track.setPointerCapture = () => undefined
  track.scrollTo = () => undefined

  const cells = Array.from({ length: CELL_COUNT }, (_, index) => {
    const cell = document.createElement('span')
    Object.defineProperty(cell, 'offsetLeft', { value: index * CELL_WIDTH, configurable: true })
    Object.defineProperty(cell, 'offsetWidth', { value: CELL_WIDTH, configurable: true })
    track.append(cell)
    return cell
  })

  document.body.append(track)
  return { track, cells, selected: [] }
}

/** Re-lay the rig's cells at a new cell width, as crossing the wheel's breakpoint does. */
function relayout(rig: Rig, cellWidth: number): void {
  rig.cells.forEach((cell, index) => {
    Object.defineProperty(cell, 'offsetLeft', { value: index * cellWidth, configurable: true })
    Object.defineProperty(cell, 'offsetWidth', { value: cellWidth, configurable: true })
  })
  resizeCallbacks.forEach((callback) => {
    callback()
  })
}

function press(track: HTMLElement, type: string, clientX: number, pointerType = 'mouse'): void {
  const event = new MouseEvent(type, { clientX, bubbles: true })
  Object.defineProperty(event, 'pointerType', { value: pointerType })
  track.dispatchEvent(event)
}

describe('wheel controller pointer handling', () => {
  beforeEach(() => {
    document.body.replaceChildren()
    resizeCallbacks.length = 0
    vi.stubGlobal('ResizeObserver', StubResizeObserver)
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      callback(0)
      return 0
    })
  })

  // Index 3 is centred (its centre, 140, sits at scrollLeft 40 + half of a 200px track).
  it('a tap without drag selects the cell under the pointer, not the centred one', () => {
    const rig = buildRig(40)
    createWheelController(rig.track, rig.cells, (index) => rig.selected.push(index))

    // Content offset 40 + 180 = 220 — the centre of cell 5.
    press(rig.track, 'pointerdown', 180)
    press(rig.track, 'pointerup', 180)

    expect(rig.selected).toEqual([5])
  })

  it('ignores sub-slop pointer jitter, still treating the press as a tap', () => {
    const rig = buildRig(40)
    createWheelController(rig.track, rig.cells, (index) => rig.selected.push(index))

    press(rig.track, 'pointerdown', 180)
    press(rig.track, 'pointermove', 182)
    press(rig.track, 'pointerup', 182)

    expect(rig.selected).toEqual([5])
  })

  // Dragging 70px leaves the viewport centre at 210, midway between cells 5 and 6; a drag snaps to
  // the centred cell (the tie resolves low, to 5) while the pointer sits over cell 6.
  it('a drag snaps to the centred cell rather than the cell under the pointer', () => {
    const rig = buildRig(40)
    createWheelController(rig.track, rig.cells, (index) => rig.selected.push(index))

    press(rig.track, 'pointerdown', 180)
    press(rig.track, 'pointermove', 110)
    press(rig.track, 'pointerup', 110)

    expect(rig.track.scrollLeft).toBe(110)
    expect(rig.selected).toEqual([5])
  })

  it('tracks a touch drag directly and snaps as soon as the finger lifts', () => {
    const rig = buildRig(40)
    createWheelController(rig.track, rig.cells, (index) => rig.selected.push(index))

    press(rig.track, 'pointerdown', 180, 'touch')
    press(rig.track, 'pointermove', 110, 'touch')
    press(rig.track, 'pointerup', 110, 'touch')

    expect(rig.track.scrollLeft).toBe(110)
    expect(rig.selected).toEqual([5])
  })

  it('clamps a tap past the last cell to the last index', () => {
    const rig = buildRig(200)
    createWheelController(rig.track, rig.cells, (index) => rig.selected.push(index))

    press(rig.track, 'pointerdown', 199)
    press(rig.track, 'pointerup', 199)

    expect(rig.selected).toEqual([CELL_COUNT - 1])
  })
})

describe('wheel controller resizing', () => {
  beforeEach(() => {
    document.body.replaceChildren()
    resizeCallbacks.length = 0
    vi.stubGlobal('ResizeObserver', StubResizeObserver)
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      callback(0)
      return 0
    })
  })

  // The drum narrows on a phone, which moves every cell under a scroll position that no longer
  // means the same thing; the chosen grade has to ride back under the lens, and stay chosen.
  it('re-centres the chosen cell when the drum is resized, without reselecting', () => {
    const rig = buildRig(0)
    // jsdom stubs scrollTo as a no-op, so mirror the scroll a browser would perform.
    rig.track.scrollTo = (options?: ScrollToOptions | number) => {
      if (typeof options === 'object' && typeof options.left === 'number') {
        rig.track.scrollLeft = options.left
      }
    }
    const controller = createWheelController(rig.track, rig.cells, (index) => rig.selected.push(index))
    controller.setIndex(5, false)
    rig.selected.length = 0

    relayout(rig, CELL_WIDTH * 2)

    // Cell 5 now spans 400..480, so its centre sits under the track centre at scrollLeft 340.
    expect(rig.track.scrollLeft).toBe(340)
    expect(rig.selected).toEqual([])
  })
})

describe('wheel controller on a flat row', () => {
  beforeEach(() => {
    document.body.replaceChildren()
    resizeCallbacks.length = 0
    vi.stubGlobal('ResizeObserver', StubResizeObserver)
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      callback(0)
      return 0
    })
  })

  /** A rig laid out flat, as site.css lays the control out from Bootstrap's xl breakpoint up. */
  function buildFlatRig(): Rig {
    const rig = buildRig(0)
    rig.track.style.setProperty('--gwheel-flat', '1')
    return rig
  }

  it('marks the chosen cell without scrolling or curving the row', () => {
    const rig = buildFlatRig()
    const controller = createWheelController(rig.track, rig.cells, (index) => rig.selected.push(index))

    controller.setIndex(7, false)

    expect(rig.track.scrollLeft).toBe(0)
    expect(rig.cells[7].dataset.selected).toBe('true')
    expect(rig.cells.filter((cell) => cell.style.transform !== '' || cell.style.opacity !== '')).toEqual([])
  })

  it('steps the arrow keys from the chosen grade, there being no centred cell to step from', () => {
    const rig = buildFlatRig()
    const controller = createWheelController(rig.track, rig.cells, (index) => rig.selected.push(index))
    controller.setIndex(7, false)
    rig.selected.length = 0

    rig.track.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }))

    expect(rig.selected).toEqual([6])
    expect(rig.cells[6].dataset.selected).toBe('true')
  })
})

describe('wheel controller glide interruption', () => {
  beforeEach(() => {
    document.body.replaceChildren()
    resizeCallbacks.length = 0
    vi.stubGlobal('ResizeObserver', StubResizeObserver)
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      callback(0)
      return 0
    })
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  // A smooth glide can be cancelled by a re-layout of the rows around the drum. The drum then sits
  // on the wrong grade while the model holds the right one — so it re-centres once the glide ends.
  it('re-centres the chosen cell when a smooth glide is cancelled', () => {
    const rig = buildRig(0)
    // A browser that drops the smooth scroll: only the instant one moves the drum.
    rig.track.scrollTo = (options?: ScrollToOptions | number) => {
      if (typeof options === 'object' && options.behavior !== 'smooth' && typeof options.left === 'number') {
        rig.track.scrollLeft = options.left
      }
    }
    const controller = createWheelController(rig.track, rig.cells, (index) => rig.selected.push(index))

    controller.setIndex(5, true)
    expect(rig.track.scrollLeft).toBe(0)

    vi.advanceTimersByTime(SCROLL_SETTLE_MS)

    // Cell 5 spans 200..240, so its centre sits under the track centre at scrollLeft 120.
    expect(rig.track.scrollLeft).toBe(120)
    expect(rig.cells[5].dataset.selected).toBe('true')
    expect(rig.selected).toEqual([])
  })
})
