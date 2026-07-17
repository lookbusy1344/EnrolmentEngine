import { expect, test } from '@playwright/test'
import { skipUnlessProject } from './support.ts'

// These live at the e2e tier rather than in the unit suites because what they guard is browser
// behaviour: an out-of-range number input fails constraint validation, and a form without
// `novalidate` silently refuses to submit — so the server-side clamp never runs at all. The
// WebApplicationFactory tests post over HTTP and cannot see that.
test.describe('GCSE grade normalisation', () => {
  test.beforeEach(({}, testInfo) => {
    test.skip(skipUnlessProject(testInfo, 'desktop-1366'), 'Grade handling does not depend on viewport size.')
  })

  test('the Razor form submits an out-of-range grade and renders it back clamped', async ({ page }) => {
    await page.goto('/razor')
    await page.selectOption('#Gcses_0__Subject', 'maths')
    await page.fill('#Gcses_0__Grade', '47')

    await page.getByRole('button', { name: 'Save & see options' }).click()
    await page.waitForLoadState('networkidle')

    await expect(page.locator('#Gcses_0__Grade')).toHaveValue('9')
  })

  test('the Razor form rounds a decimal grade on save', async ({ page }) => {
    await page.goto('/razor')
    await page.selectOption('#Gcses_0__Subject', 'maths')
    await page.fill('#Gcses_0__Grade', '7.6')

    await page.getByRole('button', { name: 'Save & see options' }).click()
    await page.waitForLoadState('networkidle')

    await expect(page.locator('#Gcses_0__Grade')).toHaveValue('8')
  })

  test('the Vue form normalises a grade once the field is committed', async ({ page }) => {
    await page.goto('/app')
    await page.selectOption('#gcse-subject-0', 'maths')

    const grade = page.locator('#gcse-grade-0')
    await grade.fill('47')
    await expect(grade).toHaveValue('47') // still focused: not rewritten mid-edit
    await grade.blur()

    await expect(grade).toHaveValue('9')
  })

  test('both grade fields show the 1-9 scale as placeholder text', async ({ page }) => {
    await page.goto('/razor')
    await expect(page.locator('#Gcses_0__Grade')).toHaveAttribute('placeholder', '1-9')

    await page.goto('/app')
    await expect(page.locator('#gcse-grade-0')).toHaveAttribute('placeholder', '1-9')
  })
})
