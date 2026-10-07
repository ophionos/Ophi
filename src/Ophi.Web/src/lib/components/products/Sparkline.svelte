<script lang="ts">
	interface Props {
		data: { date: string; price: number }[];
		width?: number;
		height?: number;
	}

	let { data, width = 64, height = 20 }: Props = $props();

	const padding = 1;

	const points = $derived.by(() => {
		if (data.length < 3) return '';
		const prices = data.map((d) => d.price);
		const min = Math.min(...prices);
		const max = Math.max(...prices);
		const range = max - min || 1;

		const innerW = width - padding * 2;
		const innerH = height - padding * 2;

		return data
			.map((d, i) => {
				const x = padding + (i / (data.length - 1)) * innerW;
				const y = padding + innerH - ((d.price - min) / range) * innerH;
				return `${x.toFixed(1)},${y.toFixed(1)}`;
			})
			.join(' ');
	});

	const strokeColor = $derived.by(() => {
		if (data.length < 3) return 'rgb(156, 163, 175)';
		const first = data[0].price;
		const last = data[data.length - 1].price;
		if (last < first) return 'rgb(22, 163, 74)';
		if (last > first) return 'rgb(239, 68, 68)';
		return 'rgb(156, 163, 175)';
	});
</script>

{#if data.length >= 3}
	<svg
		{width}
		{height}
		viewBox="0 0 {width} {height}"
		class="block"
		aria-hidden="true"
	>
		<polyline
			{points}
			fill="none"
			stroke={strokeColor}
			stroke-width="1.5"
			stroke-linecap="round"
			stroke-linejoin="round"
		/>
	</svg>
{/if}
