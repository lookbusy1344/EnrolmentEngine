import { describe, expect, it } from 'vitest'
import html from '../../index.html?raw'

// index.html is the Vite dev-server shell (only served by `vite dev`, not the ASP.NET host — see
// AppShellTests for the shared _Layout.cshtml viewport, which /app serves instead).
// Imported raw rather than driving a browser: this is a static file, not rendered markup.
describe('index.html viewport', () => {
  it('allows pinch zoom: no maximum-scale or user-scalable restriction', () => {
    expect(html).toMatch(/<meta content="width=device-width, initial-scale=1\.0" name="viewport" \/>/)
    expect(html).not.toContain('maximum-scale')
    expect(html).not.toContain('user-scalable')
  })
})
