import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import StoreCard from './StoreCard.svelte';
import type { Store } from '$lib/api/client';

describe('StoreCard', () => {
	const mockStore: Store = {
		id: 'store-1',
		storeId: 'test-store',
		name: 'Test Store',
		domainPatterns: ['example.com', 'example.co.uk'],
		selectors: {
			priceSelectors: ['.price', '#product-price'],
			nameSelectors: ['.product-name'],
			imageSelectors: ['.product-image']
		},
		isBuiltIn: false,
		priceLocale: 'en-US',
		requiresJavaScript: false,
		createdAt: '2024-01-15T10:00:00Z'
	};

	const builtInStore: Store = {
		storeId: 'amazon',
		name: 'Amazon',
		domainPatterns: ['amazon.com', 'amazon.co.uk'],
		selectors: {
			priceSelectors: ['#priceblock_ourprice'],
			nameSelectors: ['#productTitle'],
			imageSelectors: ['#landingImage']
		},
		isBuiltIn: true,
		priceLocale: 'en-US',
		requiresJavaScript: false
	};

	const autoCreatedStore: Store = {
		id: 'store-auto',
		storeId: 'auto-store',
		name: 'Auto Store',
		domainPatterns: ['autoshop.com'],
		selectors: {
			priceSelectors: ['.price'],
			nameSelectors: ['.name'],
			imageSelectors: ['.img']
		},
		isBuiltIn: false,
		isAutoCreated: true,
		priceLocale: 'en-US',
		requiresJavaScript: false,
		createdAt: '2024-02-01T10:00:00Z'
	};

	describe('rendering', () => {
		it('should render store name', () => {
			render(StoreCard, { props: { store: mockStore } });
			expect(screen.getByText('Test Store')).toBeInTheDocument();
		});

		it('should render store ID', () => {
			render(StoreCard, { props: { store: mockStore } });
			expect(screen.getByText('test-store')).toBeInTheDocument();
		});

		it('should render domain patterns', () => {
			render(StoreCard, { props: { store: mockStore } });
			expect(screen.getByText('example.com')).toBeInTheDocument();
			expect(screen.getByText('example.co.uk')).toBeInTheDocument();
		});

		it('should render selector counts', () => {
			render(StoreCard, { props: { store: mockStore } });
			expect(screen.getByText('2 price, 1 name, 1 image selectors')).toBeInTheDocument();
		});
	});

	describe('store type badges', () => {
		it('should show Custom badge for custom stores', () => {
			render(StoreCard, { props: { store: mockStore } });
			expect(screen.getByText('Custom')).toBeInTheDocument();
			expect(screen.queryByText('Built-in')).not.toBeInTheDocument();
		});

		it('should show Built-in badge for built-in stores', () => {
			render(StoreCard, { props: { store: builtInStore } });
			expect(screen.getByText('Built-in')).toBeInTheDocument();
			expect(screen.queryByText('Custom')).not.toBeInTheDocument();
		});
	});

	describe('auto-created badge', () => {
		it('should show Auto-detected badge when isAutoCreated is true', () => {
			render(StoreCard, { props: { store: autoCreatedStore } });
			expect(screen.getByText('Auto-detected')).toBeInTheDocument();
		});

		it('should show Custom badge alongside Auto-detected badge', () => {
			render(StoreCard, { props: { store: autoCreatedStore } });
			expect(screen.getByText('Custom')).toBeInTheDocument();
			expect(screen.getByText('Auto-detected')).toBeInTheDocument();
		});

		it('should not show Auto-detected badge when isAutoCreated is false', () => {
			render(StoreCard, { props: { store: mockStore } });
			expect(screen.queryByText('Auto-detected')).not.toBeInTheDocument();
		});

		it('should not show Auto-detected badge when isAutoCreated is undefined', () => {
			const storeWithoutFlag = { ...mockStore, isAutoCreated: undefined };
			render(StoreCard, { props: { store: storeWithoutFlag } });
			expect(screen.queryByText('Auto-detected')).not.toBeInTheDocument();
		});

		it('should not show Auto-detected badge for built-in stores', () => {
			const builtInAutoCreated = { ...builtInStore, isAutoCreated: true };
			render(StoreCard, { props: { store: builtInAutoCreated } });
			expect(screen.queryByText('Auto-detected')).not.toBeInTheDocument();
		});

		it('should show edit and delete buttons for auto-created stores', () => {
			const onEdit = vi.fn();
			const onDelete = vi.fn();
			const { container } = render(StoreCard, { props: { store: autoCreatedStore, onEdit, onDelete } });
			const editButton = container.querySelector('button[title="Edit"]');
			const deleteButton = container.querySelector('button[title="Delete"]');
			expect(editButton).toBeInTheDocument();
			expect(deleteButton).toBeInTheDocument();
		});
	});

	describe('created date', () => {
		it('should render created date for custom stores', () => {
			render(StoreCard, { props: { store: mockStore } });
			expect(screen.getByText(/Created/)).toBeInTheDocument();
		});

		it('should not render created date for built-in stores', () => {
			render(StoreCard, { props: { store: builtInStore } });
			expect(screen.queryByText(/Created/)).not.toBeInTheDocument();
		});

		it('should not render created date when not provided', () => {
			const storeWithoutDate = { ...mockStore, createdAt: undefined };
			render(StoreCard, { props: { store: storeWithoutDate } });
			expect(screen.queryByText(/Created/)).not.toBeInTheDocument();
		});
	});

	describe('action buttons for custom stores', () => {
		it('should render edit button when onEdit is provided', () => {
			const onEdit = vi.fn();
			const { container } = render(StoreCard, { props: { store: mockStore, onEdit } });
			const editButton = container.querySelector('button[title="Edit"]');
			expect(editButton).toBeInTheDocument();
		});

		it('should render delete button when onDelete is provided', () => {
			const onDelete = vi.fn();
			const { container } = render(StoreCard, { props: { store: mockStore, onDelete } });
			const deleteButton = container.querySelector('button[title="Delete"]');
			expect(deleteButton).toBeInTheDocument();
		});

		it('should call onEdit with store when edit button is clicked', async () => {
			const onEdit = vi.fn();
			const { container } = render(StoreCard, { props: { store: mockStore, onEdit } });
			const editButton = container.querySelector('button[title="Edit"]');
			await fireEvent.click(editButton!);
			expect(onEdit).toHaveBeenCalledWith(mockStore);
		});

		it('should call onDelete with store when delete button is clicked', async () => {
			const onDelete = vi.fn();
			const { container } = render(StoreCard, { props: { store: mockStore, onDelete } });
			const deleteButton = container.querySelector('button[title="Delete"]');
			await fireEvent.click(deleteButton!);
			expect(onDelete).toHaveBeenCalledWith(mockStore);
		});

		it('should not render edit button when onEdit is not provided', () => {
			const { container } = render(StoreCard, { props: { store: mockStore } });
			const editButton = container.querySelector('button[title="Edit"]');
			expect(editButton).not.toBeInTheDocument();
		});

		it('should not render delete button when onDelete is not provided', () => {
			const { container } = render(StoreCard, { props: { store: mockStore } });
			const deleteButton = container.querySelector('button[title="Delete"]');
			expect(deleteButton).not.toBeInTheDocument();
		});
	});

	describe('test button', () => {
		it('should render test button when onTest is provided', () => {
			const onTest = vi.fn();
			const { container } = render(StoreCard, { props: { store: mockStore, onTest } });
			const testButton = container.querySelector('button[title="Test"]');
			expect(testButton).toBeInTheDocument();
		});

		it('should call onTest with store when test button is clicked', async () => {
			const onTest = vi.fn();
			const { container } = render(StoreCard, { props: { store: mockStore, onTest } });
			const testButton = container.querySelector('button[title="Test"]');
			await fireEvent.click(testButton!);
			expect(onTest).toHaveBeenCalledWith(mockStore);
		});

		it('should render test button for built-in stores when onTest is provided', () => {
			const onTest = vi.fn();
			const { container } = render(StoreCard, { props: { store: builtInStore, onTest } });
			const testButton = container.querySelector('button[title="Test"]');
			expect(testButton).toBeInTheDocument();
		});

		it('should not render test button when onTest is not provided', () => {
			const { container } = render(StoreCard, { props: { store: mockStore } });
			const testButton = container.querySelector('button[title="Test"]');
			expect(testButton).not.toBeInTheDocument();
		});
	});

	describe('export button', () => {
		it('should render export button for custom stores when onExport is provided', () => {
			const onExport = vi.fn();
			const { container } = render(StoreCard, { props: { store: mockStore, onExport } });
			const exportButton = container.querySelector('button[title="Export"]');
			expect(exportButton).toBeInTheDocument();
		});

		it('should not render export button for built-in stores', () => {
			const onExport = vi.fn();
			const { container } = render(StoreCard, { props: { store: builtInStore, onExport } });
			const exportButton = container.querySelector('button[title="Export"]');
			expect(exportButton).not.toBeInTheDocument();
		});

		it('should call onExport with store when export button is clicked', async () => {
			const onExport = vi.fn();
			const { container } = render(StoreCard, { props: { store: mockStore, onExport } });
			const exportButton = container.querySelector('button[title="Export"]');
			await fireEvent.click(exportButton!);
			expect(onExport).toHaveBeenCalledWith(mockStore);
		});

		it('should not render export button when onExport is not provided', () => {
			const { container } = render(StoreCard, { props: { store: mockStore } });
			const exportButton = container.querySelector('button[title="Export"]');
			expect(exportButton).not.toBeInTheDocument();
		});
	});

	describe('action buttons for built-in stores', () => {
		it('should not render edit button for built-in stores', () => {
			const onEdit = vi.fn();
			const { container } = render(StoreCard, { props: { store: builtInStore, onEdit } });
			const editButton = container.querySelector('button[title="Edit"]');
			expect(editButton).not.toBeInTheDocument();
		});

		it('should not render delete button for built-in stores', () => {
			const onDelete = vi.fn();
			const { container } = render(StoreCard, { props: { store: builtInStore, onDelete } });
			const deleteButton = container.querySelector('button[title="Delete"]');
			expect(deleteButton).not.toBeInTheDocument();
		});
	});

	describe('multiple domain patterns', () => {
		it('should render all domain patterns', () => {
			const storeWithManyDomains: Store = {
				...mockStore,
				domainPatterns: ['store1.com', 'store2.com', 'store3.com', 'store4.com']
			};
			render(StoreCard, { props: { store: storeWithManyDomains } });

			expect(screen.getByText('store1.com')).toBeInTheDocument();
			expect(screen.getByText('store2.com')).toBeInTheDocument();
			expect(screen.getByText('store3.com')).toBeInTheDocument();
			expect(screen.getByText('store4.com')).toBeInTheDocument();
		});
	});

	describe('affiliate badge', () => {
		it('should show affiliate badge when affiliate config is set', () => {
			const affiliateStore: Store = {
				...mockStore,
				affiliateParamName: 'tag',
				affiliateTag: 'ophi-20'
			};
			render(StoreCard, { props: { store: affiliateStore } });
			expect(screen.getByTestId('affiliate-badge')).toBeInTheDocument();
			expect(screen.getByText('Affiliate')).toBeInTheDocument();
		});

		it('should not show affiliate badge when no affiliate config', () => {
			render(StoreCard, { props: { store: mockStore } });
			expect(screen.queryByTestId('affiliate-badge')).not.toBeInTheDocument();
		});

		it('should not show affiliate badge when only param name is set', () => {
			const partialStore: Store = {
				...mockStore,
				affiliateParamName: 'tag'
			};
			render(StoreCard, { props: { store: partialStore } });
			expect(screen.queryByTestId('affiliate-badge')).not.toBeInTheDocument();
		});
	});

	describe('varying selector counts', () => {
		it('should display correct counts for various selector configurations', () => {
			const storeWithVariedSelectors: Store = {
				...mockStore,
				selectors: {
					priceSelectors: ['.price-1', '.price-2', '.price-3'],
					nameSelectors: [],
					imageSelectors: ['.img-1', '.img-2']
				}
			};
			render(StoreCard, { props: { store: storeWithVariedSelectors } });
			expect(screen.getByText('3 price, 0 name, 2 image selectors')).toBeInTheDocument();
		});
	});
});
