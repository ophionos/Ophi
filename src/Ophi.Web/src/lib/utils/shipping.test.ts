import { describe, it, expect } from 'vitest';
import { shippingHost, findShippingRate } from './shipping';

describe('shippingHost', () => {
	it('should lowercase the host and strip a leading www when given a URL', () => {
		expect(shippingHost('https://WWW.Amazon.ES/dp/B1')).toBe('amazon.es');
	});

	it('should keep other subdomains when given a URL', () => {
		expect(shippingHost('https://shop.example.com/x')).toBe('shop.example.com');
	});

	it('should normalize a bare host when the input is not a URL', () => {
		expect(shippingHost('  WWW.Example.com ')).toBe('example.com');
	});
});

describe('findShippingRate', () => {
	const rates = [{ host: 'amazon.es', amount: 3, currency: 'EUR' }];

	it('should find a rate case-insensitively when the host matches', () => {
		expect(findShippingRate(rates, 'WWW.amazon.es')?.amount).toBe(3);
	});

	it('should return undefined when the host has no rate', () => {
		expect(findShippingRate(rates, 'other.com')).toBeUndefined();
		expect(findShippingRate(rates, null)).toBeUndefined();
	});
});
