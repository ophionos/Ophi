/**
 * Arrow-key navigation for dropdown menus. Attach to the wrapper that contains both the trigger
 * button and the open panel: ArrowUp/ArrowDown cycle focus through the focusable items (trigger
 * included), Home/End jump to the ends. Open/close and Escape handling stay with the caller —
 * this action only moves focus.
 */
export function menuKeyNav(node: HTMLElement) {
	function items(): HTMLElement[] {
		return [...node.querySelectorAll<HTMLElement>('a[href], button:not([disabled])')];
	}

	function handleKeydown(e: KeyboardEvent) {
		const list = items();
		if (list.length === 0) return;

		const current = list.indexOf(document.activeElement as HTMLElement);
		let next = -1;
		switch (e.key) {
			case 'ArrowDown':
				next = current < list.length - 1 ? current + 1 : 0;
				break;
			case 'ArrowUp':
				next = current > 0 ? current - 1 : list.length - 1;
				break;
			case 'Home':
				next = 0;
				break;
			case 'End':
				next = list.length - 1;
				break;
		}

		if (next >= 0) {
			e.preventDefault();
			list[next].focus();
		}
	}

	node.addEventListener('keydown', handleKeydown);
	return {
		destroy() {
			node.removeEventListener('keydown', handleKeydown);
		}
	};
}
