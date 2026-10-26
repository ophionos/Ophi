import { describe, it, expect, vi, beforeEach } from 'vitest';
import { load } from '../../../routes/dashboard/+page';
import { api } from '$lib/api/client';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return {
		...actual,
		api: {
			withFetch: vi.fn(),
			getProducts: vi.fn(),
			getTags: vi.fn(),
			getChallengeAvailability: vi.fn()
		}
	};
});

function loadAt(url: string) {
	return load({
		fetch: vi.fn(),
		parent: async () => ({ user: { id: '1' } }),
		url: new URL(url)
	} as never);
}

beforeEach(() => {
	vi.clearAllMocks();
	vi.mocked(api.withFetch).mockReturnValue(api as never);
	vi.mocked(api.getProducts).mockResolvedValue({ items: [] } as never);
	vi.mocked(api.getTags).mockResolvedValue({ items: [] } as never);
	vi.mocked(api.getChallengeAvailability).mockResolvedValue({ available: false } as never);
});

describe('dashboard loader', () => {
	it('should send priceDrop when the URL has filter=drop', async () => {
		await loadAt('http://localhost/dashboard?filter=drop');

		expect(api.getProducts).toHaveBeenCalledWith(expect.objectContaining({ priceDrop: true, includeSparkline: true }));
	});

	it('should send the default sort when the URL has the retired favourites sort', async () => {
		await loadAt('http://localhost/dashboard?sortBy=favourites&sortDirection=asc');

		expect(api.getProducts).toHaveBeenCalledWith(expect.objectContaining({ sortBy: undefined, sortDirection: undefined, includeSparkline: true }));
	});

	it('should keep a column sort from the URL', async () => {
		await loadAt('http://localhost/dashboard?sortBy=lastchecked&sortDirection=asc');

		expect(api.getProducts).toHaveBeenCalledWith(expect.objectContaining({ sortBy: 'lastchecked', sortDirection: 'asc' }));
	});
});
