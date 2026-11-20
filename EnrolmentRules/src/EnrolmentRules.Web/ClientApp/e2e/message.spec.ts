import { expect, test } from '@playwright/test'
import { fillGoldenFacts, skipUnlessProject } from './support.ts'

test.describe('subject card message', () => {
  test.beforeEach(({}, testInfo) => {
    test.skip(skipUnlessProject(testInfo, 'desktop-1366'), 'Behaviour does not depend on viewport size.')
  })

  test('shows what the next rating up needs, and nothing more', async ({ page }) => {
    await page.goto('/app')
    await fillGoldenFacts(page)

    const card = page.locator('.card', { hasText: 'Computer Studies' })
    await expect(card.locator('.card-text')).toHaveText(
      'Amber needs GCSE Computer Studies at grade 5 or above (not taken).',
    )
    await expect(page.locator('.card details')).toHaveCount(0)
  })
})
