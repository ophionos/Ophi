import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import ActionMenu from './ActionMenu.svelte';

const setup = () => {
	const onPause = vi.fn();
	const onDelete = vi.fn();
	render(ActionMenu, {
		props: {
			label: 'Actions for Desk Lamp',
			items: [
				{ label: 'Open in store', href: 'https://shop.example/lamp', external: true },
				{ label: 'Pause tracking', onSelect: onPause },
				{ label: 'Delete', onSelect: onDelete, danger: true }
			]
		}
	});
	return { onPause, onDelete, trigger: screen.getByRole('button', { name: 'Actions for Desk Lamp' }) };
};

describe('ActionMenu', () => {
	it('should open on click and focus the first item', async () => {
		const { trigger } = setup();
		expect(trigger).toHaveAttribute('aria-expanded', 'false');

		await fireEvent.click(trigger);

		expect(trigger).toHaveAttribute('aria-expanded', 'true');
		expect(screen.getByRole('menuitem', { name: 'Open in store' })).toHaveFocus();
		expect(screen.getByRole('menuitem', { name: 'Open in store' })).toHaveAttribute('target', '_blank');
	});

	it('should run the item action, close, and return focus to the trigger', async () => {
		const { trigger, onPause } = setup();
		await fireEvent.click(trigger);

		await fireEvent.click(screen.getByRole('menuitem', { name: 'Pause tracking' }));

		expect(onPause).toHaveBeenCalledOnce();
		expect(screen.queryByRole('menu')).not.toBeInTheDocument();
		expect(trigger).toHaveFocus();
	});

	it('should move with the arrow keys and close on Escape', async () => {
		const { trigger } = setup();
		await fireEvent.click(trigger);
		const menu = screen.getByRole('menu');

		await fireEvent.keyDown(menu, { key: 'ArrowDown' });
		expect(screen.getByRole('menuitem', { name: 'Pause tracking' })).toHaveFocus();
		await fireEvent.keyDown(menu, { key: 'ArrowUp' });
		await fireEvent.keyDown(menu, { key: 'ArrowUp' });
		expect(screen.getByRole('menuitem', { name: 'Delete' })).toHaveFocus();

		await fireEvent.keyDown(menu, { key: 'Escape' });
		expect(screen.queryByRole('menu')).not.toBeInTheDocument();
		expect(trigger).toHaveFocus();
	});

	it('should close on a click outside', async () => {
		const { trigger } = setup();
		await fireEvent.click(trigger);

		await fireEvent.pointerDown(document.body);

		expect(screen.queryByRole('menu')).not.toBeInTheDocument();
	});

	it('should open upwards when there is no room below the trigger', async () => {
		const { trigger } = setup();
		trigger.getBoundingClientRect = () =>
			({ top: window.innerHeight - 30, bottom: window.innerHeight - 10, right: 100, left: 60 }) as DOMRect;

		await fireEvent.click(trigger);

		expect(screen.getByRole('menu').style.bottom).not.toBe('');
		expect(screen.getByRole('menu').style.top).toBe('');
	});
});
