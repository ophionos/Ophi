<script lang="ts">
	import { onMount, onDestroy } from 'svelte';

	const stages = [
		'Connecting...',
		'Reading page...',
		'Extracting price...'
	] as const;

	let stageIndex = $state(0);
	let timer: ReturnType<typeof setInterval> | undefined;

	onMount(() => {
		timer = setInterval(() => {
			if (stageIndex < stages.length - 1) {
				stageIndex++;
				if (stageIndex >= stages.length - 1 && timer) {
					clearInterval(timer);
					timer = undefined;
				}
			}
		}, 1500);
	});

	onDestroy(() => {
		if (timer) clearInterval(timer);
	});

	const label = $derived(stages[stageIndex]);
	const progress = $derived(((stageIndex + 1) / stages.length) * 100);
</script>

<div class="w-full h-full flex flex-col items-center justify-center gap-3 p-4">
	<!-- Simulated page outline -->
	<div
		class="relative w-20 h-24 rounded border-2 border-gray-300/60 dark:border-gray-500/40 bg-white/50 dark:bg-gray-700/30 overflow-hidden"
		data-testid="page-outline"
	>
		<!-- Fake content lines -->
		<div class="p-2 space-y-1.5">
			<div class="h-1.5 w-12 bg-gray-200 dark:bg-gray-600 rounded-full"></div>
			<div class="h-1 w-16 bg-gray-200/70 dark:bg-gray-600/70 rounded-full"></div>
			<div class="h-1 w-10 bg-gray-200/70 dark:bg-gray-600/70 rounded-full"></div>
			<div class="h-3 w-14 bg-gray-200/50 dark:bg-gray-600/50 rounded mt-1"></div>
			<div class="h-1 w-12 bg-gray-200/70 dark:bg-gray-600/70 rounded-full"></div>
			<div class="h-1 w-8 bg-gray-200/70 dark:bg-gray-600/70 rounded-full"></div>
		</div>

		<!-- Scan line sweep -->
		<div
			class="absolute left-0 w-full h-0.5 bg-gradient-to-r from-transparent via-brand to-transparent animate-scan-sweep"
			data-testid="scan-line"
		></div>
	</div>

	<!-- Stage label -->
	<p class="text-xs font-medium text-gray-500 dark:text-gray-400 text-center">
		{label}
	</p>

	<!-- Progress bar -->
	<div class="w-16 h-1 bg-gray-200 dark:bg-gray-600 rounded-full overflow-hidden">
		<div
			class="h-full bg-brand rounded-full transition-all duration-500 ease-out"
			style="width: {progress}%"
		></div>
	</div>
</div>
