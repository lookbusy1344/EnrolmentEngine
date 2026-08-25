import { expect, test } from '@playwright/test'
import { skipUnlessProject } from './support.ts'

test.describe('routing', () => {
  test.beforeEach(({}, testInfo) => {
    test.skip(skipUnlessProject(testInfo, 'desktop-1366'), 'Routing does not depend on viewport size.')
  })

  test('/ serves the Vue app directly, with no redirect', async ({ page }) => {
    const response = await page.goto('/')

    expect(response?.request().redirectedFrom()).toBeFalsy()
    await expect(page.locator('#enrolment-vue-app')).toBeVisible()
  })

  test('/app renders the Vue mount point and the built script tag', async ({ page }) => {
    await page.goto('/app')

    await expect(page.locator('#enrolment-vue-app')).toBeVisible()
    await expect(page.locator('#facts-heading')).toBeVisible()
  })
})
