<script lang="ts">
	import { X } from 'lucide-svelte';

	interface Props {
		name: string;
		color: string;
		removable?: boolean;
		onRemove?: () => void;
	}

	let { name, color, removable = false, onRemove }: Props = $props();

	function getContrastColor(hexColor: string): string {
		const hex = hexColor.replace('#', '');
		const r = parseInt(hex.substring(0, 2), 16);
		const g = parseInt(hex.substring(2, 4), 16);
		const b = parseInt(hex.substring(4, 6), 16);
		const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
		return luminance > 0.5 ? '#000000' : '#ffffff';
	}
</script>

<span
	class="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium"
	style="background-color: {color}; color: {getContrastColor(color)}"
	data-testid="tag-badge"
>
	{name}
	{#if removable && onRemove}
		<button
			type="button"
			onclick={(e) => {
				e.stopPropagation();
				onRemove?.();
			}}
			class="hover:opacity-70 -mr-0.5"
			aria-label="Remove tag {name}"
		>
			<X size={12} />
		</button>
	{/if}
</span>
