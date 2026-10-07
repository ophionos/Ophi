import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import { tick } from 'svelte';
import NavigationProgress from './NavigationProgress.svelte';

describe('NavigationProgress', () => {
	beforeEach(() => vi.useFakeTimers());
	afterEach(() => vi.useRealTimers());

	it('should render nothing while idle', () => {
		render(NavigationProgress, { props: { active: false } });
		expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
	});

	it('should not flash for navigations faster than the delay', async () => {
		const { rerender } = render(NavigationProgress, { props: { active: true } });
		vi.advanceTimersByTime(100);
		await rerender({ active: false });
		vi.advanceTimersByTime(200);
		await tick();
		expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
	});

	it('should appear once a navigation outlasts the delay and hide when it ends', async () => {
		const { rerender } = render(NavigationProgress, { props: { active: true } });
		vi.advanceTimersByTime(200);
		await tick();
		expect(screen.getByRole('progressbar', { name: 'Loading page' })).toBeInTheDocument();

		await rerender({ active: false });
		expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
	});
});
