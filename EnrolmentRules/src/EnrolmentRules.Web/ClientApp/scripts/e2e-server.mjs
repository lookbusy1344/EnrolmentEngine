// Builds and launches the real ASP.NET-hosted app for Playwright to drive — not the Vite dev
// server, so /razor and /app render through the same _Layout, static files, and API as production.
// `dotnet run` doesn't work here (see CLAUDE.md: content root is the source dir under `dotnet run`,
// but workflows/ and data/ only exist in the build *output*), so this builds then launches the
// compiled executable from its own output directory, exactly as CLAUDE.md documents doing by hand.
import { spawn, spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const scriptsDir = path.dirname(fileURLToPath(import.meta.url))
const repoRoot = path.resolve(scriptsDir, '../../../..')
const webProjectDir = path.join(repoRoot, 'src', 'EnrolmentRules.Web')
const webProject = path.join(webProjectDir, 'EnrolmentRules.Web.csproj')
const webBinDir = path.join(webProjectDir, 'bin', 'Debug', 'net10.0')
const exeName = process.platform === 'win32' ? 'EnrolmentRules.Web.exe' : 'EnrolmentRules.Web'

const build = spawnSync('dotnet', ['build', webProject], { stdio: 'inherit' })
if (build.status !== 0) {
  process.exit(build.status ?? 1)
}

const port = process.env.E2E_PORT ?? '5310'
const server = spawn(path.join(webBinDir, exeName), [], {
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
