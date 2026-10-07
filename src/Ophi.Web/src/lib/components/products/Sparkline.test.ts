import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/svelte';
import Sparkline from './Sparkline.svelte';

describe('Sparkline', () => {
	const downTrend = [
		{ date: '2026-03-10', price: 100 },
		{ date: '2026-03-11', price: 95 },
		{ date: '2026-03-12', price: 90 },
		{ date: '2026-03-13', price: 85 },
		{ date: '2026-03-14', price: 80 }
	];

	const upTrend = [
		{ date: '2026-03-10', price: 80 },
		{ date: '2026-03-11', price: 85 },
		{ date: '2026-03-12', price: 90 },
		{ date: '2026-03-13', price: 95 },
		{ date: '2026-03-14', price: 100 }
	];

	const flatTrend = [
		{ date: '2026-03-10', price: 50 },
		{ date: '2026-03-11', price: 50 },
		{ date: '2026-03-12', price: 50 }
	];

	it('should render SVG element', () => {
		const { container } = render(Sparkline, { props: { data: downTrend } });
		const svg = container.querySelector('svg');
		expect(svg).toBeInTheDocument();
	});

	it('should render polyline when data has 3+ points', () => {
		const { container } = render(Sparkline, { props: { data: downTrend } });
		const polyline = container.querySelector('polyline');
		expect(polyline).toBeInTheDocument();
		expect(polyline?.getAttribute('points')).toBeTruthy();
	});

	it('should not render anything when data has only 2 points (degenerate diagonal stub)', () => {
		const { container } = render(Sparkline, {
			props: {
				data: [
					{ date: '2026-03-10', price: 100 },
					{ date: '2026-03-11', price: 120 }
				]
			}
		});
		const svg = container.querySelector('svg');
		expect(svg).not.toBeInTheDocument();
	});

	it('should not render anything when data has fewer than 2 points', () => {
		const { container } = render(Sparkline, {
			props: { data: [{ date: '2026-03-10', price: 100 }] }
		});
		const svg = container.querySelector('svg');
		expect(svg).not.toBeInTheDocument();
	});

	it('should not render anything when data is empty', () => {
		const { container } = render(Sparkline, { props: { data: [] } });
		const svg = container.querySelector('svg');
		expect(svg).not.toBeInTheDocument();
	});

	it('should use green stroke when price is trending down', () => {
		const { container } = render(Sparkline, { props: { data: downTrend } });
		const polyline = container.querySelector('polyline');
		expect(polyline?.getAttribute('stroke')).toContain('22, 163, 74');
	});

	it('should use red stroke when price is trending up', () => {
		const { container } = render(Sparkline, { props: { data: upTrend } });
		const polyline = container.querySelector('polyline');
		expect(polyline?.getAttribute('stroke')).toContain('239, 68, 68');
	});

	it('should use gray stroke when price is flat', () => {
		const { container } = render(Sparkline, { props: { data: flatTrend } });
		const polyline = container.querySelector('polyline');
		expect(polyline?.getAttribute('stroke')).toContain('156, 163, 175');
	});

	it('should respect custom width and height', () => {
		const { container } = render(Sparkline, {
			props: { data: downTrend, width: 100, height: 30 }
		});
		const svg = container.querySelector('svg');
		expect(svg?.getAttribute('width')).toBe('100');
		expect(svg?.getAttribute('height')).toBe('30');
	});
});
