# Frontend (src/Ophi.Web)

## Svelte MCP Tools

This repo's Svelte MCP server provides comprehensive Svelte 5 / SvelteKit docs. When writing Svelte code:

1. Use `list-sections` to discover relevant docs at the start of a Svelte task.
2. Use `get-documentation` to fetch the sections that match the use case.
3. After writing Svelte code, run `svelte-autofixer` until it returns no issues or suggestions.
4. Only generate a playground link via `playground-link` when the user explicitly asks and the code was not written to project files.
