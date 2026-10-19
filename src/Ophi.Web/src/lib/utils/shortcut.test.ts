import { describe, it, expect } from 'vitest';
import { isApplePlatform, shortcutHint } from './shortcut';

describe('isApplePlatform', () => {
	it('should detect macOS from userAgentData', () => {
		expect(isApplePlatform({ userAgentData: { platform: 'macOS' }, platform: '' })).toBe(true);
	});

	it('should detect an iPad from the legacy platform string', () => {
		expect(isApplePlatform({ platform: 'iPad' })).toBe(true);
	});

	it('should not treat Windows or Linux as Apple', () => {
		expect(isApplePlatform({ userAgentData: { platform: 'Windows' }, platform: 'Win32' })).toBe(
			false
		);
		expect(isApplePlatform({ platform: 'Linux x86_64' })).toBe(false);
	});
});

describe('shortcutHint', () => {
	it('should use the Command symbol on Apple platforms', () => {
		expect(shortcutHint('K', true)).toBe('⌘K');
	});

	it('should use Ctrl elsewhere', () => {
		expect(shortcutHint('K', false)).toBe('Ctrl+K');
	});
});
