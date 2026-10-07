import '@testing-library/jest-dom/vitest';
import { beforeEach, vi } from 'vitest';

// Mock fetch globally
globalThis.fetch = vi.fn();

// Mock window.matchMedia for theme tests
Object.defineProperty(window, 'matchMedia', {
	writable: true,
	value: vi.fn().mockImplementation((query: string) => ({
		matches: false,
		media: query,
		onchange: null,
		addListener: vi.fn(),
		removeListener: vi.fn(),
		addEventListener: vi.fn(),
		removeEventListener: vi.fn(),
		dispatchEvent: vi.fn()
	}))
});

// Polyfill Web Animations API for Svelte transitions (fly, fade, etc.) in JSDOM
if (!Element.prototype.animate) {
	Element.prototype.animate = vi.fn().mockReturnValue({
		onfinish: null,
		cancel: vi.fn(),
		finish: vi.fn()
	});
}

// Reset mocks between tests
beforeEach(() => {
	vi.clearAllMocks();
});
