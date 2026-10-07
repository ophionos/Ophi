import { describe, it, expect, vi, afterEach } from 'vitest';
import { isValidHttpUrl, formatCheckInterval, formatTimeAgo, formatPrice } from './format';

describe('formatPrice', () => {
	it('should format USD with a leading symbol', () => {
		expect(formatPrice(189, 'USD')).toBe('$189.00');
	});

	it('should format EUR with the euro symbol', () => {
		expect(formatPrice(17.91, 'EUR')).toBe('€17.91');
	});

	it('should format GBP with the pound symbol', () => {
		expect(formatPrice(1999.99, 'GBP')).toBe('£1,999.99');
	});

	it('should respect zero-decimal currencies', () => {
		expect(formatPrice(1200, 'JPY')).toBe('¥1,200');
	});

	it('should render "<code> <amount>" for unknown-but-well-formed currency codes', () => {
		// Intl handles syntactically valid unknown codes itself (with a non-breaking space).
		expect(formatPrice(50, 'ZZZ')).toMatch(/^ZZZ\s50\.00$/);
	});

	it('should fall back gracefully for non-ISO currency strings', () => {
		expect(formatPrice(50, '')).toBe('50.00');
		expect(formatPrice(50, 'kr.')).toBe('kr. 50.00');
	});
});

describe('isValidHttpUrl', () => {
	it('should return true for valid http URL', () => {
		expect(isValidHttpUrl('http://example.com')).toBe(true);
	});

	it('should return true for valid https URL', () => {
		expect(isValidHttpUrl('https://example.com')).toBe(true);
	});

	it('should return true for URL with path and query', () => {
		expect(isValidHttpUrl('https://example.com/path?q=search&page=1')).toBe(true);
	});

	it('should return false for ftp URL', () => {
		expect(isValidHttpUrl('ftp://example.com')).toBe(false);
	});

	it('should return false for empty string', () => {
		expect(isValidHttpUrl('')).toBe(false);
	});

	it('should return false for plain text', () => {
		expect(isValidHttpUrl('not a url')).toBe(false);
	});

	it('should return false for URL without protocol', () => {
		expect(isValidHttpUrl('example.com')).toBe(false);
	});

	it('should return false for javascript protocol', () => {
		expect(isValidHttpUrl('javascript:alert(1)')).toBe(false);
	});
});

describe('formatCheckInterval', () => {
	it('should format exact hours', () => {
		expect(formatCheckInterval(60)).toBe('1h');
		expect(formatCheckInterval(120)).toBe('2h');
		expect(formatCheckInterval(1440)).toBe('24h');
	});

	it('should format minutes only when under an hour', () => {
		expect(formatCheckInterval(15)).toBe('15m');
		expect(formatCheckInterval(30)).toBe('30m');
		expect(formatCheckInterval(59)).toBe('59m');
	});

	it('should format hours and minutes for mixed values', () => {
		expect(formatCheckInterval(90)).toBe('1h 30m');
		expect(formatCheckInterval(150)).toBe('2h 30m');
		expect(formatCheckInterval(75)).toBe('1h 15m');
	});
});

describe('formatTimeAgo', () => {
	afterEach(() => {
		vi.useRealTimers();
	});

	it('should return "just now" for less than 60 seconds ago', () => {
		const now = new Date();
		expect(formatTimeAgo(now.toISOString())).toBe('just now');
	});

	it('should return minutes ago', () => {
		vi.useFakeTimers();
		const now = new Date('2026-01-15T12:00:00Z');
		vi.setSystemTime(now);

		const fiveMinAgo = new Date('2026-01-15T11:55:00Z');
		expect(formatTimeAgo(fiveMinAgo.toISOString())).toBe('5m ago');
	});

	it('should return hours ago', () => {
		vi.useFakeTimers();
		const now = new Date('2026-01-15T12:00:00Z');
		vi.setSystemTime(now);

		const threeHoursAgo = new Date('2026-01-15T09:00:00Z');
		expect(formatTimeAgo(threeHoursAgo.toISOString())).toBe('3h ago');
	});

	it('should return days ago', () => {
		vi.useFakeTimers();
		const now = new Date('2026-01-15T12:00:00Z');
		vi.setSystemTime(now);

		const twoDaysAgo = new Date('2026-01-13T12:00:00Z');
		expect(formatTimeAgo(twoDaysAgo.toISOString())).toBe('2d ago');
	});

	it('should return formatted date for more than a week ago', () => {
		vi.useFakeTimers();
		const now = new Date('2026-01-15T12:00:00Z');
		vi.setSystemTime(now);

		const twoWeeksAgo = new Date('2026-01-01T12:00:00Z');
		const result = formatTimeAgo(twoWeeksAgo.toISOString());
		// Should be a localized date string, not a relative time
		expect(result).not.toContain('ago');
		expect(result).not.toBe('just now');
	});
});
