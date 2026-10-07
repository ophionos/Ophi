import js from '@eslint/js';
import tseslint from '@typescript-eslint/eslint-plugin';
import tsparser from '@typescript-eslint/parser';
import svelte from 'eslint-plugin-svelte';
import prettier from 'eslint-config-prettier';
import globals from 'globals';

export default [
	js.configs.recommended,
	prettier,
	{
		files: ['**/*.{js,ts}', '**/*.svelte.ts', '**/*.svelte.js'],
		languageOptions: {
			ecmaVersion: 2022,
			sourceType: 'module',
			parser: tsparser,
			parserOptions: {
				extraFileExtensions: ['.svelte']
			},
			globals: {
				...globals.browser,
				...globals.node,
				// TypeScript global types
				RequestInit: 'readonly',
				Response: 'readonly',
				Headers: 'readonly',
				HTMLCanvasElement: 'readonly',
				Event: 'readonly',
				KeyboardEvent: 'readonly',
				MouseEvent: 'readonly',
				FormData: 'readonly'
			}
		},
		plugins: {
			'@typescript-eslint': tseslint
		},
		rules: {
			...tseslint.configs.recommended.rules,
			'@typescript-eslint/no-unused-vars': [
				'warn',
				{ argsIgnorePattern: '^_', varsIgnorePattern: '^_' }
			],
			'@typescript-eslint/no-explicit-any': 'warn',
			'no-unused-vars': 'off'
		}
	},
	...svelte.configs['flat/recommended'],
	...svelte.configs['flat/prettier'],
	{
		rules: {
			// No `paths.base` is configured, so resolve() is a no-op
			'svelte/no-navigation-without-resolve': 'off'
		}
	},
	{
		files: ['**/*.svelte.ts', '**/*.svelte.js'],
		languageOptions: {
			parser: tsparser,
			parserOptions: {
				extraFileExtensions: ['.svelte']
			},
			globals: {
				$state: 'readonly',
				$derived: 'readonly',
				$effect: 'readonly',
				$props: 'readonly',
				$bindable: 'readonly',
				$inspect: 'readonly',
				$host: 'readonly'
			}
		}
	},
	{
		files: ['**/*.svelte'],
		languageOptions: {
			parser: svelte.parser,
			parserOptions: {
				parser: tsparser
			},
			globals: {
				...globals.browser,
				// TypeScript global types in Svelte files
				RequestInit: 'readonly',
				Response: 'readonly',
				Headers: 'readonly',
				HTMLCanvasElement: 'readonly',
				Event: 'readonly',
				KeyboardEvent: 'readonly',
				MouseEvent: 'readonly',
				FormData: 'readonly'
			}
		},
		rules: {
			// Svelte 5 uses $bindable() for two-way binding, which ESLint sees as unused
			'no-unused-vars': 'off',
			'@typescript-eslint/no-unused-vars': 'off',
			// Svelte 5 $state can use native Set/Map
			'svelte/prefer-svelte-reactivity': 'off'
		}
	},
	{
		files: ['**/service-worker.ts'],
		languageOptions: {
			globals: {
				ServiceWorkerGlobalScope: 'readonly'
			}
		}
	},
	{
		files: ['**/*.test.ts', '**/*.spec.ts', '**/test/**/*.ts', '**/e2e/**/*.ts'],
		languageOptions: {
			globals: {
				...globals.node,
				...globals.browser,
				describe: 'readonly',
				it: 'readonly',
				expect: 'readonly',
				vi: 'readonly',
				beforeEach: 'readonly',
				afterEach: 'readonly',
				beforeAll: 'readonly',
				afterAll: 'readonly',
				test: 'readonly'
			}
		},
		rules: {
			// Allow unused parameters in test fixtures (e.g., Playwright page)
			'@typescript-eslint/no-unused-vars': [
				'warn',
				{ argsIgnorePattern: '^_|^page$', varsIgnorePattern: '^_' }
			]
		}
	},
	{
		ignores: [
			'.svelte-kit/**',
			'build/**',
			'dist/**',
			'node_modules/**',
			'coverage/**',
			'playwright-report/**',
			'test-results/**'
		]
	}
];
