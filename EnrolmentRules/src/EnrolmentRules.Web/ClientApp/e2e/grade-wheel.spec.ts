import { expect, type Locator, test } from '@playwright/test'
import { setGcseGrade, skipUnlessProject } from './support.ts'

/** The dimmest a cell two places from the lens may be painted and still read as a grade. */
const MIN_EDGE_OPACITY = 0.6

/** Bootstrap's lg breakpoint, at and above which the drum lays its grades out as a button row. */
const FLAT_ROW_MIN_WIDTH = 992

/** Bootstrap's md breakpoint: below it the subject select takes a line to itself. */
const SUBJECT_SHARES_LINE_MIN_WIDTH = 768

/** The row's g-2 gutter, which sits between the control and Remove when they share a line. */
const GRID_GUTTER_PX = 8

/** The drum's one width, whatever the viewport: five cells of 55px. */
const DRUM_WIDTH = 275

/** True where this project's viewport is narrow enough to render the spinning drum. */
function isDrumProject(testInfo: { project: { use: { viewport?: { width: number } | null } } }): boolean {
  return (testInfo.project.use.viewport?.width ?? 0) < FLAT_ROW_MIN_WIDTH
}

/**
 * The drum glides to a keyed grade; it has settled once that grade is painted at full opacity. A
 * flat row paints no inline opacity at all, so there is nothing to wait for there.
 */
async function waitForSettled(wheel: Locator, grade: number): Promise<void> {
  const chosen = wheel.locator(`label[for="gcse-grade-0-${grade.toString()}"]`)
  await expect
    .poll(async () => chosen.evaluate((el) => el.style.opacity === '' || Number(el.style.opacity) > 0.99))
    .toBe(true)
}

/** The drum's width and the width of one of its cells, in layout pixels. */
async function drumSize(wheel: Locator): Promise<{ width: number; cellWidth: number }> {
  return wheel.evaluate((el) => ({
    width: el.clientWidth,
    cellWidth: el.querySelector<HTMLElement>('label.gwheel__cell')?.offsetWidth ?? 0,
  }))
}

/** How far the given grade's cell sits from the drum's centre line, in layout pixels. */
async function offCentreBy(wheel: Locator, grade: number): Promise<number> {
  return wheel.evaluate((el, chosen: number) => {
    const track = el.querySelector<HTMLElement>('.gwheel__track')
    const cell = el.querySelector<HTMLElement>(`label[for="gcse-grade-0-${String(chosen)}"]`)
    if (track === null || cell === null) {
      return Number.POSITIVE_INFINITY
    }
    return Math.abs(cell.offsetLeft + cell.offsetWidth / 2 - (track.scrollLeft + track.clientWidth / 2))
  }, grade)
}

test.describe('grade wheel', () => {
  // Five grades stand across the drum at every width: the one under the lens plus two either side.
  // Checked on layout geometry (offsetWidth is transform-independent, unlike the painted rect,
  // which the drum's 3D curve distorts) plus the painted opacity of the outermost pair.
  test('shows five grades across the drum', async ({ page }, testInfo) => {
    test.skip(!isDrumProject(testInfo), 'The widest viewports lay the grades out as a button row.')
    await page.goto('/app')
    await page.selectOption('#gcse-subject-0', 'maths')
    await setGcseGrade(page, 0, 5)

    const wheel = page.locator('.gwheel').first()
    await waitForSettled(wheel, 5)
    const { width, cellWidth } = await drumSize(wheel)

    expect(cellWidth * 5).toBeLessThanOrEqual(width + 1)
    expect(cellWidth * 6).toBeGreaterThan(width + 1)

    // Grades 3 and 7 sit two places either side of the chosen 5.
    for (const grade of [3, 7]) {
      const opacity = await wheel
        .locator(`label[for="gcse-grade-0-${grade.toString()}"]`)
        .evaluate((el) => el.style.opacity)
      expect(Number(opacity)).toBeGreaterThanOrEqual(MIN_EDGE_OPACITY)
    }
  })

  // One width at every viewport that spins the drum — the Remove button has a column of its own,
  // so the drum never gives up cells to make room for it.
  test('keeps one drum width at every viewport', async ({ page }, testInfo) => {
    test.skip(!isDrumProject(testInfo), 'The widest viewports lay the grades out as a button row.')
    await page.goto('/app')
    await page.selectOption('#gcse-subject-0', 'maths')

    const { width, cellWidth } = await drumSize(page.locator('.gwheel').first())

    expect({ width, cellWidth }).toEqual({ width: DRUM_WIDTH, cellWidth: DRUM_WIDTH / 5 })
  })

  // The chosen grade holds its place under the lens however the row around the drum is re-laid.
  // Measured on layout, which the drum's 3D curve leaves alone.
  test('keeps the chosen grade under the lens across a resize', async ({ page }, testInfo) => {
    test.skip(skipUnlessProject(testInfo, 'desktop-1366'), 'The test resizes the viewport itself.')
    await page.goto('/app')
    await page.selectOption('#gcse-subject-0', 'maths')
    await setGcseGrade(page, 0, 5)

    const wheel = page.locator('.gwheel').first()
    const chosen = wheel.locator('label[for="gcse-grade-0-5"]')
    await waitForSettled(wheel, 5)

    // 1920 crosses into the button row, where there is no lens to sit under — the grade stays
    // marked either way, and stays centred wherever the drum still spins.
    for (const width of [360, 768, 1920]) {
      await page.setViewportSize({ width, height: 800 })
      await expect.poll(async () => chosen.getAttribute('data-selected')).toBe('true')
      if (width < FLAT_ROW_MIN_WIDTH) {
        await expect.poll(async () => offCentreBy(wheel, 5)).toBeLessThan(1.5)
      }
    }
  })

  // From Bootstrap's lg breakpoint up the whole 1-9 scale fits, so the drum flattens into a row of
  // buttons: every grade visible, one click away, and the chosen one marked in place of the lens.
  test('lays every grade out as a button row on the widest screens', async ({ page }, testInfo) => {
    test.skip(isDrumProject(testInfo), 'Narrower viewports spin the drum instead.')
    await page.goto('/app')
    await page.selectOption('#gcse-subject-0', 'maths')
    await setGcseGrade(page, 0, 5)

    const wheel = page.locator('.gwheel').first()
    await expect(wheel.locator('label[for="gcse-grade-0-5"]')).toHaveAttribute('data-selected', 'true')
    await expect(wheel.locator('.gwheel__lens')).toBeHidden()

    // Ten cells wide by declaration ("Not set" plus 1-9), not by whatever a browser makes of a
    // scroll container's intrinsic width — engines disagree about that, and a short answer clips
    // grades out of sight.
    const { width, cellWidth } = await drumSize(wheel)
    expect(width).toBe(cellWidth * 10)

    // Nothing is scrolled out of sight, and no cell is yawed or faded by the drum's curve.
    const row = await wheel.evaluate((el) => {
      const track = el.querySelector<HTMLElement>('.gwheel__track')
      const cells = Array.from(el.querySelectorAll<HTMLElement>('.gwheel__cell'))
      return {
        overflow: (track?.scrollWidth ?? 0) - (track?.clientWidth ?? 0),
        curved: cells.filter((cell) => cell.style.transform !== '' || cell.style.opacity !== '').length,
        visible: cells.filter((cell) => cell.offsetWidth > 0).length,
      }
    })

    expect(row).toEqual({ overflow: 0, curved: 0, visible: 10 })
  })

  test('keeps taps low-latency on the wide button row', async ({ page }, testInfo) => {
    test.skip(isDrumProject(testInfo), 'Narrower viewports spin the drum instead.')
    await page.goto('/app')
    await page.selectOption('#gcse-subject-0', 'maths')

    const touchAction = await page
      .locator('.gwheel')
      .first()
      .evaluate((el) => getComputedStyle(el).touchAction)

    expect(touchAction).toBe('manipulation')
  })

  // One tab stop and the same keys at every width — these screens are desktops, so the row has to
  // take the keyboard exactly as the drum does.
  test('takes the same keys on the button row as on the drum', async ({ page }, testInfo) => {
    test.skip(isDrumProject(testInfo), 'The drum keyboard is covered by the workflow specs.')
    await page.goto('/app')
    await page.selectOption('#gcse-subject-0', 'maths')

    const track = page.locator('[role="slider"][aria-labelledby="gcse-grade-label-0"]')
    await track.focus()

    await track.press('7')
    await expect(track).toHaveAttribute('aria-valuenow', '7')
    await expect(page.locator('label[for="gcse-grade-0-7"]')).toHaveAttribute('data-selected', 'true')

    await track.press('ArrowLeft')
    await expect(track).toHaveAttribute('aria-valuenow', '6')
    await expect(page.locator('label[for="gcse-grade-0-6"]')).toHaveAttribute('data-selected', 'true')

    await track.press('Backspace')
    await expect(track).not.toHaveAttribute('aria-valuenow', /\d/)
    await expect(page.locator('.gwheel').first().locator('.gwheel__cell--unset')).toHaveAttribute(
      'data-selected',
      'true',
    )
  })

  // Adding a row must not disturb the drums above it: the chosen grade stays under the lens, not
  // just in the model.
  test('holds each drum on its grade when another row is added', async ({ page }, testInfo) => {
    test.skip(!isDrumProject(testInfo), 'The widest viewports lay the grades out as a button row.')
    await page.goto('/app')
    await page.selectOption('#gcse-subject-0', 'maths')
    await setGcseGrade(page, 0, 5)

    await page.selectOption('#gcse-subject-1', 'physics')

    const wheel = page.locator('.gwheel').first()
    await expect(wheel.locator('label[for="gcse-grade-0-5"]')).toHaveAttribute('data-selected', 'true')
    await expect.poll(async () => offCentreBy(wheel, 5)).toBeLessThan(1.5)
  })

  // The trailing blank row has no Remove button, but it still holds the column open — otherwise the
  // subject select grows into the gap and that row's grade control sits out of line with the rest.
  test('holds the Remove column open on the blank trailing row', async ({ page }, testInfo) => {
    test.skip(
      (testInfo.project.use.viewport?.width ?? 0) < SUBJECT_SHARES_LINE_MIN_WIDTH,
      'Below md the subject select has its line to itself, so there is nothing to hold open.',
    )
    await page.goto('/app')
    await page.selectOption('#gcse-subject-0', 'maths')
    await page.selectOption('#gcse-subject-1', 'physics')

    const lefts = await page
      .locator('.gwheel')
      .evaluateAll((wheels) => wheels.map((wheel) => Math.round(wheel.getBoundingClientRect().left)))

    expect(lefts.length).toBe(3)
    expect(new Set(lefts).size).toBe(1)
  })

  // Nothing to grade until a subject is picked, so the blank row's control is hidden — in place, so
  // the row keeps its shape and the rows above stay in line.
  test('hides the grade control until the row has a subject', async ({ page }) => {
    await page.goto('/app')
    await page.selectOption('#gcse-subject-0', 'maths')

    const wheels = page.locator('.gwheel')

    await expect(wheels).toHaveCount(2)
    await expect(wheels.first()).toBeVisible()
    await expect(wheels.last()).toBeHidden()
  })

  // Remove sits in a grid column of its own, left-aligned: beside the control where the row is wide
  // enough, on its own line below it on a phone. Either way it starts at its column's left edge and
  // stays inside the row.
  test('gives the Remove button its own column, left-aligned', async ({ page }) => {
    await page.goto('/app')
    await page.selectOption('#gcse-subject-0', 'maths')

    const placement = await page
      .locator('.gwheel')
      .first()
      .evaluate((el, gutter: number) => {
        const gradeColumn = el.parentElement as HTMLElement
        const gridRow = gradeColumn.parentElement as HTMLElement
        const button = gridRow.querySelector('button') as HTMLElement
        const buttonColumn = button.parentElement as HTMLElement
        const bounds = button.getBoundingClientRect()
        return {
          ownColumn: buttonColumn !== gradeColumn && gridRow.contains(buttonColumn),
          // against the column's content box: a grid column carries half the row's gutter as padding
          fromColumnLeft: Math.round(
            bounds.left -
              buttonColumn.getBoundingClientRect().left -
              parseFloat(getComputedStyle(buttonColumn).paddingLeft),
          ),
          overhang: Math.round(bounds.right - gridRow.getBoundingClientRect().right),
          belowTheControl: bounds.top >= el.getBoundingClientRect().bottom,
          roomForBoth:
            gridRow.getBoundingClientRect().width >= el.getBoundingClientRect().width + bounds.width + gutter,
        }
      }, GRID_GUTTER_PX)

    expect(placement.ownColumn).toBe(true)
    expect(placement.fromColumnLeft).toBe(0)
    expect(placement.overhang).toBeLessThanOrEqual(0)
    // Remove drops under the control only where the row cannot hold the pair — no breakpoint decides
    // that, so the rule is the measurement itself.
    expect(placement.belowTheControl).toBe(!placement.roomForBoth)
  })

  test('clicking an off-centre grade selects it', async ({ page }, testInfo) => {
    test.skip(skipUnlessProject(testInfo, 'desktop-1366'), 'Behaviour does not depend on viewport size.')
    await page.goto('/app')
    await page.selectOption('#gcse-subject-0', 'maths')
    await setGcseGrade(page, 0, 5)

    const track = page.locator('[role="slider"][aria-labelledby="gcse-grade-label-0"]')
    await page.locator('label[for="gcse-grade-0-7"]').click()

    await expect(track).toHaveAttribute('aria-valuenow', '7')
  })
})
