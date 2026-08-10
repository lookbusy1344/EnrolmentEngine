// A focused process-launch test (F7): the apphost only searches registered runtime install
// locations, missing a per-user install under e.g. ~/.dotnet unless DOTNET_ROOT is set — see the
// comment in e2e-server.mjs. Proves the launcher resolves to `dotnet <dll>`, not the generated
// native apphost, without building or booting anything.
import path from 'node:path'
import { describe, expect, it } from 'vitest'
import { resolveServerCommand } from './e2e-server.mjs'

describe('resolveServerCommand', () => {
  it('launches the built dll through the dotnet command, not the native apphost', () => {
    const binDir = path.join('fake', 'bin', 'dir')

    const { command, args } = resolveServerCommand(binDir)

    expect(command).toBe('dotnet')
    expect(args).toEqual([path.join(binDir, 'EnrolmentRules.Web.dll')])
  })
})
