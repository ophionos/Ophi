import { describe, it, expect, vi } from 'vitest';
import { loadRegistrationOpen } from './registration';

function fakeFetch(impl: () => Promise<Partial<Response>>): typeof fetch {
	return vi.fn(impl) as unknown as typeof fetch;
}

describe('loadRegistrationOpen', () => {
	it('should report closed when the API says sign-up is closed', async () => {
		const fetcher = fakeFetch(() =>
			Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ open: false }) })
		);

		await expect(loadRegistrationOpen(fetcher)).resolves.toBe(false);
		expect(fetcher).toHaveBeenCalledWith(
			expect.stringContaining('/auth/registration'),
			expect.anything()
		);
	});

	it('should report open when the API says sign-up is open', async () => {
		const fetcher = fakeFetch(() =>
			Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ open: true }) })
		);

		await expect(loadRegistrationOpen(fetcher)).resolves.toBe(true);
	});

	it('should assume open when the API is unreachable', async () => {
		// The API enforces the setting on POST /auth/register regardless, so failing open only
		// risks showing a form that then explains sign-up is closed.
		const fetcher = fakeFetch(() => Promise.reject(new TypeError('network down')));

		await expect(loadRegistrationOpen(fetcher)).resolves.toBe(true);
	});
});
