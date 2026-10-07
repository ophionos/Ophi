import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import FocusTrapTest from './FocusTrapTest.svelte';
import { focusTrap } from './focusTrap';

describe('focusTrap', () => {
	beforeEach(() => {
		document.body.innerHTML = '';
	});

	it('should auto-focus the first focusable element on mount', () => {
		render(FocusTrapTest);
		const first = screen.getByTestId('btn-first');
		expect(document.activeElement).toBe(first);
	});

	it('should call focus on first element when Tab pressed at last', () => {
		render(FocusTrapTest);
		const first = screen.getByTestId('btn-first');
		const last = screen.getByTestId('btn-last');
		last.focus();

		const focusSpy = vi.spyOn(first, 'focus');
		const event = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });
		vi.spyOn(event, 'preventDefault');

		last.dispatchEvent(event);

		expect(event.preventDefault).toHaveBeenCalled();
		expect(focusSpy).toHaveBeenCalled();
	});

	it('should call focus on last element when Shift+Tab pressed at first', () => {
		render(FocusTrapTest);
		const first = screen.getByTestId('btn-first');
		const last = screen.getByTestId('btn-last');

		const focusSpy = vi.spyOn(last, 'focus');
		const event = new KeyboardEvent('keydown', {
			key: 'Tab',
			shiftKey: true,
			bubbles: true,
			cancelable: true
		});
		vi.spyOn(event, 'preventDefault');

		first.dispatchEvent(event);

		expect(event.preventDefault).toHaveBeenCalled();
		expect(focusSpy).toHaveBeenCalled();
	});

	it('should not prevent default when Tab pressed in the middle', () => {
		render(FocusTrapTest);
		const input = screen.getByTestId('input-middle');
		input.focus();

		const event = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });
		vi.spyOn(event, 'preventDefault');

		input.dispatchEvent(event);

		expect(event.preventDefault).not.toHaveBeenCalled();
	});

	it('should restore focus to previously focused element on destroy', () => {
		const outsideButton = document.createElement('button');
		outsideButton.textContent = 'Outside';
		document.body.appendChild(outsideButton);
		outsideButton.focus();
		expect(document.activeElement).toBe(outsideButton);

		const container = document.createElement('div');
		const btn = document.createElement('button');
		container.appendChild(btn);
		document.body.appendChild(container);

		const action = focusTrap(container);
		expect(document.activeElement).toBe(btn);

		action.destroy();
		expect(document.activeElement).toBe(outsideButton);
	});

	it('should focus the container when no focusable children exist', () => {
		const container = document.createElement('div');
		const span = document.createElement('span');
		span.textContent = 'Not focusable';
		container.appendChild(span);
		document.body.appendChild(container);

		const action = focusTrap(container);
		expect(container.getAttribute('tabindex')).toBe('-1');
		expect(document.activeElement).toBe(container);

		action.destroy();
	});
});
