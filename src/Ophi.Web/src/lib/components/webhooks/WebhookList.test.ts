import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import WebhookList from './WebhookList.svelte';
import type { WebhookTarget } from '$lib/api/client';

const sampleHook: WebhookTarget = {
	id: 'w1',
	name: 'My ntfy hook',
	url: 'https://ntfy.sh/topic',
	events: ['alert_fired'],
	isEnabled: true,
	createdAt: '2026-01-01T00:00:00Z'
};

describe('WebhookList', () => {
	it('shows the empty hint when there are no webhooks', () => {
		render(WebhookList, {
			props: {
				webhooks: [],
				onAdd: vi.fn(),
				onEdit: vi.fn(),
				onTest: vi.fn(),
				onDelete: vi.fn()
			}
		});
		expect(screen.getByText(/No webhooks configured/)).toBeInTheDocument();
	});

	it('renders webhook name, url and event chips', () => {
		render(WebhookList, {
			props: {
				webhooks: [sampleHook],
				onAdd: vi.fn(),
				onEdit: vi.fn(),
				onTest: vi.fn(),
				onDelete: vi.fn()
			}
		});
		expect(screen.getByText('My ntfy hook')).toBeInTheDocument();
		expect(screen.getByText('https://ntfy.sh/topic')).toBeInTheDocument();
		expect(screen.getByText('alert_fired')).toBeInTheDocument();
	});

	it('marks disabled webhooks', () => {
		render(WebhookList, {
			props: {
				webhooks: [{ ...sampleHook, isEnabled: false }],
				onAdd: vi.fn(),
				onEdit: vi.fn(),
				onTest: vi.fn(),
				onDelete: vi.fn()
			}
		});
		expect(screen.getByText('disabled')).toBeInTheDocument();
	});

	it('invokes onAdd / onEdit / onTest / onDelete', async () => {
		const onAdd = vi.fn();
		const onEdit = vi.fn();
		const onTest = vi.fn();
		const onDelete = vi.fn();
		render(WebhookList, {
			props: { webhooks: [sampleHook], onAdd, onEdit, onTest, onDelete }
		});

		await fireEvent.click(screen.getByText('Add'));
		expect(onAdd).toHaveBeenCalled();

		await fireEvent.click(screen.getByTitle('Send test payload'));
		expect(onTest).toHaveBeenCalledWith('w1');

		await fireEvent.click(screen.getByTitle('Edit webhook'));
		expect(onEdit).toHaveBeenCalledWith(sampleHook);

		await fireEvent.click(screen.getByTitle('Delete webhook'));
		expect(onDelete).toHaveBeenCalledWith('w1');
	});
});
