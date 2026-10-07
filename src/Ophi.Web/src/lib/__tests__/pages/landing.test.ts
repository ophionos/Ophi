import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import LandingPage from '../../../routes/+page.svelte';

describe('Landing Page', () => {
	describe('store support copy', () => {
		// The scraping engine has exactly two tuned adapters — CodeStoreConfigProvider registers
		// AmazonConfig and EbayConfig, and nothing in Ophi.Infrastructure mentions Walmart, Target
		// or Best Buy. Those URLs are scraped by the generic selector set (Open Graph, Schema.org,
		// common price classes), which usually works but is not the per-store tuning that
		// "built-in support for X" implies. See issue #131.

		it('should not advertise built-in support for stores with no tuned adapter', () => {
			render(LandingPage);

			expect(screen.getByText(/auto-extract the price/i)).not.toHaveTextContent(/walmart/i);
		});

		it('should still promise that any product URL works', () => {
			// Dropping the false specificity must not read as "we only handle two stores" — the
			// generic fallback genuinely does scrape arbitrary product pages.
			render(LandingPage);

			expect(screen.getByText(/auto-extract the price/i)).toHaveTextContent(/any product page/i);
		});
	});

	describe('sign-up calls to action', () => {
		it('should offer sign-up when sign-up is open', () => {
			render(LandingPage);

			expect(screen.getAllByRole('link', { name: /Get Started Free/i }).length).toBeGreaterThan(0);
		});

		it('should only offer sign-in when sign-up is closed', () => {
			render(LandingPage, {
				props: { data: { user: null, authChecked: true, isPublic: true, registrationOpen: false } }
			});

			expect(screen.queryByRole('link', { name: /Get Started Free/i })).not.toBeInTheDocument();
			expect(screen.getAllByRole('link', { name: /Sign In/i }).length).toBeGreaterThan(0);
		});
	});
});
