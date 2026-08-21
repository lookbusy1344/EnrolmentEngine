import { expect, test } from '@playwright/test'
import { fillGoldenFacts } from './support.ts'

test.describe('responsive /app', () => {
  test('has no horizontal page scroll', async ({ page }) => {
    await page.goto('/app')
    await fillGoldenFacts(page)

    const { scrollWidth, clientWidth } = await page.evaluate(() => ({
      scrollWidth: document.documentElement.scrollWidth,
      clientWidth: document.documentElement.clientWidth,
    }))

    expect(scrollWidth).toBeLessThanOrEqual(clientWidth + 1)
  })

  test('no element overflows the viewport width', async ({ page }, testInfo) => {
    await page.goto('/app')
    await fillGoldenFacts(page)

    const viewportWidth = testInfo.project.use.viewport?.width
    expect(viewportWidth).toBeTruthy()

    // The grade wheel is a horizontal scroller: its off-screen cells sit past the viewport by
    // design, clipped by the drum's overflow:hidden (so they never widen the page — the scroll test
    // above still holds). Exclude the track's own contents; every laid-out element is checked.
    const overflowingCount = await page.evaluate((width) => {
      const elements = Array.from(document.querySelectorAll('body *')).filter(
        (element) => element.closest('.gwheel__track') === null,
      )
      return elements.filter((element) => element.getBoundingClientRect().right > width + 1).length
    }, viewportWidth ?? 0)

    expect(overflowingCount).toBe(0)
  })

  // The grade wheel lays all nine grades on one fixed-cell drum that fits the viewport at every
  // width. Cell bounding widths vary with the curvature transform, so the fixed cell size is checked
  // via offsetWidth (layout, transform-independent), not the rendered rect.
  test('the grade wheel keeps nine equal-width cells within the viewport', async ({ page }, testInfo) => {
    await page.goto('/app')

    const viewportWidth = testInfo.project.use.viewport?.width ?? 0
    expect(viewportWidth).toBeTruthy()

    const wheel = page.locator('.gwheel').first()
    const cells = wheel.locator('label.gwheel__cell')
    await expect(cells).toHaveCount(9)

    const wheelRight = await wheel.evaluate((el) => el.getBoundingClientRect().right)
    expect(wheelRight).toBeLessThanOrEqual(viewportWidth + 1)

    const widths = await cells.evaluateAll((labels) => labels.map((label) => (label as HTMLElement).offsetWidth))
    expect(new Set(widths).size).toBe(1)
  })

  test('the chosen basket and facts heading are both reachable', async ({ page }) => {
    await page.goto('/app')

    await expect(page.locator('#chosen-heading')).toBeVisible()
    await expect(page.locator('#facts-heading')).toBeVisible()
    await page.locator('#facts-heading').scrollIntoViewIfNeeded()
    await expect(page.locator('#results-heading')).toBeVisible()
  })
})
