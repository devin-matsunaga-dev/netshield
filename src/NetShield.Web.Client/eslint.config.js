import js from '@eslint/js';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import globals from 'globals';
import tseslint from 'typescript-eslint';

/**
 * CONVENTIONS.md §6: TypeScript strict, no `any`, and no non-null assertion except immediately
 * after a runtime guard. The first two are errors here; the third is not expressible as a rule,
 * so `!` is banned outright and the guarded case is written as a check instead.
 */
export default tseslint.config(
  { ignores: ['dist', 'src/routeTree.gen.ts', 'src/api/schema.d.ts'] },
  js.configs.recommended,
  {
    files: ['**/*.{ts,tsx}'],
    extends: [...tseslint.configs.strictTypeChecked],
    languageOptions: {
      ecmaVersion: 2023,
      globals: globals.browser,
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
      },
    },
    plugins: {
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      'react-refresh/only-export-components': ['warn', { allowConstantExport: true }],
      '@typescript-eslint/no-explicit-any': 'error',
      '@typescript-eslint/no-non-null-assertion': 'error',
      '@typescript-eslint/consistent-type-imports': 'error',
    },
  },
  {
    // TanStack Router signals a redirect by throwing the object `redirect()` returns. It is not
    // an error and is never meant to reach a catch block that treats it as one.
    files: ['src/routes/**/*.tsx'],
    rules: { '@typescript-eslint/only-throw-error': 'off' },
  },
  {
    files: ['vite.config.ts', 'tailwind.config.ts'],
    languageOptions: { globals: globals.node },
  },
  {
    // The flat config itself is plain JavaScript and has no type information to lint against.
    files: ['**/*.js'],
    extends: [tseslint.configs.disableTypeChecked],
    languageOptions: { globals: globals.node },
  },
);
