import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import ApiKeyList from './ApiKeyList.svelte';
import type { ApiKeyInfo } from '$lib/api/client';

const sampleKey: ApiKeyInfo = {
	id: 'k1',
	name: 'Home Assistant',
	scopes: ['read'],
	createdAt: '2026-01-01T00:00:00Z',
	expiresAt: undefined,
	lastUsedAt: undefined
};

describe('ApiKeyList', () => {
	it('renders the empty hint when no keys exist', () => {
		render(ApiKeyList, {
			props: { keys: [], onCreate: vi.fn(), onDelete: vi.fn() }
		});
		expect(screen.getByText(/No API keys created/)).toBeInTheDocument();
	});

	it('renders key name and scope chip', () => {
		render(ApiKeyList, {
			props: { keys: [sampleKey], onCreate: vi.fn(), onDelete: vi.fn() }
		});
		expect(screen.getByText('Home Assistant')).toBeInTheDocument();
		expect(screen.getByText('read')).toBeInTheDocument();
	});

	it('marks expired keys', () => {
		const expired: ApiKeyInfo = {
			...sampleKey,
			expiresAt: '2020-01-01T00:00:00Z'
		};
		render(ApiKeyList, {
			props: { keys: [expired], onCreate: vi.fn(), onDelete: vi.fn() }
		});
		expect(screen.getByText('expired')).toBeInTheDocument();
	});

	it('invokes onCreate and onDelete', async () => {
		const onCreate = vi.fn();
		const onDelete = vi.fn();
		render(ApiKeyList, {
			props: { keys: [sampleKey], onCreate, onDelete }
		});

		await fireEvent.click(screen.getByText('Create'));
		expect(onCreate).toHaveBeenCalled();

		await fireEvent.click(screen.getByTitle('Revoke API key'));
		expect(onDelete).toHaveBeenCalledWith('k1');
	});
});
