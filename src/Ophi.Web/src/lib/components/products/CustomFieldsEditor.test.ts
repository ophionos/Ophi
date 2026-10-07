import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import CustomFieldsEditor from './CustomFieldsEditor.svelte';
import type { CustomField } from '$lib/api/client';

describe('CustomFieldsEditor', () => {
	let onChange: (fields: CustomField[]) => void;

	beforeEach(() => {
		onChange = vi.fn();
	});

	it('should render existing fields', () => {
		const fields: CustomField[] = [
			{ name: 'Color', value: 'Red' },
			{ name: 'Size', value: 'Large' }
		];

		render(CustomFieldsEditor, { props: { fields, onChange } });

		const inputs = screen.getAllByRole('textbox');
		// 2 fields x 2 inputs each = 4 textboxes
		expect(inputs).toHaveLength(4);
		expect(inputs[0]).toHaveValue('Color');
		expect(inputs[1]).toHaveValue('Red');
		expect(inputs[2]).toHaveValue('Size');
		expect(inputs[3]).toHaveValue('Large');
	});

	it('should add a new field when Add Field is clicked', async () => {
		const fields: CustomField[] = [{ name: 'Color', value: 'Red' }];

		render(CustomFieldsEditor, { props: { fields, onChange } });

		await fireEvent.click(screen.getByText('Add Field'));

		expect(onChange).toHaveBeenCalledWith([
			{ name: 'Color', value: 'Red' },
			{ name: '', value: '' }
		]);
	});

	it('should remove a field when remove button is clicked', async () => {
		const fields: CustomField[] = [
			{ name: 'Color', value: 'Red' },
			{ name: 'Size', value: 'Large' }
		];

		render(CustomFieldsEditor, { props: { fields, onChange } });

		const removeButtons = screen.getAllByLabelText('Remove field');
		await fireEvent.click(removeButtons[0]);

		expect(onChange).toHaveBeenCalledWith([{ name: 'Size', value: 'Large' }]);
	});

	it('should call onChange when field name changes', async () => {
		const fields: CustomField[] = [{ name: 'Color', value: 'Red' }];

		render(CustomFieldsEditor, { props: { fields, onChange } });

		const inputs = screen.getAllByRole('textbox');
		await fireEvent.input(inputs[0], { target: { value: 'Brand' } });

		expect(onChange).toHaveBeenCalledWith([{ name: 'Brand', value: 'Red' }]);
	});

	it('should call onChange when field value changes', async () => {
		const fields: CustomField[] = [{ name: 'Color', value: 'Red' }];

		render(CustomFieldsEditor, { props: { fields, onChange } });

		const inputs = screen.getAllByRole('textbox');
		await fireEvent.input(inputs[1], { target: { value: 'Blue' } });

		expect(onChange).toHaveBeenCalledWith([{ name: 'Color', value: 'Blue' }]);
	});

	it('should show duplicate error for duplicate field names', async () => {
		const fields: CustomField[] = [
			{ name: 'Color', value: 'Red' },
			{ name: '', value: '' }
		];

		render(CustomFieldsEditor, { props: { fields, onChange } });

		const inputs = screen.getAllByRole('textbox');
		// Type "Color" into the second field's name input (index 2)
		await fireEvent.input(inputs[2], { target: { value: 'Color' } });

		expect(screen.getByRole('alert')).toHaveTextContent('Duplicate field names are not allowed');
	});

	it('should render Add Field button', () => {
		render(CustomFieldsEditor, { props: { fields: [], onChange } });

		expect(screen.getByText('Add Field')).toBeInTheDocument();
	});

	it('should render no inputs when fields array is empty', () => {
		render(CustomFieldsEditor, { props: { fields: [], onChange } });

		expect(screen.queryAllByRole('textbox')).toHaveLength(0);
	});

	it('should not show duplicate error for empty field names', async () => {
		const fields: CustomField[] = [
			{ name: '', value: 'Red' },
			{ name: '', value: 'Blue' }
		];

		render(CustomFieldsEditor, { props: { fields, onChange } });

		// Trigger validation by changing a name field
		const inputs = screen.getAllByRole('textbox');
		await fireEvent.input(inputs[0], { target: { value: '' } });

		expect(screen.queryByText('Duplicate field names are not allowed')).not.toBeInTheDocument();
	});
});
