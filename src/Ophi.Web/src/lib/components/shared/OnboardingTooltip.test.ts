import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import OnboardingTooltip from './OnboardingTooltip.svelte';

const defaultProps = {
	targetSelector: '[data-testid="fake-target"]',
	title: 'Add a product',
	description: 'Paste a product URL to start tracking prices.',
	step: 0,
	totalSteps: 5,
	onNext: vi.fn(),
	onPrev: vi.fn(),
	onDismiss: vi.fn()
};

describe('OnboardingTooltip', () => {
	it('should render title', () => {
		render(OnboardingTooltip, { props: defaultProps });
		expect(screen.getByTestId('onboarding-title')).toHaveTextContent('Add a product');
	});

	it('should render description', () => {
		render(OnboardingTooltip, { props: defaultProps });
		expect(screen.getByTestId('onboarding-description')).toHaveTextContent(
			'Paste a product URL to start tracking prices.'
		);
	});

	it('should render step counter', () => {
		render(OnboardingTooltip, { props: defaultProps });
		expect(screen.getByTestId('onboarding-counter')).toHaveTextContent('1 / 5');
	});

	it('should hide Prev button on first step', () => {
		render(OnboardingTooltip, { props: defaultProps });
		expect(screen.queryByTestId('onboarding-prev')).not.toBeInTheDocument();
	});

	it('should show Prev button on step > 0', () => {
		render(OnboardingTooltip, { props: { ...defaultProps, step: 2 } });
		expect(screen.getByTestId('onboarding-prev')).toBeInTheDocument();
	});

	it('should show "Finish" on last step', () => {
		render(OnboardingTooltip, { props: { ...defaultProps, step: 4 } });
		expect(screen.getByTestId('onboarding-next')).toHaveTextContent('Finish');
	});

	it('should call onNext when Next clicked', async () => {
		const onNext = vi.fn();
		render(OnboardingTooltip, { props: { ...defaultProps, onNext } });
		await fireEvent.click(screen.getByTestId('onboarding-next'));
		expect(onNext).toHaveBeenCalled();
	});

	it('should call onPrev when Back clicked', async () => {
		const onPrev = vi.fn();
		render(OnboardingTooltip, { props: { ...defaultProps, step: 2, onPrev } });
		await fireEvent.click(screen.getByTestId('onboarding-prev'));
		expect(onPrev).toHaveBeenCalled();
	});

	it('should call onDismiss when Skip clicked', async () => {
		const onDismiss = vi.fn();
		render(OnboardingTooltip, { props: { ...defaultProps, onDismiss } });
		await fireEvent.click(screen.getByLabelText('Skip tour'));
		expect(onDismiss).toHaveBeenCalled();
	});

	it('should have role="dialog"', () => {
		render(OnboardingTooltip, { props: defaultProps });
		expect(screen.getByRole('dialog')).toBeInTheDocument();
	});
});
