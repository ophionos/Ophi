import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import ConvertedPrice from './ConvertedPrice.svelte';
import { fx } from '$lib/stores/fx.svelte';

const rates = { EUR: 1, USD: 1.1367, GBP: 0.85986 };
const fresh = new Date().toISOString();

beforeEach(() => fx.reset());

describe('ConvertedPrice', () => {
	it('should render nothing when no display currency is set', () => {
		fx.set({ displayCurrency: null, rates, asOf: fresh });
		const { container } = render(ConvertedPrice, { props: { amount: 100, currency: 'EUR' } });
		expect(container.textContent?.trim()).toBe('');
	});

	it('should render nothing when the price is already in the display currency', () => {
		fx.set({ displayCurrency: 'EUR', rates, asOf: fresh });
		const { container } = render(ConvertedPrice, { props: { amount: 100, currency: 'EUR' } });
		expect(container.textContent?.trim()).toBe('');
	});

	it('should render nothing for a currency without an ECB rate', () => {
		fx.set({ displayCurrency: 'USD', rates, asOf: fresh });
		const { container } = render(ConvertedPrice, { props: { amount: 100, currency: 'ARS' } });
		expect(container.textContent?.trim()).toBe('');
	});

	it('should show the approximate converted value, naming the rate source', () => {
		fx.set({ displayCurrency: 'USD', rates, asOf: '2026-09-24T00:00:00Z' });
		vi.setSystemTime(new Date('2026-09-25T12:00:00Z'));
		render(ConvertedPrice, { props: { amount: 100, currency: 'EUR' } });
		const el = screen.getByTestId('converted-price');
		expect(el).toHaveTextContent('≈ $113.67');
		expect(el).toHaveAttribute('title', expect.stringContaining('ECB reference rate'));
		vi.useRealTimers();
	});

	it('should label stale rates with their date', () => {
		fx.set({ displayCurrency: 'USD', rates, asOf: '2026-09-10T00:00:00Z' });
		vi.setSystemTime(new Date('2026-09-25T12:00:00Z'));
		render(ConvertedPrice, { props: { amount: 100, currency: 'EUR' } });
		expect(screen.getByTestId('converted-price')).toHaveTextContent(/rates from Sep 10/);
		vi.useRealTimers();
	});
});
