const FOCUSABLE_SELECTOR =
	'a[href], button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])';

export function focusTrap(node: HTMLElement): { destroy: () => void } {
	const previouslyFocused = document.activeElement as HTMLElement | null;

	function getFocusableElements(): HTMLElement[] {
		return Array.from(node.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR)).sort((a, b) =>
			a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1
		);
	}

	// Auto-focus first focusable element, or the node itself
	const focusable = getFocusableElements();
	if (focusable.length > 0) {
		focusable[0].focus();
	} else {
		node.setAttribute('tabindex', '-1');
		node.focus();
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key !== 'Tab') return;

		const elements = getFocusableElements();
		if (elements.length === 0) {
			e.preventDefault();
			return;
		}

		const first = elements[0];
		const last = elements[elements.length - 1];

		if (e.shiftKey && document.activeElement === first) {
			e.preventDefault();
			last.focus();
		} else if (!e.shiftKey && document.activeElement === last) {
			e.preventDefault();
			first.focus();
		}
	}

	node.addEventListener('keydown', handleKeydown);

	return {
		destroy() {
			node.removeEventListener('keydown', handleKeydown);
			previouslyFocused?.focus();
		}
	};
}
