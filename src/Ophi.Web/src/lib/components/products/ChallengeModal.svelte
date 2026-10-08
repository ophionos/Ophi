<script lang="ts">
	import { untrack } from 'svelte';
	import { LoaderCircle } from 'lucide-svelte';
	import Modal from '$lib/components/shared/Modal.svelte';
	import { api, type ChallengeInput } from '$lib/api/client';
	import { toast } from '$lib/stores/toast.svelte';

	interface Props {
		isOpen: boolean;
		productId: string;
		urlId: string;
		onClose: () => void;
		onSolved: () => void;
	}

	let { isOpen, productId, urlId, onClose, onSolved }: Props = $props();

	// The remote page's viewport (ChallengeSession.ViewportWidth/Height on the server).
	const VIEWPORT_WIDTH = 1920;
	const VIEWPORT_HEIGHT = 1080;
	const POLL_MS = 500;
	// A poll can fail while the remote page navigates; give up only after several in a row.
	const MAX_POLL_FAILURES = 3;
	const KEYS = ['Enter', 'Tab', 'Backspace', 'Escape', 'Space'];

	let image = $state<string | null>(null);
	let error = $state<string | null>(null);
	let ended = $state(false);
	let text = $state('');
	let frame = $state<HTMLImageElement | null>(null);

	// Not reactive: polling bookkeeping only.
	let running = false;
	let timer: ReturnType<typeof setTimeout> | null = null;
	let failures = 0;
	// Inputs are sent one at a time so a release never overtakes its press.
	let inputs: Promise<unknown> = Promise.resolve();

	$effect(() => {
		if (!isOpen) return;
		const ids = [productId, urlId] as const;
		untrack(() => start(ids[0], ids[1]));
		return stop;
	});

	function message(err: unknown, fallback: string): string {
		return err instanceof Error && err.message ? err.message : fallback;
	}

	async function start(product: string, url: string) {
		image = null;
		error = null;
		ended = false;
		running = true;
		failures = 0;
		try {
			await api.startChallenge(product, url);
		} catch (err) {
			running = false;
			error = message(err, 'The store page could not be opened.');
			return;
		}
		await poll();
	}

	async function poll() {
		if (!running) return;
		try {
			const result = await api.getChallenge();
			if (!running) return;
			failures = 0;
			if (result.state === 'active') {
				image = result.image ?? image;
				timer = setTimeout(poll, POLL_MS);
			} else if (result.state === 'solved') {
				running = false;
				toast.success(`Challenge solved. Checking ${result.host ?? 'the store'} again.`);
				onSolved();
			} else {
				running = false;
				ended = true;
			}
		} catch (err) {
			if (!running) return;
			failures++;
			if (failures < MAX_POLL_FAILURES) {
				timer = setTimeout(poll, POLL_MS);
				return;
			}
			running = false;
			error = message(err, 'The connection to the session was lost.');
		}
	}

	function stop() {
		running = false;
		if (timer) {
			clearTimeout(timer);
			timer = null;
		}
	}

	async function cancel() {
		stop();
		try {
			await api.closeChallenge();
		} catch {
			// The session times out on the server anyway.
		}
		onClose();
	}

	function send(input: ChallengeInput) {
		inputs = inputs.then(() => api.sendChallengeInput(input)).catch(() => {
			// A lost input shows in the next frame; the user can repeat it.
		});
	}

	function toRemote(e: PointerEvent): { x: number; y: number } | null {
		if (!frame) return null;
		const rect = frame.getBoundingClientRect();
		if (rect.width === 0 || rect.height === 0) return null;
		const x = Math.round(((e.clientX - rect.left) / rect.width) * VIEWPORT_WIDTH);
		const y = Math.round(((e.clientY - rect.top) / rect.height) * VIEWPORT_HEIGHT);
		return {
			x: Math.min(Math.max(x, 0), VIEWPORT_WIDTH - 1),
			y: Math.min(Math.max(y, 0), VIEWPORT_HEIGHT - 1)
		};
	}

	function handlePointer(e: PointerEvent, kind: 'down' | 'up') {
		e.preventDefault();
		// Keep the release on the frame when the pointer leaves it during a press-and-hold.
		if (kind === 'down') frame?.setPointerCapture?.(e.pointerId);
		const point = toRemote(e);
		if (point) send({ kind, ...point });
	}

	function typeText() {
		if (!text) return;
		send({ kind: 'text', text });
		text = '';
	}
</script>

<Modal
	{isOpen}
	title="Solve the store's challenge"
	size="3xl"
	closeOnBackdrop={false}
	onClose={cancel}
>
	<div class="p-4 space-y-3 overflow-y-auto">
		<p class="text-sm text-gray-600 dark:text-gray-300">
			This is the store page, opened on the server. Click and type on it as on the real page. When
			the page is past the check, Ophi keeps the store's cookies for 7 days and checks the price
			again. What you type here goes to the store.
		</p>

		{#if error}
			<p class="text-sm text-red-600 dark:text-red-400" role="alert">{error}</p>
		{:else if ended}
			<p class="text-sm text-gray-600 dark:text-gray-300" role="status">
				The session ended. Open it again to continue.
			</p>
		{:else if image}
			<img
				bind:this={frame}
				src={`data:image/jpeg;base64,${image}`}
				alt="Store page"
				draggable="false"
				class="w-full aspect-video rounded border border-gray-200 dark:border-gray-700 cursor-pointer select-none touch-none"
				onpointerdown={(e) => handlePointer(e, 'down')}
				onpointerup={(e) => handlePointer(e, 'up')}
			/>
			<form
				class="flex flex-wrap gap-2"
				onsubmit={(e) => {
					e.preventDefault();
					typeText();
				}}
			>
				<input
					bind:value={text}
					aria-label="Text to type"
					maxlength="256"
					autocomplete="off"
					class="flex-1 min-w-40 px-3 py-1.5 text-sm rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-white"
				/>
				<button
					type="submit"
					class="px-3 py-1.5 text-sm font-medium text-white bg-brand rounded-md hover:bg-brand-hover"
				>
					Type
				</button>
				{#each KEYS as key (key)}
					<button
						type="button"
						onclick={() => send({ kind: 'key', key })}
						class="px-2 py-1.5 text-xs font-medium rounded-md border border-gray-300 dark:border-gray-600 text-gray-700 dark:text-gray-200 hover:bg-gray-100 dark:hover:bg-gray-700"
					>
						{key}
					</button>
				{/each}
			</form>
		{:else}
			<div class="flex items-center gap-2 text-sm text-gray-500 dark:text-gray-400" role="status">
				<LoaderCircle size={16} class="animate-spin" />
				Opening the store page…
			</div>
		{/if}

		<div class="flex justify-end">
			<button
				type="button"
				onclick={cancel}
				class="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-200 bg-white dark:bg-gray-700 border border-gray-300 dark:border-gray-600 rounded-md hover:bg-gray-50 dark:hover:bg-gray-600"
			>
				Cancel
			</button>
		</div>
	</div>
</Modal>
