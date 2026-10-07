import { redirect } from '@sveltejs/kit';
import { resolve } from '$app/paths';
import type { PageLoad } from './$types';
import { loadRegistrationOpen } from '$lib/utils/registration';

/**
 * Signed-in users skip the marketing page. The root layout deliberately doesn't auth-check public
 * routes, so probe the session here — previously this redirect lived in onMount, which flashed the
 * landing page first and missed fresh page loads entirely (auth.current is only populated client-side).
 */
export const load: PageLoad = async ({ fetch }) => {
	let signedIn = false;
	try {
		const res = await fetch('/api/v1/auth/me');
		signedIn = res.ok;
	} catch {
		// API unreachable — show the landing page rather than failing the route.
	}

	if (signedIn) {
		redirect(307, resolve('/dashboard'));
	}

	return { registrationOpen: await loadRegistrationOpen(fetch) };
};
