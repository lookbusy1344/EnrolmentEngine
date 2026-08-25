import { expect, test } from '@playwright/test'

/**
 * Focused zoom-accessibility check, run across every configured viewport (phone through
 * wide-desktop — see playwright.config.ts): the shared _Layout.cshtml viewport meta tag must
 * never carry maximum-scale or user-scalable=no, which would block pinch zoom for low-vision
 * users. A structural markup/content check like this is deliberately not scoped to one viewport
 * project — the meta tag is static, but this proves it holds wherever a user might load the app,
 * not just on the reference desktop project.
 */
test('/app allows pinch zoom via its viewport meta tag', async ({ page }) => {
  await page.goto('/app')

  const viewportContent = await page.locator('head meta[name="viewport"]').getAttribute('content')

  expect(viewportContent).toBe('width=device-width, initial-scale=1')
})
