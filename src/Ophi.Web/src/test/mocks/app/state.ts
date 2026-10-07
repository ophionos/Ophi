export const page = {
	url: new URL('http://localhost'),
	params: {} as Record<string, string>,
	route: { id: '' },
	status: 200,
	error: null as Error | null,
	data: {} as Record<string, unknown>,
	form: null
};

// `$app/state` navigating is always an object; its fields are null while idle.
export const navigating = {
	from: null,
	to: null,
	type: null,
	willUnload: null,
	delta: null,
	complete: null
};
export const updated = { current: false, check: async () => false };
