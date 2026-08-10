import js from '@eslint/js'
import pluginVue from 'eslint-plugin-vue'
import tseslint from 'typescript-eslint'
import globals from 'globals'

// eslint-plugin-vue's config objects aren't typed precisely enough for `Linter.Config[]`/
// `defineConfig` (see https://github.com/vuejs/eslint-plugin-vue/issues/2606) — left untyped here
// rather than fighting that with a cast; ESLint validates the actual shape at runtime regardless.
export default [
  { ignores: ['dist/**', 'node_modules/**', 'scripts/**'] },
  js.configs.recommended,
  ...tseslint.configs.strictTypeChecked,
  ...pluginVue.configs['flat/recommended'],
  // Disables eslint-plugin-vue's formatting rules (attribute line breaks, self-closing void
  // elements, etc.) — Prettier owns .vue formatting; without this the two fight over the same lines.
  pluginVue.configs['no-layout-rules'],
  {
    files: ['**/*.vue'],
    languageOptions: {
      parserOptions: {
        parser: tseslint.parser,
        extraFileExtensions: ['.vue'],
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
      },
    },
  },
  {
    files: ['**/*.ts', '**/*.vue'],
    languageOptions: {
      globals: { ...globals.browser, ...globals.node },
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
      },
    },
    rules: {
      '@typescript-eslint/no-explicit-any': 'error',
      '@typescript-eslint/no-unsafe-assignment': 'error',
      '@typescript-eslint/no-unsafe-member-access': 'error',
      '@typescript-eslint/no-unsafe-call': 'error',
      '@typescript-eslint/no-unsafe-return': 'error',
      '@typescript-eslint/no-unnecessary-type-assertion': 'error',
      '@typescript-eslint/consistent-type-imports': 'error',
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],
    },
  },
  {
    // Playwright's test.beforeEach/test callbacks require a literal destructuring pattern as their
    // first (fixtures) parameter — it inspects the function source to know which fixtures to inject —
    // so `({}, testInfo) => ...` is the correct call shape here, not an accidental empty pattern.
    files: ['e2e/**/*.ts'],
    rules: {
      'no-empty-pattern': 'off',
    },
  },
]
