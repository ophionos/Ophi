<script lang="ts">
	interface Props {
		size?: number;
		class?: string;
	}

	let { size = 28, class: className = '' }: Props = $props();
</script>

<svg
	xmlns="http://www.w3.org/2000/svg"
	viewBox="0 0 192 192"
	width={size}
	height={size}
	class={className}
	aria-hidden="true"
>
	<rect width="192" height="192" rx="32" fill="#0d9488" />
	<g transform="translate(96,96)">
		<path
			d="M-20,-50 L20,-50 L30,-40 L30,10 L-10,50 L-20,50 L-30,40 L-30,-40 Z"
			fill="none"
			stroke="white"
			stroke-width="8"
			stroke-linejoin="round"
		/>
		<circle cx="-5" cy="-25" r="10" fill="white" />
		<line x1="30" y1="10" x2="50" y2="30" stroke="white" stroke-width="8" stroke-linecap="round" />
	</g>
</svg>
