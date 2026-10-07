import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import type { RequestEvent } from '@sveltejs/kit';

import { createHandle } from './handle';

const handle = createHandle('http://api:5000');

type HandleInput = Parameters<typeof handle>[0];

function makeEvent(
	path: string,
	init: RequestInit = {},
	getClientAddress: () => string = () => '203.0.113.7'
): RequestEvent {
	const url = new URL(`http://localhost:3000${path}`);
	return {
		url,
		request: new Request(url, init),
		getClientAddress
	} as unknown as RequestEvent;
}

async function runHandle(
	event: RequestEvent,
	resolve = vi.fn(async () => new Response('<html></html>'))
) {
	return handle({ event, resolve } as unknown as HandleInput);
}

describe('createHandle', () => {
	let fetchMock: ReturnType<typeof vi.fn>;

	beforeEach(() => {
		fetchMock = vi.fn(async () => new Response('{}', { status: 200 }));
		vi.stubGlobal('fetch', fetchMock);
	});

	afterEach(() => {
		vi.unstubAllGlobals();
	});

	function forwardedHeaders(): Headers {
		const init = fetchMock.mock.calls[0][1] as RequestInit;
		return init.headers as Headers;
	}

	it('should set X-Forwarded-For to the client address when proxying to the API', async () => {
		await runHandle(makeEvent('/api/v1/auth/login', { method: 'POST', body: '{}' }));

		expect(fetchMock).toHaveBeenCalledWith('http://api:5000/api/v1/auth/login', expect.anything());
		expect(forwardedHeaders().get('x-forwarded-for')).toBe('203.0.113.7');
	});

	it('should overwrite a client-supplied X-Forwarded-For when proxying to the API', async () => {
		await runHandle(
			makeEvent('/api/v1/auth/login', {
				method: 'POST',
				body: '{}',
				headers: { 'X-Forwarded-For': '1.2.3.4, 5.6.7.8' }
			})
		);

		expect(forwardedHeaders().get('x-forwarded-for')).toBe('203.0.113.7');
	});

	it('should drop other client-supplied forwarding headers when proxying to the API', async () => {
		await runHandle(
			makeEvent('/api/v1/products', {
				headers: {
					'X-Forwarded-Proto': 'https',
					'X-Forwarded-Host': 'evil.example',
					'X-Real-IP': '1.2.3.4',
					Forwarded: 'for=1.2.3.4'
				}
			})
		);

		const headers = forwardedHeaders();
		expect(headers.get('x-forwarded-proto')).toBeNull();
		expect(headers.get('x-forwarded-host')).toBeNull();
		expect(headers.get('x-real-ip')).toBeNull();
		expect(headers.get('forwarded')).toBeNull();
	});

	it('should proxy without X-Forwarded-For when the client address is unavailable', async () => {
		await runHandle(
			makeEvent('/api/v1/products', { headers: { 'X-Forwarded-For': '1.2.3.4' } }, () => {
				throw new Error('Could not determine clientAddress');
			})
		);

		expect(fetchMock).toHaveBeenCalledOnce();
		expect(forwardedHeaders().get('x-forwarded-for')).toBeNull();
	});

	it('should set security headers when serving a page', async () => {
		const response = await runHandle(makeEvent('/dashboard'));

		expect(response.headers.get('x-frame-options')).toBe('DENY');
		expect(response.headers.get('x-content-type-options')).toBe('nosniff');
		expect(response.headers.get('referrer-policy')).toBe('strict-origin-when-cross-origin');
		expect(fetchMock).not.toHaveBeenCalled();
	});

	it('should not add a content security policy when serving a page', async () => {
		const response = await runHandle(makeEvent('/dashboard'));

		expect(response.headers.get('content-security-policy')).toBeNull();
	});
});
