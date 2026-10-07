import { describe, it, expect, beforeEach } from 'vitest';
import { auth } from './auth.svelte';

describe('auth store', () => {
	beforeEach(() => {
		auth.logout();
	});

	it('should start with null user', () => {
		expect(auth.current).toBeNull();
	});

	it('should login a user', () => {
		auth.login({ id: '1', email: 'test@example.com', name: 'Test' });

		expect(auth.current).toEqual({ id: '1', email: 'test@example.com', name: 'Test' });
	});

	it('should logout a user', () => {
		auth.login({ id: '1', email: 'test@example.com', name: 'Test' });
		auth.logout();

		expect(auth.current).toBeNull();
	});

	it('should set user directly', () => {
		auth.setUser({ id: '2', email: 'new@example.com', name: 'New User' });

		expect(auth.current?.id).toBe('2');
	});

	it('should set user to null', () => {
		auth.login({ id: '1', email: 'test@example.com', name: 'Test' });
		auth.setUser(null);

		expect(auth.current).toBeNull();
	});
});
