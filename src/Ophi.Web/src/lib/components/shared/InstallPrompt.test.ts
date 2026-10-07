import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import InstallPrompt from './InstallPrompt.svelte';

describe('InstallPrompt', () => {
	beforeEach(() => {
		vi.restoreAllMocks();
		localStorage.clear();
	});

	it('should be hidden by default', () => {
		render(InstallPrompt);
		expect(screen.queryByTestId('install-prompt')).not.toBeInTheDocument();
	});

	it('should show when beforeinstallprompt fires', async () => {
		render(InstallPrompt);

		const event = new Event('beforeinstallprompt', { cancelable: true });
		window.dispatchEvent(event);

		expect(await screen.findByTestId('install-prompt')).toBeInTheDocument();
	});

	it('should call prompt() on install click', async () => {
		render(InstallPrompt);

		const mockPrompt = vi.fn();
		const event = Object.assign(new Event('beforeinstallprompt', { cancelable: true }), {
			prompt: mockPrompt,
			userChoice: Promise.resolve({ outcome: 'accepted' })
		});
		window.dispatchEvent(event);

		await screen.findByTestId('install-prompt');
		await fireEvent.click(screen.getByTestId('install-prompt-install'));

		expect(mockPrompt).toHaveBeenCalled();
	});

	it('should hide when dismissed', async () => {
		render(InstallPrompt);

		const event = new Event('beforeinstallprompt', { cancelable: true });
		window.dispatchEvent(event);

		await screen.findByTestId('install-prompt');
		await fireEvent.click(screen.getByTestId('install-prompt-dismiss'));

		expect(screen.queryByTestId('install-prompt')).not.toBeInTheDocument();
	});

	it('should stay hidden when a previous dismissal was stored', async () => {
		localStorage.setItem('ophi:install-prompt-dismissed', 'true');
		render(InstallPrompt);

		window.dispatchEvent(new Event('beforeinstallprompt', { cancelable: true }));

		await Promise.resolve();
		expect(screen.queryByTestId('install-prompt')).not.toBeInTheDocument();
	});

	it('should persist the dismissal so it does not return on the next visit', async () => {
		render(InstallPrompt);

		window.dispatchEvent(new Event('beforeinstallprompt', { cancelable: true }));
		await screen.findByTestId('install-prompt');
		await fireEvent.click(screen.getByTestId('install-prompt-dismiss'));

		expect(localStorage.getItem('ophi:install-prompt-dismissed')).toBe('true');
	});

	it('should have accessible label', async () => {
		render(InstallPrompt);

		const event = new Event('beforeinstallprompt', { cancelable: true });
		window.dispatchEvent(event);

		const banner = await screen.findByTestId('install-prompt');
		expect(banner).toHaveAttribute('aria-label', 'Install app prompt');
		expect(screen.getByLabelText('Dismiss install prompt')).toBeInTheDocument();
	});
});
