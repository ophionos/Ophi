import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import { createRawSnippet } from 'svelte';
import FormError from './FormError.svelte';

describe('FormError', () => {
	it('should announce the message via role="alert" when rendered', () => {
		render(FormError, { props: { message: 'Something went wrong' } });
		expect(screen.getByRole('alert')).toHaveTextContent('Something went wrong');
	});

	it('should put the id on the message element when given, so inputs can reference it', () => {
		render(FormError, { props: { message: 'Name is required', id: 'name-error' } });
		expect(screen.getByRole('alert')).toHaveAttribute('id', 'name-error');
	});

	it('should set the test id on the wrapper when given', () => {
		render(FormError, { props: { message: 'Bad', testid: 'my-error' } });
		expect(screen.getByTestId('my-error')).toContainElement(screen.getByRole('alert'));
	});

	it('should render the boxed style when variant is box', () => {
		render(FormError, { props: { message: 'Bad', variant: 'box', testid: 'err' } });
		expect(screen.getByTestId('err')).toHaveClass('bg-red-50');
	});

	it('should render the inline style by default', () => {
		render(FormError, { props: { message: 'Bad', testid: 'err' } });
		expect(screen.getByTestId('err')).not.toHaveClass('bg-red-50');
	});

	it('should render extra content outside the alert region when children are given', () => {
		const children = createRawSnippet(() => ({
			render: () => '<button data-testid="retry">Retry</button>'
		}));
		render(FormError, { props: { message: 'Network error', children } });
		expect(screen.getByTestId('retry')).toBeInTheDocument();
		// The retry control must not be read out as part of the error announcement.
		expect(screen.getByRole('alert')).not.toContainElement(screen.getByTestId('retry'));
	});
});
