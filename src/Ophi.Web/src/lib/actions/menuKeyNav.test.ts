import { describe, it, expect, beforeEach } from 'vitest';
import { menuKeyNav } from './menuKeyNav';

function buildMenu() {
	// Mirrors the layout dropdowns: a wrapper containing the trigger button and a panel of links.
	const wrapper = document.createElement('div');
	const trigger = document.createElement('button');
	trigger.textContent = 'Open';
	const panel = document.createElement('div');
	const linkA = document.createElement('a');
	linkA.href = '/a';
	linkA.textContent = 'A';
	const linkB = document.createElement('a');
	linkB.href = '/b';
	linkB.textContent = 'B';
	const disabled = document.createElement('button');
	disabled.disabled = true;
	panel.append(linkA, linkB, disabled);
	wrapper.append(trigger, panel);
	document.body.appendChild(wrapper);
	return { wrapper, trigger, linkA, linkB };
}

function press(target: HTMLElement, key: string) {
	const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
	target.dispatchEvent(event);
	return event;
}

describe('menuKeyNav', () => {
	beforeEach(() => {
		document.body.innerHTML = '';
	});

	it('should move focus to the next item on ArrowDown', () => {
		const { wrapper, trigger, linkA } = buildMenu();
		menuKeyNav(wrapper);
		trigger.focus();

		const event = press(trigger, 'ArrowDown');

		expect(document.activeElement).toBe(linkA);
		expect(event.defaultPrevented).toBe(true);
	});

	it('should wrap from the last item back to the first on ArrowDown', () => {
		const { wrapper, trigger, linkB } = buildMenu();
		menuKeyNav(wrapper);
		linkB.focus();

		press(linkB, 'ArrowDown');

		expect(document.activeElement).toBe(trigger);
	});

	it('should move focus to the previous item on ArrowUp, wrapping at the top', () => {
		const { wrapper, trigger, linkB } = buildMenu();
		menuKeyNav(wrapper);
		trigger.focus();

		press(trigger, 'ArrowUp');

		expect(document.activeElement).toBe(linkB);
	});

	it('should jump to first and last with Home and End', () => {
		const { wrapper, trigger, linkA, linkB } = buildMenu();
		menuKeyNav(wrapper);
		linkA.focus();

		press(linkA, 'End');
		expect(document.activeElement).toBe(linkB);

		press(linkB, 'Home');
		expect(document.activeElement).toBe(trigger);
	});

	it('should skip disabled elements', () => {
		const { wrapper, linkB, trigger } = buildMenu();
		menuKeyNav(wrapper);
		linkB.focus();

		// The disabled button after linkB is skipped; focus wraps to the trigger.
		press(linkB, 'ArrowDown');
		expect(document.activeElement).toBe(trigger);
	});

	it('should ignore unrelated keys', () => {
		const { wrapper, trigger } = buildMenu();
		menuKeyNav(wrapper);
		trigger.focus();

		const event = press(trigger, 'Enter');

		expect(document.activeElement).toBe(trigger);
		expect(event.defaultPrevented).toBe(false);
	});

	it('should stop listening after destroy', () => {
		const { wrapper, trigger, linkA } = buildMenu();
		const action = menuKeyNav(wrapper);
		action.destroy();
		trigger.focus();

		press(trigger, 'ArrowDown');

		expect(document.activeElement).not.toBe(linkA);
	});
});
