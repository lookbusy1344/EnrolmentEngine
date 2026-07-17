import { expect, test } from '@playwright/test'
import { fillGoldenFacts, GOLDEN_GCSES, skipUnlessProject } from './support.ts'

test.describe('Vue workflow', () => {
  test.beforeEach(({}, testInfo) => {
    test.skip(skipUnlessProject(testInfo, 'desktop-1366'), 'Behaviour does not depend on viewport size.')
  })

  test('choosing and removing a subject updates the basket without a full page reload', async ({ page }) => {
    await page.goto('/app')
    await fillGoldenFacts(page)
    await page.evaluate(() => {
      window.__e2eMarker = true
    })

    await page.locator('article.card').getByRole('button', { name: 'Choose' }).first().click()
    await expect(page.locator('.chosen-summary')).not.toContainText('None chosen yet.')

    const markerSurvived = await page.evaluate(() => window.__e2eMarker === true)
    expect(markerSurvived).toBe(true)

    // Scoped to a subject card, not just role+name: GcseRows/PriorQualificationRows/HobbyRows also
    // have "Remove" buttons for blank-row cleanup, and those come first in the DOM.
    await page.locator('article.card').getByRole('button', { name: 'Remove' }).first().click()
    await expect(page.locator('.chosen-summary')).toContainText('None chosen yet.')
  })

  test('lowering the GCSE grades ejects a chosen subject that is no longer available', async ({ page }) => {
    await page.goto('/app')
    await fillGoldenFacts(page)

    await page.locator('article.card').getByRole('button', { name: 'Choose' }).first().click()
    await expect(page.locator('.chosen-summary')).not.toContainText('None chosen yet.')

    // Collapse every grade to a 1: the student drops below the eligibility gate, so the chosen
    // subject goes red and can no longer be held.
    for (const index of GOLDEN_GCSES.keys()) {
      await page.fill(`#gcse-grade-${index.toString()}`, '1')
    }

    await expect(page.locator('.chosen-summary')).toContainText('None chosen yet.')
    await expect(page.getByRole('status')).toContainText('no longer available with your current grades')
  })

  test('refresh restores the browser-local snapshot and re-evaluates through the API', async ({ page }) => {
    await page.goto('/app')
    await fillGoldenFacts(page)

    await page.reload()

    await expect(page.locator('#date-of-birth')).toHaveValue('2009-09-01')
    await expect(page.locator('#gcse-subject-0')).toHaveValue('maths')
    await page.locator('.card').first().waitFor({ state: 'visible', timeout: 10_000 })
  })

  test('Start over clears the browser-local snapshot and resets the UI', async ({ page }) => {
    await page.goto('/app')
    await fillGoldenFacts(page)

    await page.getByRole('button', { name: 'Start over' }).click()

    await expect(page.locator('#date-of-birth')).toHaveValue(/\d{4}-\d{2}-\d{2}/)

    await page.reload()
    await expect(page.locator('#date-of-birth')).toHaveValue(/\d{4}-\d{2}-\d{2}/)
  })
})
