<script lang="ts">
	interface Props {
		min: number;
		max: number;
		current: number;
	}

	let { min, max, current }: Props = $props();

	const position = $derived(Math.min(100, Math.max(0, ((current - min) / (max - min)) * 100)));

	const fillColor = $derived.by(() => {
		if (position <= 33) return 'bg-green-500';
		if (position >= 67) return 'bg-red-500';
		return 'bg-amber-500';
	});
</script>

{#if min < max}
	<div class="w-full h-1 relative" data-testid="price-position-bar">
		<!-- Track -->
		<div class="absolute inset-0 bg-gradient-to-r from-green-500/20 via-amber-500/20 to-red-500/20"></div>
		<!-- Fill -->
		<div
			class="absolute inset-y-0 left-0 {fillColor} opacity-40"
			style="width: {position}%"
			data-testid="position-fill"
		></div>
		<!-- Marker -->
		<div
			class="absolute top-1/2 -translate-y-1/2 w-2 h-2 rounded-full {fillColor} shadow-sm ring-1 ring-white dark:ring-gray-900"
			style="left: {position}%"
			data-testid="position-marker"
		></div>
	</div>
{/if}
