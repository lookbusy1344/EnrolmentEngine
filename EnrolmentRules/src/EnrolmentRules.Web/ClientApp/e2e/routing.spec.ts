import { expect, test } from '@playwright/test'
import { skipUnlessProject } from './support.ts'

test.describe('routing', () => {
  test.beforeEach(({}, testInfo) => {
    test.skip(skipUnlessProject(testInfo, 'desktop-1366'), 'Routing does not depend on viewport size.')
  })

  test('/ redirects to the configured default experience without rendering a third workflow UI', async ({ page }) => {
    const response = await page.goto('/')

    expect(response?.request().redirectedFrom()).toBeTruthy()
    expect(page.url()).toContain('/app')
    await expect(page.locator('#enrolment-vue-app')).toBeVisible()
  })

  test('/razor and /app share the same header from the shared layout', async ({ page }) => {
    await page.goto('/razor')
    const razorBrand = await page.locator('.brand-name').textContent()
    const razorTag = await page.locator('.brand-tag').textContent()

    await page.goto('/app')
    const appBrand = await page.locator('.brand-name').textContent()
    const appTag = await page.locator('.brand-tag').textContent()

    expect(appBrand).toBe(razorBrand)
    expect(appTag).toBe(razorTag)
  })

  test('/razor names itself server rendered and links across to the dynamic version', async ({ page }) => {
    await page.goto('/razor')

    await expect(page).toHaveTitle(/server rendered/i)
    await expect(page.locator('.mode-tag')).toHaveText(/server rendered/i)

    await page.locator('.mode-switch').click()
    expect(page.url()).toContain('/app')
  })

  test('/app names itself dynamic and links across to the server-rendered version', async ({ page }) => {
    await page.goto('/app')

    await expect(page).toHaveTitle(/dynamic/i)
    await expect(page.locator('.mode-tag')).toHaveText(/dynamic/i)

    await page.locator('.mode-switch').click()
    expect(page.url()).toContain('/razor')
  })

  test('/app renders the Vue mount point and the built script tag', async ({ page }) => {
    await page.goto('/app')

    await expect(page.locator('#enrolment-vue-app')).toBeVisible()
    await expect(page.locator('#facts-heading')).toBeVisible()
  })
})
