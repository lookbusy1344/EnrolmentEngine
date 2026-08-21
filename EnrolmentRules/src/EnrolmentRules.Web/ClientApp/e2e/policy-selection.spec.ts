import { expect, test } from '@playwright/test'
import { fillGoldenFacts, setRazorGcseGrade, skipUnlessProject } from './support.ts'

test.describe('Policy selection across responsive layouts', () => {
  test.beforeEach(({}, testInfo) => {
    test.skip(
      !['phone-360', 'desktop-1366'].includes(testInfo.project.name),
      'The top policy switch is covered once at phone and desktop widths.',
    )
  })

  test('Vue: defaults to Standard and switching to Elite keeps facts and the chosen basket', async ({ page }) => {
    await page.goto('/app')
    await fillGoldenFacts(page)

    await expect(page.locator('.policy-switch')).toContainText('Standard')
    await page.locator('article.card').getByRole('button', { name: 'Choose' }).first().click()

    const basket = page.locator('.chosen-summary')
    await expect(basket).not.toContainText('None chosen yet.')
    await expect(basket.locator('li')).toHaveCount(1)

    await page.locator('.policy-switch a', { hasText: 'Switch to Elite' }).click()

    await expect(page.locator('.policy-switch')).toContainText('Elite')
    // Elite's eligibility gate needs at least eight GCSEs; the golden set only submits five, so it is
    // not eligible under Elite — but the facts and the chosen subject stay exactly as they were.
    await expect(page.locator('#date-of-birth')).toHaveValue('2009-09-01')
    await expect(page.locator('#gcse-subject-0')).toHaveValue('maths')
    await expect(basket.locator('li')).toHaveCount(1)
  })
})

test.describe('Policy selection behaviour', () => {
  test.beforeEach(({}, testInfo) => {
    test.skip(skipUnlessProject(testInfo, 'desktop-1366'), 'Behaviour does not depend on viewport size.')
  })

  test('Vue: a ?policy=elite URL loads Elite directly', async ({ page }) => {
    await page.goto('/app?policy=elite')

    await expect(page.locator('.policy-switch')).toContainText('Elite')
    await expect(page.locator('.policy-switch a')).toContainText('Switch to Standard')
  })

  test('Vue: an unknown ?policy= value falls back without crashing the page', async ({ page }) => {
    await page.goto('/app?policy=nonexistent')

    await expect(page.locator('.policy-switch')).toContainText('Standard')
  })

  test('Vue: switching policy flags a basket when too many choices become available', async ({ page }) => {
    await page.addInitScript(() => {
      localStorage.setItem(
        'enrolmentRules.vue.snapshot.v1',
        JSON.stringify({
          schemaVersion: 2,
          savedAt: new Date().toISOString(),
          selectedPolicyId: 'elite',
          snapshot: {
            dateOfBirth: '2009-09-01',
            gcses: [
              { subject: 'maths', grade: 7 },
              { subject: 'english_language', grade: 7 },
              { subject: 'english_literature', grade: 7 },
              { subject: 'biology', grade: 7 },
              { subject: 'chemistry', grade: 7 },
              { subject: 'history', grade: 7 },
            ],
            priorQualifications: [],
            hobbies: [],
            chosenALevels: ['english_literature', 'biology', 'chemistry', 'history'],
          },
        }),
      )
    })
    await page.goto('/app?policy=elite')

    const basket = page.locator('.chosen-summary')
    await expect(basket.locator('li.text-bg-danger')).toHaveCount(4)
    await expect(basket.locator('#basket-choice-limit-error')).toHaveCount(0)

    await page.locator('.policy-switch a', { hasText: 'Switch to Standard' }).click()

    await expect(basket).toHaveClass(/bg-danger-subtle/)
    await expect(basket.locator('#basket-choice-limit-error')).toContainText(
      'this policy allows at most 3, but your basket contains 4',
    )
  })

  test('Razor: defaults to Standard and the top link switches to Elite, keeping facts', async ({ page }) => {
    await page.goto('/razor')
    await page.fill('#DateOfBirth', '2009-09-01')
    await page.selectOption('#Gcses_0__Subject', 'maths')
    await setRazorGcseGrade(page, 0, 8)
    await page.getByRole('button', { name: 'Save & see options' }).click()

    await expect(page.locator('.policy-switch')).toContainText('Standard')

    await page.locator('.policy-switch a', { hasText: 'Switch to Elite' }).click()

    await expect(page.locator('.policy-switch')).toContainText('Elite')
    await expect(page.locator('#DateOfBirth')).toHaveValue('2009-09-01')
  })

  test('Razor: an invalid ?policy= value redirects to the canonical URL', async ({ page }) => {
    await page.goto('/razor?policy=nonexistent')

    await expect(page).toHaveURL(/\/razor$/)
    await expect(page.locator('.policy-switch')).toContainText('Standard')
  })
})
