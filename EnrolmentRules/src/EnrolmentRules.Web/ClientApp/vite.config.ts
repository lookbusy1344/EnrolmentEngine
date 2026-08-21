import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// https://vite.dev/config/
export default defineConfig({
  base: '/app/',
  plugins: [vue()],
  build: {
    // A plain filename, not `true` (which nests it under `.vite/`): MSBuild's default Content glob
    // excludes dot-directories, so a `.vite/manifest.json` never reaches bin/ output or, per Vite's
    // own docs on framework integration, necessarily the publish payload either.
    manifest: 'manifest.json',
    outDir: '../wwwroot/app',
    emptyOutDir: true,
    rollupOptions: {
      // Independent entries in one manifest: main.ts is the /app Vue SPA; razor-sync.ts mirrors
      // /razor's server-rendered snapshot into the same localStorage key/shape main.ts's state
      // module owns; razorGradeWheel.ts upgrades /razor's server-rendered grade button groups into
      // the spin wheel /app renders natively (see ViteManifestReader).
      input: {
        main: 'src/main.ts',
        razorSync: 'src/razor-sync.ts',
        razorGradeWheel: 'src/gradeWheel/razorGradeWheel.ts',
      },
    },
  },
})
