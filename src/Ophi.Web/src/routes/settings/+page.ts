import { redirect } from '@sveltejs/kit';
import { resolve } from '$app/paths';
import type { PageLoad } from './$types';

// /settings has no content of its own — land on the first section.
export const load: PageLoad = () => {
	redirect(307, resolve('/settings/scraping'));
};
