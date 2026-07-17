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

    const overflowingCount = await page.evaluate((width) => {
      const elements = Array.from(document.querySelectorAll('body *'))
      return elements.filter((element) => element.getBoundingClientRect().right > width + 1).length
    }, viewportWidth ?? 0)

    expect(overflowingCount).toBe(0)
  })

  test('the chosen basket and facts heading are both reachable', async ({ page }) => {
    await page.goto('/app')

    await expect(page.locator('#chosen-heading')).toBeVisible()
    await expect(page.locator('#facts-heading')).toBeVisible()
    await page.locator('#facts-heading').scrollIntoViewIfNeeded()
    await expect(page.locator('#results-heading')).toBeVisible()
  })
})
