import type { PageLoad } from './$types';
import { loadRegistrationOpen } from '$lib/utils/registration';

export const load: PageLoad = async ({ fetch }) => ({
	registrationOpen: await loadRegistrationOpen(fetch)
});
