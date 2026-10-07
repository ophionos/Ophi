import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import ScanAnimation from './ScanAnimation.svelte';

describe('ScanAnimation', () => {
	beforeEach(() => {
		vi.useFakeTimers();
	});

	afterEach(() => {
		vi.useRealTimers();
	});

	it('should render with initial stage', () => {
		render(ScanAnimation);
		expect(screen.getByText('Connecting...')).toBeTruthy();
	});

	it('should progress to reading stage', async () => {
		render(ScanAnimation);
		vi.advanceTimersByTime(1500);
		await vi.runAllTicks();
		expect(screen.getByText('Reading page...')).toBeTruthy();
	});

	it('should progress to extracting stage', async () => {
		render(ScanAnimation);
		vi.advanceTimersByTime(3500);
		await vi.runAllTicks();
		expect(screen.getByText('Extracting price...')).toBeTruthy();
	});

	it('should show scan line animation element', () => {
		const { container } = render(ScanAnimation);
		expect(container.querySelector('[data-testid="scan-line"]')).toBeTruthy();
	});

	it('should display a simulated page outline', () => {
		const { container } = render(ScanAnimation);
		expect(container.querySelector('[data-testid="page-outline"]')).toBeTruthy();
	});
});
