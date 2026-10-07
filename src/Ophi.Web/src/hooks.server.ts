import { env } from '$env/dynamic/private';
import { createHandle } from '$lib/server/handle';

// Logic lives in $lib/server/handle so it can be unit-tested without $env (vitest can't resolve it).
export const handle = createHandle(env.API_URL ?? 'http://localhost:5000');
