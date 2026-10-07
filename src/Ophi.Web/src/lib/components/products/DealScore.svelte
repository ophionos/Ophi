<script lang="ts">
	interface Props {
		score: number;
	}

	let { score }: Props = $props();

	const clamped = $derived(Math.min(100, Math.max(0, Math.round(score))));
	const radius = 9;
	const circumference = 2 * Math.PI * radius;
	const offset = $derived(circumference - (clamped / 100) * circumference);

	const strokeColor = $derived.by(() => {
		if (clamped > 60) return '#22c55e';
		if (clamped >= 30) return '#f59e0b';
		return '#ef4444';
	});
</script>

<svg
	width="24"
	height="24"
	viewBox="0 0 24 24"
	aria-label="Deal score: {clamped} out of 100"
	role="img"
>
	<!-- Background circle -->
	<circle
		cx="12"
		cy="12"
		r={radius}
		fill="none"
		stroke="#e5e7eb"
		stroke-width="3"
	/>
	<!-- Progress circle -->
	<circle
		cx="12"
		cy="12"
		r={radius}
		fill="none"
		stroke={strokeColor}
		stroke-width="3"
		stroke-linecap="round"
		stroke-dasharray={circumference}
		style="stroke-dashoffset: {offset}; transform: rotate(-90deg); transform-origin: center;"
	/>
	<!-- Score text -->
	<text
		x="12"
		y="12"
		text-anchor="middle"
		dominant-baseline="central"
		font-size="8"
		font-weight="bold"
		fill="currentColor"
	>{clamped}</text>
</svg>
