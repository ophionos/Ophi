import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/svelte';
import PricePositionBar from './PricePositionBar.svelte';

describe('PricePositionBar', () => {
	it('should render bar when min < max and current provided', () => {
		const { container } = render(PricePositionBar, {
			props: { min: 50, max: 100, current: 75 }
		});
		expect(container.querySelector('[data-testid="price-position-bar"]')).toBeInTheDocument();
	});

	it('should not render when min equals max', () => {
		const { container } = render(PricePositionBar, {
			props: { min: 50, max: 50, current: 50 }
		});
		expect(container.querySelector('[data-testid="price-position-bar"]')).not.toBeInTheDocument();
	});

	it('should show marker at 0% when current equals min', () => {
		const { container } = render(PricePositionBar, {
			props: { min: 50, max: 100, current: 50 }
		});
		const marker = container.querySelector('[data-testid="position-marker"]') as HTMLElement;
		expect(marker).toBeInTheDocument();
		expect(marker.style.left).toBe('0%');
	});

	it('should show marker at 100% when current equals max', () => {
		const { container } = render(PricePositionBar, {
			props: { min: 50, max: 100, current: 100 }
		});
		const marker = container.querySelector('[data-testid="position-marker"]') as HTMLElement;
		expect(marker.style.left).toBe('100%');
	});

	it('should show marker at 50% for midpoint', () => {
		const { container } = render(PricePositionBar, {
			props: { min: 0, max: 100, current: 50 }
		});
		const marker = container.querySelector('[data-testid="position-marker"]') as HTMLElement;
		expect(marker.style.left).toBe('50%');
	});

	it('should clamp position to 0% when current below min', () => {
		const { container } = render(PricePositionBar, {
			props: { min: 50, max: 100, current: 30 }
		});
		const marker = container.querySelector('[data-testid="position-marker"]') as HTMLElement;
		expect(marker.style.left).toBe('0%');
	});

	it('should clamp position to 100% when current above max', () => {
		const { container } = render(PricePositionBar, {
			props: { min: 50, max: 100, current: 120 }
		});
		const marker = container.querySelector('[data-testid="position-marker"]') as HTMLElement;
		expect(marker.style.left).toBe('100%');
	});

	it('should use green fill for lower third position', () => {
		const { container } = render(PricePositionBar, {
			props: { min: 0, max: 100, current: 20 }
		});
		const fill = container.querySelector('[data-testid="position-fill"]') as HTMLElement;
		expect(fill.className).toContain('bg-green');
	});

	it('should use red fill for upper third position', () => {
		const { container } = render(PricePositionBar, {
			props: { min: 0, max: 100, current: 90 }
		});
		const fill = container.querySelector('[data-testid="position-fill"]') as HTMLElement;
		expect(fill.className).toContain('bg-red');
	});
});
