import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import WebhookModal from './WebhookModal.svelte';

describe('WebhookModal', () => {
	it('should offer the basket alert event when the modal is open', () => {
		render(WebhookModal, { props: { isOpen: true, onClose: vi.fn(), onSave: vi.fn() } });

		expect(screen.getByRole('checkbox', { name: /Basket alert fired/ })).toBeInTheDocument();
	});
});
