import { render, screen, fireEvent } from '@testing-library/svelte';
import { describe, it, expect, vi, afterEach } from 'vitest';
import ApiKeyCreateModal from './ApiKeyCreateModal.svelte';

describe('ApiKeyCreateModal', () => {
	const defaultProps = {
		isOpen: true,
		onClose: vi.fn(),
		onSave: vi.fn().mockResolvedValue(undefined)
	};

	it('should render create form when open', () => {
		render(ApiKeyCreateModal, { props: defaultProps });

		expect(screen.getByText('Create API Key')).toBeInTheDocument();
		expect(screen.getByLabelText('Name')).toBeInTheDocument();
		expect(screen.getByText('Create Key')).toBeInTheDocument();
	});

	it('should not render when closed', () => {
		render(ApiKeyCreateModal, { props: { ...defaultProps, isOpen: false } });

		expect(screen.queryByText('Create API Key')).not.toBeInTheDocument();
	});

	it('should show error when name is empty on submit', async () => {
		render(ApiKeyCreateModal, { props: defaultProps });

		await fireEvent.click(screen.getByText('Create Key'));

		expect(screen.getByRole('alert')).toHaveTextContent('Name is required');
		expect(defaultProps.onSave).not.toHaveBeenCalled();
	});

	it('should show error when no scopes selected', async () => {
		render(ApiKeyCreateModal, { props: defaultProps });

		const nameInput = screen.getByLabelText('Name');
		await fireEvent.input(nameInput, { target: { value: 'Test Key' } });

		// Uncheck default read scope
		const readCheckbox = screen.getByRole('checkbox', { name: /read/ });
		await fireEvent.click(readCheckbox);

		await fireEvent.click(screen.getByText('Create Key'));

		expect(screen.getByText('Select at least one scope')).toBeInTheDocument();
	});

	it('should show created key view when createdKey is provided', () => {
		render(ApiKeyCreateModal, {
			props: { ...defaultProps, createdKey: 'ophi_test_key_abc123' }
		});

		expect(screen.getByText('API Key Created')).toBeInTheDocument();
		expect(screen.getByText('ophi_test_key_abc123')).toBeInTheDocument();
		expect(screen.getByText(/will not be shown again/)).toBeInTheDocument();
	});

	it('should show Done button after key is created', () => {
		render(ApiKeyCreateModal, {
			props: { ...defaultProps, createdKey: 'ophi_test_key_abc123' }
		});

		expect(screen.getByText('Done')).toBeInTheDocument();
	});

	it('should call onClose when Done is clicked', async () => {
		const onClose = vi.fn();
		render(ApiKeyCreateModal, {
			props: { ...defaultProps, onClose, createdKey: 'ophi_test_key_abc123' }
		});

		await fireEvent.click(screen.getByText('Done'));

		expect(onClose).toHaveBeenCalled();
	});

	describe('copy to clipboard', () => {
		afterEach(() => {
			vi.unstubAllGlobals();
		});

		it('should copy the key when the copy button is clicked', async () => {
			const writeText = vi.fn().mockResolvedValue(undefined);
			vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText } });
			render(ApiKeyCreateModal, {
				props: { ...defaultProps, createdKey: 'ophi_test_key_abc123' }
			});

			await fireEvent.click(screen.getByTitle('Copy to clipboard'));

			expect(writeText).toHaveBeenCalledWith('ophi_test_key_abc123');
			expect(screen.queryByText(/copy it manually/)).not.toBeInTheDocument();
		});

		// navigator.clipboard only exists in secure contexts (HTTPS or localhost);
		// over plain HTTP on a LAN address it is undefined.
		it('should show a manual-copy hint when the clipboard is unavailable', async () => {
			vi.stubGlobal('navigator', { ...navigator, clipboard: undefined });
			render(ApiKeyCreateModal, {
				props: { ...defaultProps, createdKey: 'ophi_test_key_abc123' }
			});

			await fireEvent.click(screen.getByTitle('Copy to clipboard'));

			expect(await screen.findByText(/copy it manually/)).toBeInTheDocument();
		});

		it('should show a manual-copy hint when the clipboard write is rejected', async () => {
			const writeText = vi.fn().mockRejectedValue(new Error('NotAllowedError'));
			vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText } });
			render(ApiKeyCreateModal, {
				props: { ...defaultProps, createdKey: 'ophi_test_key_abc123' }
			});

			await fireEvent.click(screen.getByTitle('Copy to clipboard'));

			expect(await screen.findByText(/copy it manually/)).toBeInTheDocument();
		});
	});
});
