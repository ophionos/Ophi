import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/svelte';
import DealScore from './DealScore.svelte';

describe('DealScore', () => {
	it('should render SVG with radial progress ring', () => {
		const { container } = render(DealScore, { props: { score: 75 } });
		const svg = container.querySelector('svg');
		expect(svg).toBeTruthy();
		const circles = container.querySelectorAll('circle');
		expect(circles.length).toBeGreaterThanOrEqual(2); // background + progress
	});

	it('should display score number in center', () => {
		const { container } = render(DealScore, { props: { score: 42 } });
		const text = container.querySelector('text');
		expect(text?.textContent?.trim()).toBe('42');
	});

	it('should use green color when score > 60', () => {
		const { container } = render(DealScore, { props: { score: 85 } });
		const progressCircle = container.querySelectorAll('circle')[1];
		expect(progressCircle?.getAttribute('stroke')).toContain('#22c55e');
	});

	it('should use amber color when score 30-60', () => {
		const { container } = render(DealScore, { props: { score: 45 } });
		const progressCircle = container.querySelectorAll('circle')[1];
		expect(progressCircle?.getAttribute('stroke')).toContain('#f59e0b');
	});

	it('should use red color when score < 30', () => {
		const { container } = render(DealScore, { props: { score: 15 } });
		const progressCircle = container.querySelectorAll('circle')[1];
		expect(progressCircle?.getAttribute('stroke')).toContain('#ef4444');
	});

	it('should set correct stroke-dashoffset for score', () => {
		const { container } = render(DealScore, { props: { score: 50 } });
		const progressCircle = container.querySelectorAll('circle')[1];
		const dashoffset = progressCircle?.style.strokeDashoffset;
		// circumference = 2 * PI * 9 ≈ 56.55; 50% = offset of ~28.27
		expect(dashoffset).toBeTruthy();
	});

	it('should have accessible aria-label', () => {
		const { container } = render(DealScore, { props: { score: 72 } });
		const svg = container.querySelector('svg');
		expect(svg?.getAttribute('aria-label')).toBe('Deal score: 72 out of 100');
	});

	it('should clamp score to 0-100 range', () => {
		const { container } = render(DealScore, { props: { score: 150 } });
		const text = container.querySelector('text');
		expect(text?.textContent?.trim()).toBe('100');
	});

	it('should handle score of 0', () => {
		const { container } = render(DealScore, { props: { score: 0 } });
		const text = container.querySelector('text');
		expect(text?.textContent?.trim()).toBe('0');
	});
});
