<script lang="ts">
	import type { PageData } from './$types';
	import { api, type ApiKeyInfo } from '$lib/api/client';
	import ApiKeyCreateModal from '$lib/components/apikeys/ApiKeyCreateModal.svelte';
	import ApiKeyList from '$lib/components/apikeys/ApiKeyList.svelte';
	import ConfirmModal from '$lib/components/shared/ConfirmModal.svelte';
	import { toast } from '$lib/stores/toast.svelte';

	let { data: pageData }: { data: PageData } = $props();

	let apiKeys = $derived<ApiKeyInfo[]>(pageData.apiKeys);
	let apiKeyModalOpen = $state(false);
	let createdApiKey = $state<string | undefined>(undefined);
	let apiKeyDeleteConfirmOpen = $state(false);
	let deletingApiKeyId = $state<string | undefined>(undefined);
	let apiKeyDeleteLoading = $state(false);

	async function handleCreateApiKey(data: { name: string; scopes: string[]; expiresAt?: string }) {
		const result = await api.createApiKey(data as import('$lib/api/client').CreateApiKeyRequest);
		createdApiKey = result.key;
		apiKeys = [{ id: result.id, name: result.name, scopes: result.scopes, lastUsedAt: undefined, expiresAt: result.expiresAt, createdAt: result.createdAt }, ...apiKeys];
	}

	function handleCloseApiKeyModal() {
		apiKeyModalOpen = false;
		createdApiKey = undefined;
	}

	function handleDeleteApiKey(id: string) {
		deletingApiKeyId = id;
		apiKeyDeleteConfirmOpen = true;
	}

	async function handleConfirmDeleteApiKey() {
		if (!deletingApiKeyId) return;
		apiKeyDeleteLoading = true;
		try {
			await api.deleteApiKey(deletingApiKeyId);
			apiKeys = apiKeys.filter((k) => k.id !== deletingApiKeyId);
			toast.success('API key revoked');
			apiKeyDeleteConfirmOpen = false;
			deletingApiKeyId = undefined;
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to revoke API key');
		} finally {
			apiKeyDeleteLoading = false;
		}
	}
</script>

<svelte:head>
	<title>API Keys - Ophi</title>
</svelte:head>

<ApiKeyList
	keys={apiKeys}
	deletingId={deletingApiKeyId}
	onCreate={() => { createdApiKey = undefined; apiKeyModalOpen = true; }}
	onDelete={handleDeleteApiKey}
/>

<ApiKeyCreateModal
	isOpen={apiKeyModalOpen}
	createdKey={createdApiKey}
	onClose={handleCloseApiKeyModal}
	onSave={handleCreateApiKey}
/>

<ConfirmModal
	isOpen={apiKeyDeleteConfirmOpen}
	title="Revoke API Key"
	message="Are you sure you want to revoke this API key? Any scripts using this key will immediately lose access."
	confirmText="Revoke"
	onConfirm={handleConfirmDeleteApiKey}
	onCancel={() => { apiKeyDeleteConfirmOpen = false; deletingApiKeyId = undefined; }}
	loading={apiKeyDeleteLoading}
/>
