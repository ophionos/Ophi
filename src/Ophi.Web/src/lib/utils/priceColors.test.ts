import { describe, it, expect } from 'vitest';
import { priceChangeTextClass, priceChangeChipClass } from './priceColors';

describe('priceChangeTextClass', () => {
	it('should use the price-drop token when the price went down', () => {
		expect(priceChangeTextClass(-4.8)).toBe('text-price-drop');
	});

	it('should use the price-increase token when the price went up', () => {
		expect(priceChangeTextClass(3)).toBe('text-price-increase');
	});

	it('should use the unchanged token when there is no change', () => {
		expect(priceChangeTextClass(0)).toBe('text-price-unchanged');
		expect(priceChangeTextClass(undefined)).toBe('text-price-unchanged');
		expect(priceChangeTextClass(null)).toBe('text-price-unchanged');
	});
});

describe('priceChangeChipClass', () => {
	it('should pair the subtle background with the text token for each direction', () => {
		expect(priceChangeChipClass(-1)).toBe('bg-price-drop-subtle text-price-drop');
		expect(priceChangeChipClass(1)).toBe('bg-price-increase-subtle text-price-increase');
		expect(priceChangeChipClass(0)).toBe('bg-price-unchanged-subtle text-price-unchanged');
	});
});
