// Fails if any hand-written vanilla JavaScript module has crept into ClientApp/src — TypeScript is
// mandatory for application source (see CLAUDE.md). Plain Node so this check needs no extra
// dependency; it lives outside src/, so it is not itself subject to the rule it enforces.
import { readdirSync, statSync } from 'node:fs'
import { join } from 'node:path'

const srcDir = new URL('../src', import.meta.url).pathname
const bannedExtensions = ['.js', '.jsx', '.mjs', '.cjs']

function findBannedFiles(dir) {
  const found = []
  for (const entry of readdirSync(dir)) {
    const path = join(dir, entry)
    if (statSync(path).isDirectory()) {
      found.push(...findBannedFiles(path))
    } else if (bannedExtensions.some((ext) => entry.endsWith(ext))) {
      found.push(path)
    }
  }

  return found
}

const bannedFiles = findBannedFiles(srcDir)
if (bannedFiles.length > 0) {
  console.error('Hand-written vanilla JavaScript is not allowed under ClientApp/src:')
  for (const file of bannedFiles) {
    console.error(`  ${file}`)
  }

  process.exit(1)
}
