import type { Page } from '@playwright/test'

// Lets page.evaluate() callbacks read/write window.__e2eMarker without a cast — the property only
// ever exists inside the browser context these callbacks run in, never in this file's own Node context.
declare global {
  interface Window {
    __e2eMarker?: boolean
  }
}

/** A minimal, known-eligible fact set (min_passes: 5, pass_grade: 4 in data/thresholds.yaml) — enough for a Choose button to appear. */
export const GOLDEN_GCSES: readonly { subject: string; grade: number }[] = [
  { subject: 'maths', grade: 8 },
  { subject: 'english_language', grade: 8 },
  { subject: 'english_literature', grade: 8 },
  { subject: 'physics', grade: 8 },
  { subject: 'chemistry', grade: 8 },
]

/** Fills the Vue facts form with {@link GOLDEN_GCSES} and waits for the resulting evaluation to render. */
export async function fillGoldenFacts(page: Page): Promise<void> {
  await page.fill('#date-of-birth', '2009-09-01')

  for (const [index, row] of GOLDEN_GCSES.entries()) {
    await page.selectOption(`#gcse-subject-${index.toString()}`, row.subject)
    await page.fill(`#gcse-grade-${index.toString()}`, row.grade.toString())
  }

  await page.locator('.card').first().waitFor({ state: 'visible', timeout: 10_000 })
}

/** Only the single project named `projectName` runs this test — for checks that don't depend on viewport size. */
export function skipUnlessProject(testInfo: { project: { name: string } }, projectName: string): boolean {
  return testInfo.project.name !== projectName
}
