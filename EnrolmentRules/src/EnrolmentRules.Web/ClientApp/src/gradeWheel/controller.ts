// The GCSE grade wheel's behaviour, shared by both front ends (the Vue component and the /razor
// enhancement script) so they spin, snap and key identically. On the widest screens the same
// markup is laid out flat, as a row of every grade (site.css sets --gwheel-flat there); the drum's
// curve and snapping then sit out, and the keyboard, clicks and selection work exactly as before. It owns scroll, drag, keyboard and
// the per-frame curvature; it does not know what a cell *means*. Cells are laid out [unset, 1..9],
// so a cell index equals its grade (index 0 is "Not set"), which is why the digit keys map straight
// to an index. The host supplies the cell elements and an onSelect(index) callback that records the
// choice (checking a radio, updating a model) and the aria value.

import { cellOpacity, cellTransform, nearestIndexFromCentres } from './curvature'

/** How long after the last scroll event a free scroll is treated as settled, when scrollend is absent. */
const SETTLE_MS = 110
/** Fallback wait for a programmatic smooth scroll to finish, where scrollend is unsupported. */
const SCROLLEND_FALLBACK_MS = 300
/** Pointer travel that still counts as a tap on a cell rather than a drag of the drum. */
const TAP_SLOP_PX = 4
/** The CSS custom property by which a stylesheet flattens the drum into a plain row of grades. */
const FLAT_PROPERTY = '--gwheel-flat'

export interface WheelController {
  /** Centre a cell without firing onSelect — for the initial position and external model changes. */
  setIndex: (index: number, smooth: boolean) => void
  destroy: () => void
}

/** Reflect the chosen grade (null = "Not set") onto a slider element's aria value. */
export function applyTrackAria(track: HTMLElement, grade: number | null): void {
  track.setAttribute('aria-valuetext', grade === null ? 'Not set' : `Grade ${String(grade)}`)
  if (grade === null) {
    track.removeAttribute('aria-valuenow')
  } else {
    track.setAttribute('aria-valuenow', String(grade))
  }
}

export function createWheelController(
  track: HTMLElement,
  cells: readonly HTMLElement[],
  onSelect: (index: number) => void,
): WheelController {
  const flat = (): boolean => getComputedStyle(track).getPropertyValue(FLAT_PROPERTY).trim() === '1'
  const clamp = (index: number): number => Math.min(Math.max(index, 0), cells.length - 1)
  const centres = (): number[] => cells.map((cell) => cell.offsetLeft + cell.offsetWidth / 2)
  const viewportCentre = (): number => track.scrollLeft + track.clientWidth / 2
  const centredIndex = (): number => nearestIndexFromCentres(viewportCentre(), centres())
  // The cell under a screen x. The pointer is captured on the track, so a press on an off-centre
  // cell never reaches that cell's own click handler — this is how a tap picks its grade.
  const indexAtClientX = (clientX: number): number =>
    nearestIndexFromCentres(track.scrollLeft + (clientX - track.getBoundingClientRect().left), centres())

  // The cell the wheel is showing. Held rather than derived from the scroll position, so that a
  // change in the drum's own width (it narrows on a phone) can put it back under the lens.
  let currentIndex = 0
  let programmatic = false
  let rafPending = false
  let settleTimer: ReturnType<typeof setTimeout> | undefined

  function markSelected(index: number): void {
    cells.forEach((cell) => {
      cell.dataset.selected = 'false'
    })
    cells[index].dataset.selected = 'true'
  }

  // Flat rows show every grade at rest, so the curve's inline transform and opacity come off.
  function paintFlat(): void {
    cells.forEach((cell) => {
      cell.style.removeProperty('transform')
      cell.style.removeProperty('opacity')
    })
    markSelected(currentIndex)
  }

  function paint(): void {
    if (flat()) {
      paintFlat()
      return
    }
    const mid = viewportCentre()
    const width = cells[0]?.offsetWidth || 1
    const cs = centres()
    cells.forEach((cell, i) => {
      const normalised = (cs[i] - mid) / width
      cell.style.transform = cellTransform(normalised)
      cell.style.opacity = String(cellOpacity(normalised))
    })
    markSelected(nearestIndexFromCentres(mid, cs))
  }

  function schedulePaint(): void {
    if (!rafPending) {
      rafPending = true
      requestAnimationFrame(() => {
        rafPending = false
        paint()
      })
    }
  }

  // scrollend where the browser has it, else a timer; a safety timeout so `programmatic` never sticks.
  function afterScrollEnd(callback: () => void): void {
    if ('onscrollend' in track) {
      const done = (): void => {
        track.removeEventListener('scrollend', done)
        clearTimeout(safety)
        callback()
      }
      const safety = setTimeout(done, 1000)
      track.addEventListener('scrollend', done)
    } else {
      setTimeout(callback, SCROLLEND_FALLBACK_MS)
    }
  }

  // Centre a cell. Marked programmatic so the resulting scroll is not mistaken for a user gesture
  // and re-snapped mid-flight — the bug that let typed/keyed grades drift off centre.
  function glideTo(index: number, smooth: boolean): void {
    if (index < 0 || index >= cells.length) {
      return
    }
    if (flat()) {
      schedulePaint()
      return
    }
    const cell = cells[index]
    programmatic = true
    track.scrollTo({
      left: cell.offsetLeft + cell.offsetWidth / 2 - track.clientWidth / 2,
      behavior: smooth ? 'smooth' : 'auto',
    })
    schedulePaint()
    afterScrollEnd(() => {
      programmatic = false
      // A smooth glide can be cancelled mid-flight — a re-layout of the rows around it, a competing
      // scroll — leaving the drum wherever it stopped while the model reads the chosen grade. Put
      // it where it belongs, instantly, and the instant move needs no such check itself.
      if (smooth && centredIndex() !== index) {
        glideTo(index, false)
      }
    })
  }

  function choose(index: number): void {
    const target = clamp(index)
    currentIndex = target
    onSelect(target)
    glideTo(target, true)
  }

  function onScroll(): void {
    schedulePaint()
    if (programmatic) {
      return
    }
    clearTimeout(settleTimer)
    settleTimer = setTimeout(() => {
      choose(centredIndex())
    }, SETTLE_MS)
  }

  function onKeydown(event: KeyboardEvent): void {
    const key = event.key
    // Stepping from the selection, not from whatever sits at the track's centre: a flat row has no
    // centred cell to step from, and on the drum the two are the same once a scroll has settled.
    if (key === 'ArrowLeft' || key === 'ArrowDown') {
      choose(currentIndex - 1)
    } else if (key === 'ArrowRight' || key === 'ArrowUp') {
      choose(currentIndex + 1)
    } else if (key === 'Home' || key === 'Backspace' || key === 'Delete') {
      choose(0)
    } else if (key === 'End') {
      choose(cells.length - 1)
    } else if (key >= '1' && key <= '9') {
      choose(Number(key))
    } else {
      return
    }
    event.preventDefault()
  }

  const clickHandlers = cells.map((cell, index) => {
    const handler = (event: Event): void => {
      event.preventDefault()
      choose(index)
    }
    cell.addEventListener('click', handler)
    return handler
  })

  let dragging = false
  let dragged = false
  let dragStartX = 0
  let dragStartScroll = 0

  function onPointerdown(event: PointerEvent): void {
    if (event.pointerType === 'touch') {
      return
    }
    dragging = true
    dragged = false
    programmatic = true
    dragStartX = event.clientX
    dragStartScroll = track.scrollLeft
    track.setPointerCapture(event.pointerId)
  }
  function onPointermove(event: PointerEvent): void {
    if (dragging) {
      const travel = event.clientX - dragStartX
      dragged ||= Math.abs(travel) > TAP_SLOP_PX
      track.scrollLeft = dragStartScroll - travel
      schedulePaint()
    }
  }
  // A drag snaps to whatever ended up under the lens; a tap takes the cell it landed on.
  function onPointerup(event: PointerEvent): void {
    if (dragging) {
      dragging = false
      programmatic = false
      choose(dragged ? centredIndex() : indexAtClientX(event.clientX))
    }
  }

  // A width change re-lays the cells under a scroll position that no longer means the same thing;
  // ride the current one back under the lens. Silent, and any pending snap is dropped — a resize
  // chooses nothing.
  function onResize(): void {
    clearTimeout(settleTimer)
    glideTo(currentIndex, false)
  }
  const resizeObserver = typeof ResizeObserver === 'undefined' ? undefined : new ResizeObserver(onResize)
  resizeObserver?.observe(track)

  track.addEventListener('scroll', onScroll)
  track.addEventListener('keydown', onKeydown)
  track.addEventListener('pointerdown', onPointerdown)
  track.addEventListener('pointermove', onPointermove)
  track.addEventListener('pointerup', onPointerup)

  return {
    setIndex(index, smooth) {
      currentIndex = clamp(index)
      glideTo(currentIndex, smooth)
    },
    destroy() {
      clearTimeout(settleTimer)
      resizeObserver?.disconnect()
      track.removeEventListener('scroll', onScroll)
      track.removeEventListener('keydown', onKeydown)
      track.removeEventListener('pointerdown', onPointerdown)
      track.removeEventListener('pointermove', onPointermove)
      track.removeEventListener('pointerup', onPointerup)
      cells.forEach((cell, i) => {
        cell.removeEventListener('click', clickHandlers[i])
      })
    },
  }
}
