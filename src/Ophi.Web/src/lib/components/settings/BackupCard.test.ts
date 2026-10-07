import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import BackupCard from './BackupCard.svelte';
import { api } from '$lib/api/client';
import { downloadBlob } from '$lib/utils/download';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return { ...actual, api: { exportBackup: vi.fn(), importBackup: vi.fn() } };
});
vi.mock('$lib/utils/download', () => ({ downloadBlob: vi.fn() }));

const result = {
	settingsRestored: true,
	tagsAdded: 2,
	comparisonGroupsAdded: 1,
	storesAdded: 0,
	storesKept: 1,
	productsAdded: 5,
	productsSkipped: 2,
	pricePointsAdded: 300,
	alertsAdded: 3,
	alertsPaused: 1,
	warnings: ['1 alert(s) were imported paused: the account reached its limit of 100 active alerts.']
};

beforeEach(() => vi.clearAllMocks());

describe('BackupCard', () => {
	it('should download the backup under the server-provided file name', async () => {
		vi.mocked(api.exportBackup).mockResolvedValue(
			new Response('{}', {
				headers: { 'Content-Disposition': 'attachment; filename=ophi-backup-2026-09-25.json' }
			})
		);
		render(BackupCard);
		await fireEvent.click(screen.getByRole('button', { name: /Download backup/ }));
		await waitFor(() => expect(downloadBlob).toHaveBeenCalledOnce());
		// Response.blob() returns Node's native Blob, not jsdom's global — so no expect.any(Blob).
		const [blob, name] = vi.mocked(downloadBlob).mock.calls[0];
		expect(name).toBe('ophi-backup-2026-09-25.json');
		expect(await blob.text()).toBe('{}');
	});

	it('should restore a file and summarise what was added, skipped and paused', async () => {
		vi.mocked(api.importBackup).mockResolvedValue(result);
		render(BackupCard);
		const file = new File(['{}'], 'ophi-backup.json', { type: 'application/json' });
		await fireEvent.change(screen.getByLabelText('Restore from a backup file'), {
			target: { files: [file] }
		});

		expect(api.importBackup).toHaveBeenCalledWith(file);
		const summary = await screen.findByRole('status');
		expect(summary).toHaveTextContent('5 products added');
		expect(summary).toHaveTextContent('2 already tracked');
		expect(summary).toHaveTextContent('imported paused');
	});

	it('should announce a failed restore', async () => {
		vi.mocked(api.importBackup).mockRejectedValue(new Error('Not an Ophi backup this server can read'));
		render(BackupCard);
		await fireEvent.change(screen.getByLabelText('Restore from a backup file'), {
			target: { files: [new File(['x'], 'bad.json')] }
		});
		expect(await screen.findByRole('alert')).toHaveTextContent('Not an Ophi backup');
	});
});
