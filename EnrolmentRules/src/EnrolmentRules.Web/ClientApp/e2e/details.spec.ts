import { expect, test } from '@playwright/test'
import { fillGoldenFacts, skipUnlessProject } from './support.ts'

test.describe('subject card details', () => {
  test.beforeEach(({}, testInfo) => {
    test.skip(skipUnlessProject(testInfo, 'desktop-1366'), 'Behaviour does not depend on viewport size.')
  })

  test('expand and remain readable', async ({ page }) => {
    await page.goto('/app')
    await fillGoldenFacts(page)

    const details = page.locator('details').first()
    await expect(details.locator('dl')).toBeHidden()

    await details.locator('summary').click()

    await expect(details.locator('dl')).toBeVisible()
    await expect(details.getByText('Base rating')).toBeVisible()
    await expect(details.getByText('Rule')).toBeVisible()
    await expect(details.getByText('Predicted points')).toBeVisible()
  })
})
