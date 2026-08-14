import { expect, test } from '@playwright/test'
import { fillBorderlineFacts, fillGoldenFacts, setGcseGrade, skipUnlessProject } from './support.ts'

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

  test('an amber choice is flagged borderline in the basket, a green one is not', async ({ page }) => {
    await page.goto('/app')
    await fillBorderlineFacts(page)

    const musicCard = page.locator('article.card').filter({ hasText: 'Music' }).first()
    await expect(musicCard.locator('.badge')).toContainText('Amber')
    await musicCard.getByRole('button', { name: 'Choose' }).click()

    const basket = page.locator('.chosen-summary')
    await expect(basket.locator('li.text-bg-warning')).toContainText('Music - Borderline')
    await expect(basket.locator('#borderline-notice')).toContainText('additional authorisation')

    // A green choice sits on the plain pill and does not extend the notice to itself.
    await page
      .locator('article.card')
      .filter({ hasText: 'Green' })
      .first()
      .getByRole('button', { name: 'Choose' })
      .click()
    await expect(basket.locator('li.text-bg-primary')).toHaveCount(1)
    await expect(basket.locator('li.text-bg-primary')).not.toContainText('Borderline')
  })

  test('lowering the GCSE grades keeps a chosen subject in the basket, flagged unavailable', async ({ page }) => {
    await page.goto('/app')
    await fillGoldenFacts(page)

    await page.locator('article.card').getByRole('button', { name: 'Choose' }).first().click()
    const basket = page.locator('.chosen-summary')
    await expect(basket).not.toContainText('None chosen yet.')
    await expect(basket.locator('li')).toHaveCount(1)

    // The golden set is exactly `min_passes` passes, so lowering one grade drops the student below the
    // eligibility gate and the chosen subject goes red — it stays in the basket, marked unavailable,
    // rather than being ejected.
    await setGcseGrade(page, 0, 1)

    await expect(basket.locator('li')).toHaveCount(1)
    await expect(basket.locator('li.text-bg-danger')).toContainText('Unavailable')
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
