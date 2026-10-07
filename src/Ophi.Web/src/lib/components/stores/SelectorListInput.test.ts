import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import SelectorListInput from './SelectorListInput.svelte';

describe('SelectorListInput', () => {
	const defaultProps = {
		label: 'Test Selectors',
		values: ['selector1', 'selector2'],
		onChange: vi.fn()
	};

	describe('rendering', () => {
		it('should render label', () => {
			render(SelectorListInput, { props: defaultProps });
			expect(screen.getByText('Test Selectors')).toBeInTheDocument();
		});

		it('should render all input values', () => {
			render(SelectorListInput, { props: defaultProps });
			const inputs = screen.getAllByRole('textbox') as HTMLInputElement[];
			expect(inputs).toHaveLength(2);
			expect(inputs[0].value).toBe('selector1');
			expect(inputs[1].value).toBe('selector2');
		});

		it('should render Add button', () => {
			render(SelectorListInput, { props: defaultProps });
			expect(screen.getByRole('button', { name: /Add/i })).toBeInTheDocument();
		});

		it('should render remove buttons for each item', () => {
			const { container } = render(SelectorListInput, { props: defaultProps });
			const removeButtons = container.querySelectorAll('button[title="Remove"]');
			expect(removeButtons).toHaveLength(2);
		});

		it('should show required asterisk when required is true', () => {
			render(SelectorListInput, { props: { ...defaultProps, required: true } });
			expect(screen.getByText('*')).toBeInTheDocument();
		});

		it('should not show required asterisk when required is false', () => {
			render(SelectorListInput, { props: { ...defaultProps, required: false } });
			expect(screen.queryByText('*')).not.toBeInTheDocument();
		});

		it('should render help text when provided', () => {
			render(SelectorListInput, { props: { ...defaultProps, helpText: 'This is help text' } });
			expect(screen.getByText('This is help text')).toBeInTheDocument();
		});

		it('should render placeholder on inputs', () => {
			render(SelectorListInput, { props: { ...defaultProps, placeholder: 'Enter selector' } });
			const inputs = screen.getAllByPlaceholderText('Enter selector');
			expect(inputs).toHaveLength(2);
		});

		it('should show empty message when values is empty', () => {
			render(SelectorListInput, { props: { ...defaultProps, values: [] } });
			expect(screen.getByText('No items added')).toBeInTheDocument();
		});
	});

	describe('adding items', () => {
		it('should call onChange with new item when Add is clicked', async () => {
			const onChange = vi.fn();
			render(SelectorListInput, { props: { ...defaultProps, onChange } });

			await fireEvent.click(screen.getByRole('button', { name: /Add/i }));

			expect(onChange).toHaveBeenCalledWith(['selector1', 'selector2', '']);
		});
	});

	describe('removing items', () => {
		it('should call onChange without the item when remove is clicked', async () => {
			const onChange = vi.fn();
			const { container } = render(SelectorListInput, { props: { ...defaultProps, onChange } });

			const removeButtons = container.querySelectorAll('button[title="Remove"]');
			await fireEvent.click(removeButtons[0]);

			expect(onChange).toHaveBeenCalledWith(['selector2']);
		});

		it('should not allow removing last item when required is true', async () => {
			const onChange = vi.fn();
			const { container } = render(SelectorListInput, {
				props: { ...defaultProps, values: ['only-item'], required: true, onChange }
			});

			const removeButton = container.querySelector('button[title="Remove"]');
			expect(removeButton).toBeDisabled();

			await fireEvent.click(removeButton!);
			expect(onChange).not.toHaveBeenCalled();
		});

		it('should allow removing last item when required is false', async () => {
			const onChange = vi.fn();
			const { container } = render(SelectorListInput, {
				props: { ...defaultProps, values: ['only-item'], required: false, onChange }
			});

			const removeButton = container.querySelector('button[title="Remove"]');
			expect(removeButton).not.toBeDisabled();

			await fireEvent.click(removeButton!);
			expect(onChange).toHaveBeenCalledWith([]);
		});
	});

	describe('updating items', () => {
		it('should call onChange with updated value when input changes', async () => {
			const onChange = vi.fn();
			render(SelectorListInput, { props: { ...defaultProps, onChange } });

			const inputs = screen.getAllByRole('textbox');
			await fireEvent.input(inputs[0], { target: { value: 'updated-selector' } });

			expect(onChange).toHaveBeenCalledWith(['updated-selector', 'selector2']);
		});

		it('should call onChange with updated value for second input', async () => {
			const onChange = vi.fn();
			render(SelectorListInput, { props: { ...defaultProps, onChange } });

			const inputs = screen.getAllByRole('textbox');
			await fireEvent.input(inputs[1], { target: { value: 'new-value' } });

			expect(onChange).toHaveBeenCalledWith(['selector1', 'new-value']);
		});
	});
});
