import { api } from '$lib/api/client';

/**
 * Whether the sign-up form and links should be offered. Only a UI hint: the API enforces
 * `Registration:Enabled` on POST /auth/register itself, so an unreachable API fails open and the
 * worst case is a form whose submit explains that sign-up is closed.
 */
export async function loadRegistrationOpen(fetcher: typeof fetch): Promise<boolean> {
	try {
		const status = await api.withFetch(fetcher).getRegistrationStatus();
		return status.open;
	} catch {
		return true;
	}
}
