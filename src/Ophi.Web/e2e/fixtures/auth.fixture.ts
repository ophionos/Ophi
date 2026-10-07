import { test as base, expect } from '@playwright/test';

// Test user credentials (created during global setup)
export const TEST_USER = {
	email: 'e2e-test@ophi.local',
	password: 'E2eTestPassword123!',
	name: 'E2E Test User'
};

// Extended test fixture that ensures authentication
export const test = base.extend({});

export { expect };
