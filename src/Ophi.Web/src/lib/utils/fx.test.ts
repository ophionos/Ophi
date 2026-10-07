import { describe, it, expect } from 'vitest';
import { convert, isStale } from './fx';

// Units per 1 EUR, as the ECB publishes them.
const rates = { EUR: 1, USD: 1.1367, GBP: 0.85986, JPY: 180.57 };

describe('convert', () => {
	it('should convert from EUR by multiplying', () => {
		expect(convert(100, 'EUR', 'USD', rates)).toBeCloseTo(113.67, 2);
	});

	it('should convert to EUR by dividing', () => {
		expect(convert(113.67, 'USD', 'EUR', rates)).toBeCloseTo(100, 2);
	});

	it('should cross-convert through EUR', () => {
		// 100 GBP -> EUR -> USD = 100 / 0.85986 * 1.1367
		expect(convert(100, 'GBP', 'USD', rates)).toBeCloseTo(132.2, 2);
	});

	it('should be case-insensitive on currency codes', () => {
		expect(convert(100, 'eur', 'usd', rates)).toBeCloseTo(113.67, 2);
	});

	it('should return null when either currency has no rate, rather than guess', () => {
		expect(convert(100, 'ARS', 'USD', rates)).toBeNull();
		expect(convert(100, 'USD', 'ARS', rates)).toBeNull();
	});

	it('should return null for a same-currency pair, since there is nothing to show', () => {
		expect(convert(100, 'USD', 'USD', rates)).toBeNull();
	});
});

describe('isStale', () => {
	const now = new Date('2026-09-25T12:00:00Z');

	it('should treat rates up to 4 days old as current (covers ECB holiday gaps)', () => {
		expect(isStale('2026-09-22T00:00:00Z', now)).toBe(false);
	});

	it('should treat older rates as stale', () => {
		expect(isStale('2026-09-21T00:00:00Z', now)).toBe(true);
	});
});
