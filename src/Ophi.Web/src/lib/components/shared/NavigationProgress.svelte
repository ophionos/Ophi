<script lang="ts">
	interface Props {
		/** True while a client-side navigation (and its loader) is in flight. */
		active: boolean;
		/** Only show for navigations slower than this, so fast ones don't flash. */
		delayMs?: number;
	}

	let { active, delayMs = 150 }: Props = $props();

	let visible = $state(false);

	// Loaders block navigation, so the old page stays up until data arrives; without this bar a
	// slow loader makes a click look like it did nothing.
	$effect(() => {
		if (!active) {
			visible = false;
			return;
		}
		const timer = setTimeout(() => (visible = true), delayMs);
		return () => clearTimeout(timer);
	});
</script>

{#if visible}
	<div
		role="progressbar"
		aria-label="Loading page"
		class="fixed top-0 inset-x-0 h-0.5 z-[300] overflow-hidden bg-brand/20"
	>
		<div class="h-full w-1/3 bg-brand animate-nav-progress motion-reduce:w-full"></div>
	</div>
{/if}
