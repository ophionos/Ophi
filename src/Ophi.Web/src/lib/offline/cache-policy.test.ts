import { describe, it, expect } from 'vitest';
import {
	CACHED_AT_HEADER,
	precacheList,
	MAX_AGE_MS,
	endsSession,
	isFresh,
	isOfflineApi,
	stamp
} from './cache-policy';

const ORIGIN = 'https://ophi.test';
const url = (path: string) => new URL(path, ORIGIN);

describe('isOfflineApi', () => {
	it.each([
		'/api/v1/auth/me',
		'/api/v1/products',
		'/api/v1/products?page=2&includeSparkline=true',
		'/api/v1/products/0b7e3a1c-1111-2222-3333-444455556666',
		'/api/v1/products/0b7e3a1c-1111-2222-3333-444455556666/history?days=7',
		'/api/v1/tags',
		'/api/v1/stores',
		'/api/v1/comparisons'
	])('should keep a copy of %s when it is a read the offline pages need', (path) => {
		expect(isOfflineApi(url(path), ORIGIN)).toBe(true);
	});

	it.each([
		'/api/v1/products/export',
		'/api/v1/settings',
		'/api/v1/apikeys',
		'/api/v1/notifications',
		'/api/v1/live',
		'/dashboard'
	])('should not keep a copy of %s when the offline pages do not need it', (path) => {
		expect(isOfflineApi(url(path), ORIGIN)).toBe(false);
	});

	it('should not keep a copy when the request goes to another origin', () => {
		expect(isOfflineApi(new URL('/api/v1/products', 'http://localhost:5041'), ORIGIN)).toBe(false);
	});
});

describe('endsSession', () => {
	it.each([
		['POST', '/api/v1/auth/login'],
		['POST', '/api/v1/auth/logout'],
		['POST', '/api/v1/auth/register'],
		['DELETE', '/api/v1/account']
	])('should clear the saved data when the request is %s %s', (method, path) => {
		expect(endsSession(method, url(path), ORIGIN)).toBe(true);
	});

	it.each([
		['GET', '/api/v1/auth/me'],
		['POST', '/api/v1/products'],
		['DELETE', '/api/v1/products/1']
	])('should keep the saved data when the request is %s %s', (method, path) => {
		expect(endsSession(method, url(path), ORIGIN)).toBe(false);
	});
});

describe('stamp and isFresh', () => {
	it('should keep the body and status when the response is stamped', async () => {
		const stamped = await stamp(new Response('{"a":1}', { status: 200 }), 1000);

		expect(stamped.status).toBe(200);
		expect(stamped.headers.get(CACHED_AT_HEADER)).toBe('1000');
		expect(await stamped.text()).toBe('{"a":1}');
	});

	it('should treat a copy as fresh when it is younger than 7 days', async () => {
		const stamped = await stamp(new Response('x'), 0);

		expect(isFresh(stamped, MAX_AGE_MS)).toBe(true);
		expect(isFresh(stamped, MAX_AGE_MS + 1)).toBe(false);
	});

	it('should treat a copy as stale when it has no stamp', () => {
		expect(isFresh(new Response('x'), 0)).toBe(false);
	});
});

describe('precacheList', () => {
	it('should list each path once when the offline page is also a static file', () => {
		// Cache.addAll rejects duplicate requests, which fails the whole install.
		const list = precacheList(['/_app/a.js'], ['/offline.html', '/robots.txt'], '/offline.html');

		expect(list).toEqual(['/_app/a.js', '/offline.html', '/robots.txt']);
	});
});
