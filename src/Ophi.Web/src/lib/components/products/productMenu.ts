import { Bell, ExternalLink, History, Pause, Play, Trash2 } from 'lucide-svelte';
import type { Product, ProductStatus } from '$lib/api/client';
import type { ActionMenuItem } from '$lib/components/shared/ActionMenu.svelte';

export interface ProductMenuHandlers {
	onViewHistory?: (product: Product) => void;
	onStatusChange?: (id: string, status: ProductStatus) => void;
	/** Opens the view's own quick-alert form; omitted where the view has none. */
	onSetAlert?: (product: Product) => void;
	onDelete?: (id: string) => void;
}

/** The one row menu of the watchlist: table rows, phone rows and gallery cards offer the same items. */
export function productMenuItems(
	product: Product,
	handlers: ProductMenuHandlers
): ActionMenuItem[] {
	const isPending = product.status === 'pending';
	const items: ActionMenuItem[] = [
		{
			label: 'Open in store',
			icon: ExternalLink,
			href: product.affiliateUrl ?? product.bestUrl ?? product.url,
			external: true
		}
	];
	const { onViewHistory, onStatusChange, onSetAlert, onDelete } = handlers;
	if (!isPending && onViewHistory)
		items.push({ label: 'Price history', icon: History, onSelect: () => onViewHistory(product) });
	if ((product.status === 'active' || product.status === 'paused') && onStatusChange) {
		const pausing = product.status === 'active';
		items.push({
			label: pausing ? 'Pause tracking' : 'Resume tracking',
			icon: pausing ? Pause : Play,
			onSelect: () => onStatusChange(product.id, pausing ? 'paused' : 'active')
		});
	}
	if (!isPending && onSetAlert)
		items.push({ label: 'Set price alert', icon: Bell, onSelect: () => onSetAlert(product) });
	if (onDelete)
		items.push({
			label: 'Delete',
			icon: Trash2,
			danger: true,
			onSelect: () => onDelete(product.id)
		});
	return items;
}
