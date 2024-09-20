// Intercept fetch requests for .wasm files, request pre-compressed .wasm.br files instead,
// and decompress them before returning to the caller.
const originalFetch = globalThis.fetch;
globalThis.fetch = (url, fetchArgs) => {
    if (typeof url === 'string' && url.endsWith('.wasm') && !url.includes('localhost')) {
        return (async () => {
            const response = await originalFetch(url + '.br', { cache: 'no-cache' });
            if (!response.ok) {
                return response;
            }

            try {
                const originalResponseBuffer = await response.arrayBuffer();
                const originalResponseArray = new Int8Array(originalResponseBuffer);
                const decompressedResponseArray = BrotliDecode(originalResponseArray);
                return new Response(decompressedResponseArray, {
                    headers: { 'content-type': 'application/wasm' },
                });
            } catch (error) {
                console.error('Failed to decompress response', url, error);
                return response;
            }
        })();
    }

    return originalFetch(url, fetchArgs);
};
