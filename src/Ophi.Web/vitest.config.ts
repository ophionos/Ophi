import { defineConfig } from 'vitest/config';
import { svelte } from '@sveltejs/vite-plugin-svelte';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const __dirname = dirname(fileURLToPath(import.meta.url));

export default defineConfig({
	plugins: [svelte()],
	resolve: {
		alias: {
			$lib: resolve(__dirname, './src/lib'),
			'$app/environment': resolve(__dirname, './src/test/mocks/app/environment.ts'),
			'$app/navigation': resolve(__dirname, './src/test/mocks/app/navigation.ts'),
			'$app/paths': resolve(__dirname, './src/test/mocks/app/paths.ts'),
			'$app/stores': resolve(__dirname, './src/test/mocks/app/stores.ts'),
			'$app/state': resolve(__dirname, './src/test/mocks/app/state.ts'),
			$app: resolve(__dirname, './src/test/mocks/app')
		},
		conditions: ['browser']
	},
	test: {
		include: ['src/**/*.{test,spec}.{js,ts}'],
		globals: true,
		environment: 'jsdom',
		setupFiles: ['./src/test/setup.ts'],
		// Tests must not depend on a developer's local .env (CI has none). The API client reads
		// VITE_API_URL at import time and throws if unset, so provide a fixed value for tests.
		env: {
			VITE_API_URL: 'http://localhost:5000/api/v1'
		},
		// Add more time for Svelte component compilation
		testTimeout: 10000,
		coverage: {
			provider: 'v8',
			reporter: ['text', 'json', 'html'],
			include: ['src/lib/**/*.{ts,svelte}'],
			exclude: ['src/**/*.test.ts', 'src/test/**/*']
		}
	}
});
