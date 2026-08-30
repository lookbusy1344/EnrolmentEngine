// Pure geometry for the GCSE grade wheel — no DOM, used by the Vue component. Everything here is
// a pure function of numbers, and is unit-tested in curvature.test.ts.

/** Tunables for the drum's curved edge — pitched so the two cells either side of the lens stay
 readable and tappable, since five grades stand under the drum's unmasked band. */
export const CURVE = {
  /** Degrees a cell yaws away per cell-width of distance from centre. */
  rotateDegPerCell: 22,
  /** Pixels a cell recedes (−z) per cell-width of distance. */
  depthPxPerCell: 8,
  /** Pixels a cell shifts horizontally per cell-width, tightening the spacing toward the rim. */
  drawInPxPerCell: 6,
  /** Scale lost per cell-width of distance, and the floor it never drops below. */
  scalePerCell: 0.12,
  maxScaleDrop: 0.4,
  /** Opacity lost per cell-width of distance, and the floor it never drops below. */
  opacityPerCell: 0.15,
  maxOpacityDrop: 0.55,
} as const

export interface CellStyle {
  rotateY: number
  translateZ: number
  translateX: number
  scale: number
  opacity: number
}

/**
 * The transform for one cell sitting `normalised` cell-widths from the centred position (0 = dead
 * centre, ±1 = one cell away). Symmetric about centre; scale and opacity are floored so distant
 * cells recede without vanishing or flipping negative.
 */
export function curveStyle(normalised: number): CellStyle {
  const spread = Math.abs(normalised)
  return {
    rotateY: normalised * -CURVE.rotateDegPerCell,
    translateZ: -spread * CURVE.depthPxPerCell,
    translateX: normalised * -CURVE.drawInPxPerCell,
    scale: 1 - Math.min(spread * CURVE.scalePerCell, CURVE.maxScaleDrop),
    opacity: 1 - Math.min(spread * CURVE.opacityPerCell, CURVE.maxOpacityDrop),
  }
}

/** The CSS `transform` for a cell `normalised` cell-widths from centre — the drum's curved edge. */
export function cellTransform(normalised: number): string {
  const s = curveStyle(normalised)
  return (
    `translateX(${String(s.translateX)}px) rotateY(${String(s.rotateY)}deg) ` +
    `translateZ(${String(s.translateZ)}px) scale(${String(s.scale)})`
  )
}

/** The cell opacity for a cell `normalised` cell-widths from centre. */
export function cellOpacity(normalised: number): number {
  return curveStyle(normalised).opacity
}

/**
 * Index of the cell whose centre lies closest to `viewportCentre`. Ties resolve to the lower index.
 * `centres` is assumed non-empty (a wheel always has cells).
 */
export function nearestIndexFromCentres(viewportCentre: number, centres: readonly number[]): number {
  let best = 0
  let bestDistance = Infinity
  centres.forEach((centre, index) => {
    const distance = Math.abs(centre - viewportCentre)
    if (distance < bestDistance) {
      bestDistance = distance
      best = index
    }
  })
  return best
}
