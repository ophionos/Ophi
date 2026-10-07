/** @type {import('tailwindcss').Config} */
export default {
	content: ['./src/**/*.{html,js,svelte,ts}'],
	darkMode: 'class',
	theme: {
		extend: {
			colors: {
				brand: {
					DEFAULT: '#0d9488',
					hover: '#0f766e',
					light: '#f0fdfa',
					subtle: '#ccfbf1',
					dark: '#134e4a',
					muted: '#2dd4bf',
					secondary: '#6366f1',
					'secondary-dark': '#818cf8'
				}
			}
		}
	},
	plugins: []
};
