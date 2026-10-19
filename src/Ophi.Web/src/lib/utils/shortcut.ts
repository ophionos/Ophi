/** The slice of `navigator` the check reads; `userAgentData` exists only in Chromium browsers. */
interface PlatformInfo {
	platform: string;
	userAgentData?: { platform: string };
}

/** macOS, iOS and iPadOS use Command for shortcuts; every other platform uses Ctrl. */
export function isApplePlatform(nav: PlatformInfo): boolean {
	const platform = nav.userAgentData?.platform || nav.platform;
	return /mac|iphone|ipad|ipod/i.test(platform);
}

/** The visible hint for a Cmd/Ctrl shortcut: "⌘K" on Apple platforms, "Ctrl+K" elsewhere. */
export function shortcutHint(key: string, apple: boolean): string {
	return apple ? `⌘${key}` : `Ctrl+${key}`;
}
