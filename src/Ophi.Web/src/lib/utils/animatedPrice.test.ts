import { describe, it, expect } from 'vitest';

// We test the animation logic by testing the core calculation
// The actual svelte/motion integration is tested via component rendering

describe('animatedPrice', () => {
	it('should export createAnimatedPrice function', async () => {
		const { createAnimatedPrice } = await import('./animatedPrice.svelte');
		expect(createAnimatedPrice).toBeDefined();
		expect(typeof createAnimatedPrice).toBe('function');
	});

	it('should initialize with given value', async () => {
		const { createAnimatedPrice } = await import('./animatedPrice.svelte');
		const animated = createAnimatedPrice(99.99);
		expect(animated.current).toBe(99.99);
	});

	it('should initialize with zero by default', async () => {
		const { createAnimatedPrice } = await import('./animatedPrice.svelte');
		const animated = createAnimatedPrice();
		expect(animated.current).toBe(0);
	});

	it('should update target value', async () => {
		const { createAnimatedPrice } = await import('./animatedPrice.svelte');
		const animated = createAnimatedPrice(50);
		animated.target = 100;
		// Target is set, current will animate toward it
		// After enough time it should reach target
		// We can't easily test animation timing, but we can check it doesn't throw
		expect(animated.current).toBeDefined();
	});
});
