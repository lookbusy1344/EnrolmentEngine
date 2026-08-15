import { expect, test } from '@playwright/test'
import { fillGoldenFacts, skipUnlessProject } from './support.ts'

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
