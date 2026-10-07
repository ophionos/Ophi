import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import ComparisonsPage from '../../../routes/comparisons/+page.svelte';
import { auth } from '$lib/stores/auth.svelte';
import { comparisons } from '$lib/stores/comparisons.svelte';
import { api } from '$lib/api/client';
import { goto } from '$app/navigation';

// Mock the API client
vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return {
		...actual,
		api: {
			getComparisonGroups: vi.fn(),
			createComparisonGroup: vi.fn(),
			deleteComparisonGroup: vi.fn()
		}
	};
});

// Mock goto
vi.mock('$app/navigation', () => ({
	goto: vi.fn()
}));

const mockUser = { id: '1', email: 'test@example.com', name: 'Test User' };

const mockGroups = [
	{ id: 'g1', name: 'Laptops', productCount: 3 },
	{ id: 'g2', name: 'Phones', productCount: 2 }
];

function renderPage(groups: typeof mockGroups = []) {
	return render(ComparisonsPage, {
		props: { data: { user: null, authChecked: true, isPublic: false, groups } }
	});
}

describe('Comparisons Page', () => {
	beforeEach(() => {
		vi.clearAllMocks();
		auth.logout();
		comparisons.clear();
		auth.login(mockUser);
	});

	describe('rendering', () => {
		it('should render heading and subtitle', () => {
			renderPage();

			expect(screen.getByText('Comparison Groups')).toBeInTheDocument();
			expect(screen.getByText('Compare prices across similar products')).toBeInTheDocument();
		});

		it('should render Create Comparison button', () => {
			renderPage();
			expect(screen.getByText('Create Comparison')).toBeInTheDocument();
		});

		it('should render stats cards', () => {
			renderPage();
			expect(screen.getByText('Total Groups')).toBeInTheDocument();
			expect(screen.getByText('Total Products')).toBeInTheDocument();
		});
	});

	describe('empty state', () => {
		it('should show empty message when no comparison groups', () => {
			renderPage([]);
			expect(screen.getByText('No comparison groups yet')).toBeInTheDocument();
			expect(
				screen.getByRole('button', { name: 'Create your first comparison' })
			).toBeInTheDocument();
			expect(
				screen.getByText('Create a comparison group to compare prices across products')
			).toBeInTheDocument();
		});
	});

	describe('data display', () => {
		it('should render comparison cards from loader data', () => {
			renderPage(mockGroups);
			expect(screen.getByText('Laptops')).toBeInTheDocument();
			expect(screen.getByText('Phones')).toBeInTheDocument();
		});

		it('should show correct stats counts', () => {
			renderPage(mockGroups);
			// totalGroups = 2, totalProducts = 3 + 2 = 5
			expect(screen.getByText('2')).toBeInTheDocument();
			expect(screen.getByText('5')).toBeInTheDocument();
		});
	});

	describe('create comparison', () => {
		it('should open modal when Create Comparison is clicked', async () => {
			renderPage();

			const createButton = screen.getByText('Create Comparison');
			await fireEvent.click(createButton);

			await waitFor(() => {
				expect(screen.getByText(/Create Comparison Group/i)).toBeInTheDocument();
			});
		});

		it('should call API and update store on save', async () => {
			const newGroup = { id: 'g3', name: 'Tablets', productCount: 0 };
			vi.mocked(api.createComparisonGroup).mockResolvedValue(newGroup);
			renderPage();

			const createButton = screen.getByText('Create Comparison');
			await fireEvent.click(createButton);

			await waitFor(() => {
				expect(screen.getByLabelText(/Name/i)).toBeInTheDocument();
			});

			const nameInput = screen.getByLabelText(/Name/i);
			await fireEvent.input(nameInput, { target: { value: 'Tablets' } });

			const saveButton = screen.getByRole('button', { name: /^Create$/i });
			await fireEvent.click(saveButton);

			await waitFor(() => {
				expect(api.createComparisonGroup).toHaveBeenCalledWith('Tablets', undefined);
			});
		});
	});

	describe('delete comparison', () => {
		it('should show confirm modal when delete is triggered', async () => {
			renderPage(mockGroups);

			const deleteButtons = screen.getAllByTitle('Delete');
			await fireEvent.click(deleteButtons[0]);

			await waitFor(() => {
				expect(screen.getByText('Delete Comparison Group')).toBeInTheDocument();
				expect(
					screen.getByText(
						'Are you sure you want to delete this comparison group? This action cannot be undone.'
					)
				).toBeInTheDocument();
			});
		});

		it('should call API and remove from store on confirm', async () => {
			vi.mocked(api.deleteComparisonGroup).mockResolvedValue(undefined as never);
			renderPage(mockGroups);

			const cardDeleteButtons = screen.getAllByTitle('Delete');
			await fireEvent.click(cardDeleteButtons[0]);

			await waitFor(() => {
				expect(screen.getByText('Delete Comparison Group')).toBeInTheDocument();
			});

			const allDeleteButtons = screen.getAllByRole('button', { name: /^Delete$/i });
			const confirmButton = allDeleteButtons[allDeleteButtons.length - 1];
			await fireEvent.click(confirmButton);

			await waitFor(() => {
				expect(api.deleteComparisonGroup).toHaveBeenCalledWith('g1');
			});
		});
	});

	describe('navigation', () => {
		it('should navigate to comparison detail on view', async () => {
			renderPage(mockGroups);

			const viewButtons = screen.getAllByTitle('View');
			await fireEvent.click(viewButtons[0]);

			await waitFor(() => {
				expect(goto).toHaveBeenCalledWith('/comparisons/g1');
			});
		});
	});
});
