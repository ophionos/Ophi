export interface PriceStoryInput {
	currentPrice?: number;
	previousPrice?: number;
	priceChange?: number;
	priceMin?: number;
	priceMax?: number;
	sparkline?: { date: string; price: number }[];
}

export interface PriceStoryLabel {
	text: string;
	variant: 'positive' | 'negative' | 'neutral' | 'highlight';
}

export function getPriceStory(input: PriceStoryInput): PriceStoryLabel | null {
	const { currentPrice, priceChange, priceMin, priceMax, sparkline } = input;

	if (currentPrice == null) return null;

	const hasSparkline = sparkline != null && sparkline.length >= 2;

	// 1. "Just dropped!" — price decreased on the most recent data point
	if (hasSparkline && priceChange != null && priceChange < 0) {
		const last = sparkline![sparkline!.length - 1].price;
		const prev = sparkline![sparkline!.length - 2].price;
		if (last < prev) {
			return { text: 'Just dropped!', variant: 'positive' };
		}
	}

	// 2. "Lowest in 14 days" — current equals 14-day min with enough data
	if (
		priceMin != null &&
		priceMax != null &&
		priceMin !== priceMax &&
		currentPrice <= priceMin &&
		hasSparkline &&
		sparkline!.length >= 7
	) {
		return { text: 'Lowest in 14 days', variant: 'highlight' };
	}

	// 3. "Near 14-day low" — within 5% of min but not equal
	if (priceMin != null && priceMax != null && priceMin !== priceMax && priceMin > 0) {
		const pctAboveMin = (currentPrice - priceMin) / priceMin;
		if (pctAboveMin > 0 && pctAboveMin <= 0.05) {
			return { text: 'Near 14-day low', variant: 'positive' };
		}
	}

	// 4. "X% below average" — significantly below sparkline average (>10%)
	if (hasSparkline) {
		const avg = sparkline!.reduce((sum, p) => sum + p.price, 0) / sparkline!.length;
		if (avg > 0) {
			const pctBelowAvg = ((avg - currentPrice) / avg) * 100;
			if (pctBelowAvg > 10) {
				return { text: `${Math.round(pctBelowAvg)}% below average`, variant: 'positive' };
			}
		}
	}

	// 5. "Steady for X days" — last N sparkline points within 1% variation
	if (hasSparkline) {
		let steadyCount = 1;
		const lastPrice = sparkline![sparkline!.length - 1].price;
		if (lastPrice === 0) return null;
		for (let i = sparkline!.length - 2; i >= 0; i--) {
			const variation = Math.abs(sparkline![i].price - lastPrice) / lastPrice;
			if (variation <= 0.01) {
				steadyCount++;
			} else {
				break;
			}
		}
		if (steadyCount >= 3) {
			return { text: `Steady for ${steadyCount} days`, variant: 'neutral' };
		}
	}

	// 6. "Trending down" — generic fallback for any decline
	if (priceChange != null && priceChange < 0) {
		return { text: 'Trending down', variant: 'positive' };
	}

	// 7. "Price rising" — significant rise (>5%)
	if (priceChange != null && priceChange > 5) {
		return { text: 'Price rising', variant: 'negative' };
	}

	return null;
}
