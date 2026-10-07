const publicRoutes = ['/', '/auth/login', '/auth/register', '/auth/forgot-password', '/auth/reset-password'];

export const load = async ({ fetch, url }) => {
	const isPublic = publicRoutes.includes(url.pathname);
	if (isPublic) {
		return { user: null, authChecked: true, isPublic: true };
	}

	try {
		const res = await fetch('/api/v1/auth/me');
		if (res.ok) {
			return { user: await res.json(), authChecked: true, isPublic: false };
		}
	} catch {
		// Ignore and let client-side auth check handle it.
	}

	return { user: null, authChecked: false, isPublic: false };
};
