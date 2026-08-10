import { expect, test } from '@playwright/test'
import { fillGoldenFacts } from './support.ts'

/** Bootstrap's `md` breakpoint — below it the facts row stacks and the grade group goes full width. */
const BOOTSTRAP_MD = 768

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

    const overflowingCount = await page.evaluate((width) => {
      const elements = Array.from(document.querySelectorAll('body *'))
      return elements.filter((element) => element.getBoundingClientRect().right > width + 1).length
    }, viewportWidth ?? 0)

    expect(overflowingCount).toBe(0)
  })

  // The 1-9 toggle group is the one control that has to wrap on a phone: nine buttons never fit on
  // a 360px row. Left to Bootstrap's own `flex: 1 1 auto` it wraps ragged (six wide buttons, then
  // three wider ones), so the geometry is asserted rather than the class name. Above md the group
  // is content-width and never wraps, so only the row count is checked there.
  test('every GCSE grade button is the same width and the group wraps to at most two rows', async ({
    page,
  }, testInfo) => {
    await page.goto('/app')

    const viewportWidth = testInfo.project.use.viewport?.width ?? 0
    expect(viewportWidth).toBeTruthy()

    const boxes = await page.locator('label[for^="gcse-grade-0-"]').evaluateAll((labels) =>
      labels.map((label) => {
        const { width, top } = label.getBoundingClientRect()
        return { width: Math.round(width), top: Math.round(top) }
      }),
    )

    expect(boxes).toHaveLength(9)
    expect(new Set(boxes.map((box) => box.top)).size).toBeLessThanOrEqual(2)

    if (viewportWidth < BOOTSTRAP_MD) {
      const widths = boxes.map((box) => box.width)
      expect(Math.max(...widths) - Math.min(...widths)).toBeLessThanOrEqual(1)
    }
  })

  test('the chosen basket and facts heading are both reachable', async ({ page }) => {
    await page.goto('/app')

    await expect(page.locator('#chosen-heading')).toBeVisible()
    await expect(page.locator('#facts-heading')).toBeVisible()
    await page.locator('#facts-heading').scrollIntoViewIfNeeded()
    await expect(page.locator('#results-heading')).toBeVisible()
  })
})
