import { expect, type Page } from '@playwright/test'

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

/**
 * {@link GOLDEN_GCSES} with Music in place of Chemistry. With no `plays_*` hobby listed, Music A-level
 * comes back amber ("requires own-time practice — authorisation required"), so it can be chosen but is
 * borderline — the only fact set here that puts a non-green subject in the basket.
 */
export const BORDERLINE_GCSES: readonly { subject: string; grade: number }[] = [
  ...GOLDEN_GCSES.slice(0, 4),
  { subject: 'music', grade: 8 },
]

/** Fills the Vue facts form with {@link GOLDEN_GCSES} and waits for the resulting evaluation to render. */
export async function fillGoldenFacts(page: Page): Promise<void> {
  await fillFacts(page, GOLDEN_GCSES)
}

/** Fills the Vue facts form with {@link BORDERLINE_GCSES} and waits for the resulting evaluation to render. */
export async function fillBorderlineFacts(page: Page): Promise<void> {
  await fillFacts(page, BORDERLINE_GCSES)
}

async function fillFacts(page: Page, gcses: readonly { subject: string; grade: number }[]): Promise<void> {
  await page.fill('#date-of-birth', '2009-09-01')

  for (const [index, row] of gcses.entries()) {
    await page.selectOption(`#gcse-subject-${index.toString()}`, row.subject)
    await setGcseGrade(page, index, row.grade)
  }

  await page.locator('.card').first().waitFor({ state: 'visible', timeout: 10_000 })
}

/**
 * Keys a grade into the grade wheel identified by its label element id: focus the row's slider and
 * press the digit. The digit keys map straight to a grade and centre it exactly, so this is stable
 * across viewports and both front ends — unlike clicking a cell that may be rotated off-centre and
 * faded under the drum, where the scroll track intercepts the click.
 */
export async function keyGradeWheel(page: Page, gradeLabelId: string, grade: number): Promise<void> {
  const track = page.locator(`[role="slider"][aria-labelledby="${gradeLabelId}"]`)
  await track.focus()
  await track.press(grade.toString())
  await expect(track).toHaveAttribute('aria-valuenow', grade.toString())
}

/** Sets a grade on the Vue (/app) facts form. */
export async function setGcseGrade(page: Page, index: number, grade: number): Promise<void> {
  await keyGradeWheel(page, `gcse-grade-label-${index.toString()}`, grade)
}

/** Sets a grade on the server-rendered (/razor) facts form. */
export async function setRazorGcseGrade(page: Page, index: number, grade: number): Promise<void> {
  await keyGradeWheel(page, `Gcses_${index.toString()}__GradeLabel`, grade)
}

/** Waits until /app's localStorage write has landed (it runs on Vue's watcher tick) and mentions `expectedFact`. */
export async function waitForStoredFacts(page: Page, expectedFact: string): Promise<void> {
  await page.waitForFunction(
    (fact) => (window.localStorage.getItem('enrolmentRules.vue.snapshot.v1') ?? '').includes(fact),
    expectedFact,
    { timeout: 10_000 },
  )
}

/** Only the single project named `projectName` runs this test — for checks that don't depend on viewport size. */
export function skipUnlessProject(testInfo: { project: { name: string } }, projectName: string): boolean {
  return testInfo.project.name !== projectName
}
