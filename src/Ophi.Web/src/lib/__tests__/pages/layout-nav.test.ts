import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/svelte';
import Layout from '../../../routes/+layout.svelte';
import { page } from '$app/state';

vi.mock('$lib/stores/liveUpdates.svelte', () => ({
	liveUpdates: { connect: vi.fn(), disconnect: vi.fn(), subscribe: vi.fn(() => () => {}) }
}));

vi.mock('$lib/stores/fx.svelte', () => ({
	fx: { load: vi.fn(), reset: vi.fn() }
}));

const data = {
	user: { id: 'u1', email: 'user@example.com', name: 'User' },
	authChecked: true,
	isPublic: false
};

function renderAt(path: string) {
	page.url = new URL(`http://localhost${path}`) as unknown as typeof page.url;
	render(Layout, { props: { data } });
	return screen.getByRole('navigation', { name: 'Main navigation' });
}

describe('main navigation', () => {
	beforeEach(() => {
		vi.clearAllMocks();
	});

	it('should list Tags, Stores and Scrape Health in the Manage menu when it is opened', async () => {
		const nav = renderAt('/dashboard');

		await fireEvent.click(within(nav).getByRole('button', { name: 'Manage' }));

		const menu = nav.querySelector('[data-manage-menu]') as HTMLElement;
		const links = within(menu).getAllByRole('link');
		expect(links.map((l) => l.textContent?.trim())).toEqual(['Tags', 'Stores', 'Scrape Health']);
		expect(links.map((l) => l.getAttribute('href'))).toEqual(['/tags', '/stores', '/scrape-health']);
	});

	it('should link Settings straight to the settings page when on the desktop nav', () => {
		const nav = renderAt('/dashboard');

		const settings = within(nav).getByRole('link', { name: 'Settings' });
		expect(settings).toHaveAttribute('href', '/settings');
		expect(within(nav).queryByText('App Settings')).not.toBeInTheDocument();
	});

	it('should highlight Manage and not Settings when on a library page', () => {
		const nav = renderAt('/stores');

		expect(within(nav).getByRole('button', { name: 'Manage' }).className).toContain('bg-brand-light');
		expect(within(nav).getByRole('link', { name: 'Settings' })).not.toHaveAttribute('aria-current');
	});

	it('should mark Settings as the current page when on a settings tab', () => {
		const nav = renderAt('/settings/scraping');

		expect(within(nav).getByRole('link', { name: 'Settings' })).toHaveAttribute('aria-current', 'page');
		expect(within(nav).getByRole('button', { name: 'Manage' }).className).not.toContain('bg-brand-light');
	});

	it('should group the library pages under Manage and keep Settings separate when on the mobile menu', async () => {
		const nav = renderAt('/dashboard');

		await fireEvent.click(within(nav).getByRole('button', { name: 'Toggle menu' }));

		const mobile = nav.querySelector('[data-mobile-menu]') as HTMLElement;
		const group = within(mobile).getByRole('group', { name: 'Manage' });
		expect(within(group).getAllByRole('link').map((l) => l.textContent?.trim())).toEqual([
			'Tags',
			'Stores',
			'Scrape Health'
		]);
		expect(within(mobile).getByRole('link', { name: 'Settings' })).toHaveAttribute('href', '/settings');
		expect(within(mobile).queryByText('App Settings')).not.toBeInTheDocument();
	});
});
