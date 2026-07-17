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
      input: 'src/main.ts',
    },
  },
})
