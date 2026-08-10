// Builds and launches the real ASP.NET-hosted app for Playwright to drive — not the Vite dev
// server, so /razor and /app render through the same _Layout, static files, and API as production.
// `dotnet run` doesn't work here (see CLAUDE.md: content root is the source dir under `dotnet run`,
// but workflows/ and data/ only exist in the build *output*), so this builds then launches the
// built EnrolmentRules.Web.dll through `dotnet`, from its own output directory, exactly as
// CLAUDE.md documents doing by hand.
import { spawn, spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const scriptsDir = path.dirname(fileURLToPath(import.meta.url))
const repoRoot = path.resolve(scriptsDir, '../../../..')
const webProjectDir = path.join(repoRoot, 'src', 'EnrolmentRules.Web')
const webProject = path.join(webProjectDir, 'EnrolmentRules.Web.csproj')
const webBinDir = path.join(webProjectDir, 'bin', 'Debug', 'net10.0')

// Launched through the same `dotnet` command that just built it, rather than the generated native
// apphost directly: the apphost only searches registered runtime install locations and misses a
// per-user install under e.g. ~/.dotnet unless DOTNET_ROOT is set, while `dotnet <dll>` resolves the
// runtime the same way the build command already did — no DOTNET_ROOT required. Exported as a pure
// function (no process spawned) so the launcher's resolved command/args are unit-testable without
// building or booting anything.
export function resolveServerCommand(binDir) {
  return { command: 'dotnet', args: [path.join(binDir, 'EnrolmentRules.Web.dll')] }
}

// Only run the build-and-launch side effects when executed directly (`node scripts/e2e-server.mjs`),
// not when imported by a test for resolveServerCommand.
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const build = spawnSync('dotnet', ['build', webProject], { stdio: 'inherit' })
  if (build.status !== 0) {
    process.exit(build.status ?? 1)
  }

  const port = process.env.E2E_PORT ?? '5310'
  const { command, args } = resolveServerCommand(webBinDir)
  const server = spawn(command, args, {
    cwd: webBinDir,
    stdio: 'inherit',
    env: { ...process.env, ASPNETCORE_URLS: `http://localhost:${port}`, ASPNETCORE_ENVIRONMENT: 'Development' },
  })

  for (const signal of ['SIGINT', 'SIGTERM']) {
    process.on(signal, () => {
      server.kill(signal)
    })
  }

  server.on('exit', (code) => {
    process.exit(code ?? 0)
  })
}
