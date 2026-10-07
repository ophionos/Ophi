export interface TestStoreData {
	storeId: string;
	name: string;
	domainPatterns: string[];
	priceSelectors: string[];
	nameSelectors: string[];
	imageSelectors: string[];
	priceRegexPatterns?: string[];
	imageRegexPatterns?: string[];
	priceLocale?: string;
	requiresJavaScript?: boolean;
}

export function createTestStoreData(overrides?: Partial<TestStoreData>): TestStoreData {
	const timestamp = Date.now();
	return {
		storeId: `test-store-${timestamp}`,
		name: `Test Store ${timestamp}`,
		domainPatterns: [`teststore-${timestamp}.example.com`],
		priceSelectors: ['.price', '#product-price'],
		nameSelectors: ['h1.product-title', '.product-name'],
		imageSelectors: ['.product-image img', '#main-image'],
		...overrides
	};
}

export function createGlobaldataStoreData(): TestStoreData {
	const timestamp = Date.now();
	return {
		storeId: `globaldata-pt-${timestamp}`,
		name: 'Globaldata Portugal',
		domainPatterns: ['globaldata.pt'],
		priceSelectors: ["meta[property='product:price:amount']|content"],
		nameSelectors: ['h1'],
		imageSelectors: ["meta[property='og:image']|content"],
		priceLocale: 'pt-PT',
		requiresJavaScript: true
	};
}

export const BUILT_IN_STORES = ['Amazon', 'eBay'];
