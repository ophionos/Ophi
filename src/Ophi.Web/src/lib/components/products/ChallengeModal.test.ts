import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import ChallengeModal from './ChallengeModal.svelte';
import { api } from '$lib/api/client';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return {
		...actual,
		api: {
			startChallenge: vi.fn(),
			getChallenge: vi.fn(),
			sendChallengeInput: vi.fn(),
			closeChallenge: vi.fn()
		}
	};
});

vi.mock('$lib/stores/toast.svelte', () => ({
	toast: { success: vi.fn(), error: vi.fn() }
}));

const mocked = vi.mocked(api);

function props(overrides: Partial<Record<string, unknown>> = {}) {
	return {
		isOpen: true,
		productId: 'p1',
		urlId: 'u1',
		onClose: vi.fn(),
		onSolved: vi.fn(),
		...overrides
	};
}

describe('ChallengeModal', () => {
	beforeEach(() => {
		vi.clearAllMocks();
		mocked.startChallenge.mockResolvedValue({ host: 'shop.example' });
		mocked.getChallenge.mockResolvedValue({ state: 'active', image: 'AAAA', host: 'shop.example' });
		mocked.sendChallengeInput.mockResolvedValue(undefined);
		mocked.closeChallenge.mockResolvedValue(undefined);
	});

	afterEach(() => {
		vi.useRealTimers();
	});

	it('should start a session and show the frame when opened', async () => {
		render(ChallengeModal, { props: props() });

		const frame = await screen.findByAltText('Store page');
		expect(mocked.startChallenge).toHaveBeenCalledWith('p1', 'u1');
		expect(frame).toHaveAttribute('src', 'data:image/jpeg;base64,AAAA');
	});

	it('should send the press and release scaled to the remote viewport when the frame is clicked', async () => {
		render(ChallengeModal, { props: props() });
		const frame = await screen.findByAltText('Store page');
		vi.spyOn(frame, 'getBoundingClientRect').mockReturnValue({
			left: 10, top: 20, width: 480, height: 270, right: 490, bottom: 290, x: 10, y: 20, toJSON: () => ({})
		});

		await fireEvent.pointerDown(frame, { clientX: 130, clientY: 155 });
		await fireEvent.pointerUp(frame, { clientX: 130, clientY: 155 });

		await waitFor(() => expect(mocked.sendChallengeInput).toHaveBeenCalledTimes(2));
		expect(mocked.sendChallengeInput).toHaveBeenNthCalledWith(1, { kind: 'down', x: 480, y: 540 });
		expect(mocked.sendChallengeInput).toHaveBeenNthCalledWith(2, { kind: 'up', x: 480, y: 540 });
	});

	it('should type the entered text and press a key when the buttons are used', async () => {
		render(ChallengeModal, { props: props() });
		await screen.findByAltText('Store page');

		await fireEvent.input(screen.getByLabelText('Text to type'), { target: { value: 'hello' } });
		await fireEvent.click(screen.getByRole('button', { name: 'Type' }));
		await fireEvent.click(screen.getByRole('button', { name: 'Enter' }));

		await waitFor(() => expect(mocked.sendChallengeInput).toHaveBeenCalledTimes(2));
		expect(mocked.sendChallengeInput).toHaveBeenNthCalledWith(1, { kind: 'text', text: 'hello' });
		expect(mocked.sendChallengeInput).toHaveBeenNthCalledWith(2, { kind: 'key', key: 'Enter' });
	});

	it('should call onSolved when the server reports the challenge solved', async () => {
		mocked.getChallenge.mockResolvedValue({ state: 'solved', host: 'shop.example' });
		const onSolved = vi.fn();
		render(ChallengeModal, { props: props({ onSolved }) });

		await waitFor(() => expect(onSolved).toHaveBeenCalledTimes(1));
	});

	it('should keep polling after a single failed poll', async () => {
		mocked.getChallenge
			.mockRejectedValueOnce(new Error('Server error'))
			.mockResolvedValue({ state: 'active', image: 'BBBB', host: 'shop.example' });
		render(ChallengeModal, { props: props() });

		const frame = await screen.findByAltText('Store page', {}, { timeout: 3000 });
		expect(frame).toHaveAttribute('src', 'data:image/jpeg;base64,BBBB');
		expect(screen.queryByRole('alert')).not.toBeInTheDocument();
	});

	it('should show an error after repeated failed polls', async () => {
		mocked.getChallenge.mockRejectedValue(new Error('The connection to the session was lost.'));
		render(ChallengeModal, { props: props() });

		expect(await screen.findByRole('alert', {}, { timeout: 4000 })).toHaveTextContent(
			'The connection to the session was lost.'
		);
	});

	it('should say the session ended when the server has no session', async () => {
		mocked.getChallenge.mockResolvedValue({ state: 'none' });
		render(ChallengeModal, { props: props() });

		expect(await screen.findByText(/session ended/i)).toBeInTheDocument();
	});

	it('should show the error when the session cannot start', async () => {
		mocked.startChallenge.mockRejectedValue(new Error('Challenge solving is not enabled on this server.'));
		render(ChallengeModal, { props: props() });

		expect(await screen.findByText('Challenge solving is not enabled on this server.')).toBeInTheDocument();
		expect(mocked.getChallenge).not.toHaveBeenCalled();
	});

	it('should close the session when Cancel is clicked', async () => {
		const onClose = vi.fn();
		render(ChallengeModal, { props: props({ onClose }) });
		await screen.findByAltText('Store page');

		await fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

		await waitFor(() => expect(onClose).toHaveBeenCalled());
		expect(mocked.closeChallenge).toHaveBeenCalled();
	});
});
