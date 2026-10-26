import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import BasketFormModal from './BasketFormModal.svelte';

describe('BasketFormModal', () => {
	it('should call onSave with the trimmed name and description when submitted', async () => {
		const onSave = vi.fn().mockResolvedValue(undefined);
		render(BasketFormModal, { props: { open: true, onClose: vi.fn(), onSave } });

		await fireEvent.input(screen.getByLabelText(/Name/), { target: { value: ' Trip ' } });
		await fireEvent.input(screen.getByLabelText('Description'), { target: { value: 'Gear' } });
		await fireEvent.click(screen.getByRole('button', { name: 'Create' }));

		await waitFor(() => expect(onSave).toHaveBeenCalledWith('Trip', 'Gear'));
	});

	it('should show an error and not save when the name is empty', async () => {
		const onSave = vi.fn();
		render(BasketFormModal, { props: { open: true, onClose: vi.fn(), onSave } });

		await fireEvent.click(screen.getByRole('button', { name: 'Create' }));

		expect(await screen.findByText('Name is required')).toBeInTheDocument();
		expect(onSave).not.toHaveBeenCalled();
	});

	it('should show the server message under the field when onSave rejects', async () => {
		const onSave = vi.fn().mockRejectedValue(new Error('Name must be at most 200 characters'));
		render(BasketFormModal, { props: { open: true, onClose: vi.fn(), onSave } });

		await fireEvent.input(screen.getByLabelText(/Name/), { target: { value: 'x' } });
		await fireEvent.click(screen.getByRole('button', { name: 'Create' }));

		expect(await screen.findByText('Name must be at most 200 characters')).toBeInTheDocument();
	});

	it('should prefill the fields when renaming a basket', () => {
		render(BasketFormModal, {
			props: {
				open: true,
				basket: { name: 'Trip', description: 'Gear' },
				onClose: vi.fn(),
				onSave: vi.fn()
			}
		});

		expect(screen.getByLabelText(/Name/)).toHaveValue('Trip');
		expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument();
	});
});
