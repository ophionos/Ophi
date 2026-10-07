import type { PageLoad } from './$types';
import { api } from '$lib/api/client';

export const load: PageLoad = async ({ fetch, parent }) => {
	const { user } = await parent();
	if (!user) {
		return { settings: null, webhooks: [], email: null };
	}

	const client = api.withFetch(fetch);
	const [settings, webhooks] = await Promise.all([
		client.getSettings(),
		client.getWebhookTargets()
	]);

	// Alerts go to the account address, so show which one that is rather than making the user
	// guess which of their addresses the instance knows about.
	return { settings, webhooks, email: user.email };
};
