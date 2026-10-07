/**
 * Creates an animated price value that smoothly transitions between numbers.
 * Uses requestAnimationFrame with easing for smooth visual transitions.
 */
export function createAnimatedPrice(initial: number = 0) {
	let current = $state(initial);
	let _target = $state(initial);
	let animationFrame: number | undefined;
	let startValue = initial;
	let startTime = 0;
	const duration = 600; // ms

	function easeOutCubic(t: number): number {
		return 1 - Math.pow(1 - t, 3);
	}

	function animate(now: number) {
		const elapsed = now - startTime;
		const progress = Math.min(elapsed / duration, 1);
		const eased = easeOutCubic(progress);

		current = startValue + (_target - startValue) * eased;

		if (progress < 1) {
			animationFrame = requestAnimationFrame(animate);
		} else {
			current = _target;
			animationFrame = undefined;
		}
	}

	return {
		get current() {
			return current;
		},
		set target(value: number) {
			if (value === _target) return;
			_target = value;
			// Snap immediately on first value (no animation from 0)
			if (current === 0 && startValue === 0) {
				current = value;
				startValue = value;
				return;
			}
			startValue = current;
			startTime = performance.now();
			if (animationFrame) cancelAnimationFrame(animationFrame);
			animationFrame = requestAnimationFrame(animate);
		},
		destroy() {
			if (animationFrame) cancelAnimationFrame(animationFrame);
		}
	};
}
