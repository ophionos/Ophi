// One mapping from a price change to its colour, so every surface reads a drop the same way.
// The tokens carry their own dark-mode values (app.css), so callers add no `dark:` pair.

type Change = number | null | undefined;

export function priceChangeTextClass(change: Change): string {
	if (!change) return 'text-price-unchanged';
	return change < 0 ? 'text-price-drop' : 'text-price-increase';
}

export function priceChangeChipClass(change: Change): string {
	if (!change) return 'bg-price-unchanged-subtle text-price-unchanged';
	return change < 0 ? 'bg-price-drop-subtle text-price-drop' : 'bg-price-increase-subtle text-price-increase';
}
