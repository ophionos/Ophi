import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import Modal from './Modal.svelte';
import { createRawSnippet } from 'svelte';

function makeBodySnippet(html: string) {
	return createRawSnippet(() => ({
		render: () => html
	}));
}

describe('Modal shell', () => {
	const baseProps = {
		isOpen: true,
		title: 'Test Modal',
		onClose: vi.fn(),
		children: makeBodySnippet('<div data-testid="modal-body">body content</div>')
	};

	it('should render title and body when open', () => {
		render(Modal, { props: baseProps });
		expect(screen.getByText('Test Modal')).toBeInTheDocument();
		expect(screen.getByTestId('modal-body')).toBeInTheDocument();
	});

	it('should not render anything when closed', () => {
		render(Modal, { props: { ...baseProps, isOpen: false } });
		expect(screen.queryByText('Test Modal')).not.toBeInTheDocument();
	});

	it('should call onClose when close button clicked', async () => {
		const onClose = vi.fn();
		render(Modal, { props: { ...baseProps, onClose } });
		await fireEvent.click(screen.getByRole('button', { name: 'Close' }));
		expect(onClose).toHaveBeenCalled();
	});

	it('should call onClose when backdrop clicked', async () => {
		const onClose = vi.fn();
		const { container } = render(Modal, { props: { ...baseProps, onClose } });
		const backdrop = container.querySelector('.fixed.inset-0.bg-black\\/50');
		expect(backdrop).toBeTruthy();
		await fireEvent.click(backdrop as Element);
		expect(onClose).toHaveBeenCalled();
	});

	it('should not call onClose on backdrop click when closeOnBackdrop is false', async () => {
		const onClose = vi.fn();
		const { container } = render(Modal, {
			props: { ...baseProps, onClose, closeOnBackdrop: false }
		});
		const backdrop = container.querySelector('.fixed.inset-0.bg-black\\/50');
		await fireEvent.click(backdrop as Element);
		expect(onClose).not.toHaveBeenCalled();
	});

	it('should call onClose on Escape key', async () => {
		const onClose = vi.fn();
		render(Modal, { props: { ...baseProps, onClose } });
		await fireEvent.keyDown(window, { key: 'Escape' });
		expect(onClose).toHaveBeenCalled();
	});

	it('should not call onClose on Escape when closeOnEscape is false', async () => {
		const onClose = vi.fn();
		render(Modal, { props: { ...baseProps, onClose, closeOnEscape: false } });
		await fireEvent.keyDown(window, { key: 'Escape' });
		expect(onClose).not.toHaveBeenCalled();
	});

	it('should apply size class', () => {
		const { container } = render(Modal, { props: { ...baseProps, size: 'lg' } });
		expect(container.querySelector('.max-w-lg')).toBeTruthy();
	});

	it('should default to md size', () => {
		const { container } = render(Modal, { props: baseProps });
		expect(container.querySelector('.max-w-md')).toBeTruthy();
	});

	it('should set aria-labelledby when titleId provided', () => {
		const { container } = render(Modal, {
			props: { ...baseProps, titleId: 'my-title' }
		});
		const dialog = container.querySelector('[role="dialog"]');
		expect(dialog?.getAttribute('aria-labelledby')).toBe('my-title');
		expect(container.querySelector('#my-title')).toBeTruthy();
	});

	it('should fall back to aria-label with title when titleId omitted', () => {
		const { container } = render(Modal, { props: baseProps });
		const dialog = container.querySelector('[role="dialog"]');
		expect(dialog?.getAttribute('aria-label')).toBe('Test Modal');
		expect(dialog?.hasAttribute('aria-labelledby')).toBe(false);
	});

	it('should center when position is center', () => {
		const { container } = render(Modal, { props: { ...baseProps, position: 'center' } });
		expect(container.querySelector('.items-center')).toBeTruthy();
	});

	it('should default to top position', () => {
		const { container } = render(Modal, { props: baseProps });
		expect(container.querySelector('.items-start')).toBeTruthy();
	});
});
