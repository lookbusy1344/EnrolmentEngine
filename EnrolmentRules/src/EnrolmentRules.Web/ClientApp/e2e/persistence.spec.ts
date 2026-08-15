import { expect, test } from '@playwright/test'
import { fillGoldenFacts, setGcseGrade, skipUnlessProject, waitForStoredFacts } from './support.ts'

/**
 * /razor and /app persist facts in the same browser localStorage key/shape (see CLAUDE.md's
 * "Client-side persistence" section) — this is the one place that can actually verify it, since
 * the C# integration tests have no browser/localStorage.
 */
test.describe('cross-page persistence', () => {
  test.beforeEach(({}, testInfo) => {
    test.skip(skipUnlessProject(testInfo, 'desktop-1366'), 'Persistence does not depend on viewport size.')
  })

  test('facts and a chosen subject entered on /app appear on /razor in the same browser', async ({ page }) => {
    await page.goto('/app')
    await fillGoldenFacts(page)

    const chosenCard = page
      .locator('article.card')
      .filter({ has: page.getByRole('button', { name: 'Choose' }) })
      .first()
    const subjectName = await chosenCard
      .locator('h3.card-title')
      .evaluate((el) => el.childNodes[0].textContent?.trim() ?? '')
    await chosenCard.getByRole('button', { name: 'Choose' }).click()
    await expect(page.locator('.list-inline-item.badge')).toContainText(subjectName)

    await page.goto('/razor')

    await expect(page.locator('#DateOfBirth')).toHaveValue('2009-09-01')
    await expect(page.locator('#Gcses_0__Subject')).toHaveValue('maths')
    await expect(page.locator('#Gcses_0__Grade_8')).toBeChecked()
    await expect(page.locator('.list-inline-item.badge')).toContainText(subjectName)
  })

  test('facts saved on /razor appear on /app in the same browser', async ({ page }) => {
    await page.goto('/razor')
    await page.fill('#DateOfBirth', '2009-09-01')
    await page.selectOption('#Gcses_0__Subject', 'maths')
    await page.locator('label[for="Gcses_0__Grade_8"]').click()
    await page.getByRole('button', { name: 'Save & see options' }).click()
    await expect(page.locator('#DateOfBirth')).toHaveValue('2009-09-01')

    await page.goto('/app')

    await expect(page.locator('#date-of-birth')).toHaveValue('2009-09-01')
    await expect(page.locator('#gcse-subject-0')).toHaveValue('maths')
  })

  /**
   * The regression the tests above could not see: each of them reaches /razor for the first time, so
   * there is no `enrolment.state` cookie and /razor rehydrates from localStorage as a cold visit.
   * Visiting /razor *first* leaves that cookie behind, and it outlives the trip to /app — so the
   * return leg renders a non-empty but stale snapshot, which must not win over the newer /app edits.
   */
  test('GCSEs added on /app survive the trip back to a /razor that already holds a state cookie', async ({ page }) => {
    await page.goto('/razor')
    await page.fill('#DateOfBirth', '2009-09-01')
    await page.selectOption('#Gcses_0__Subject', 'physics')
    await page.locator('label[for="Gcses_0__Grade_8"]').click()
    await page.getByRole('button', { name: 'Save & see options' }).click()
    await expect(page.locator('#Gcses_0__Subject')).toHaveValue('physics')

    await page.goto('/app')
    await expect(page.locator('#gcse-subject-0')).toHaveValue('physics')
    await page.selectOption('#gcse-subject-1', 'maths')
    await setGcseGrade(page, 1, 8)
    await page.selectOption('#gcse-subject-2', 'english_language')
    await setGcseGrade(page, 2, 8)
    await expect(page.locator('#gcse-subject-2')).toHaveValue('english_language')
    // The write itself is synchronous, but Vue's watcher runs on the next tick — wait for it rather
    // than racing it, since this test is about what /razor does with a stored edit, not timing.
    await waitForStoredFacts(page, 'english_language')

    await page.goto('/razor')

    await expect(page.locator('#Gcses_1__Subject')).toHaveValue('maths')
    await expect(page.locator('#Gcses_2__Subject')).toHaveValue('english_language')
    await expect(page.locator('#Gcses_0__Subject')).toHaveValue('physics')
  })

  test('starting over on /razor clears the facts /app subsequently sees', async ({ page }) => {
    await page.goto('/app')
    await fillGoldenFacts(page)

    await page.goto('/razor')
    await expect(page.locator('#DateOfBirth')).toHaveValue('2009-09-01')
    await page.getByRole('button', { name: 'Start over' }).click()
    await expect(page.locator('#DateOfBirth')).not.toHaveValue('2009-09-01')

    await page.goto('/app')

    await expect(page.locator('#date-of-birth')).not.toHaveValue('2009-09-01')
  })
})
