import type { Handle, RequestEvent } from '@sveltejs/kit';

// Headers a client could use to claim a different origin address or scheme. The API trusts
// X-Forwarded-For from this proxy (ForwardedHeaders:KnownIPNetworks) and keys its per-IP rate
// limits on the result, so passing a client-supplied value through would let the client pick
// its own rate-limit partition. Drop them all and set X-Forwarded-For ourselves.
const CLIENT_FORWARDING_HEADERS = [
	'forwarded',
	'x-forwarded-for',
	'x-forwarded-host',
	'x-forwarded-proto',
	'x-real-ip'
];

// Page responses only; API responses carry the API's own SecurityHeadersMiddleware set. No CSP
// here: Chart.js and inline styles need work before one can be enforced.
const PAGE_SECURITY_HEADERS: Record<string, string> = {
	'X-Frame-Options': 'DENY',
	'X-Content-Type-Options': 'nosniff',
	'Referrer-Policy': 'strict-origin-when-cross-origin'
};

function tryGetClientAddress(event: RequestEvent): string | undefined {
	try {
		return event.getClientAddress();
	} catch {
		return undefined;
	}
}

export function createHandle(apiUrl: string): Handle {
	return async ({ event, resolve }) => {
		if (event.url.pathname.startsWith('/api/')) {
			const targetUrl = `${apiUrl}${event.url.pathname}${event.url.search}`;

			const body =
				event.request.method !== 'GET' && event.request.method !== 'HEAD'
					? await event.request.arrayBuffer()
					: undefined;

			const headers = new Headers(event.request.headers);
			headers.delete('host');
			for (const name of CLIENT_FORWARDING_HEADERS) headers.delete(name);
			const clientAddress = tryGetClientAddress(event);
			if (clientAddress) headers.set('x-forwarded-for', clientAddress);

			const response = await fetch(targetUrl, {
				method: event.request.method,
				headers,
				body
			});

			// Strip hop-by-hop and content-coding headers before re-emitting. fetch()
			// auto-decompresses the upstream body but leaves Content-Encoding/Content-Length
			// in place; re-streaming the now-plaintext body with those headers makes the
			// browser try to gunzip plaintext (ERR_CONTENT_DECODING_FAILED) and mismatches
			// length. We drop them and let the runtime recompute.
			const responseHeaders = new Headers(response.headers);
			responseHeaders.delete('content-encoding');
			responseHeaders.delete('content-length');
			responseHeaders.delete('transfer-encoding');
			responseHeaders.delete('connection');
			responseHeaders.delete('keep-alive');

			return new Response(response.body, {
				status: response.status,
				statusText: response.statusText,
				headers: responseHeaders
			});
		}

		const response = await resolve(event);
		for (const [name, value] of Object.entries(PAGE_SECURITY_HEADERS)) {
			response.headers.set(name, value);
		}
		return response;
	};
}
