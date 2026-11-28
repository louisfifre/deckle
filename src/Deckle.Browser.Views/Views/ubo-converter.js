// Run by the browser's hidden converter web view (UboConverter.cs), once, in a page served
// from the folder that holds uBlock Origin's modules. It does what Brave's packager does in
// Node (brave-core-crx-packager, adBlockRustUtils.js): import scriptlets.js, and give each
// entry of `builtinScriptlets` as an adblock-rust Resource, its function's source as base64
// content. One addition of ours: `requiresTrust` becomes permission bit 1, which the
// engine grants to uBlock Origin's own lists only.
//
// ExecuteScriptAsync does not await a promise, so the result is written to a global and the
// caller polls it: a JSON text on success, "error:<name>" otherwise.
(function () {
    window.__uboResult = null;
    const base64 = (text) => {
        const bytes = new TextEncoder().encode(text);
        let binary = '';
        for (let i = 0; i < bytes.length; i += 0x8000)
            binary += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
        return btoa(binary);
    };
    import(new URL('/js/resources/scriptlets.js', location.href).href).then((module) => {
        const resources = module.builtinScriptlets.map((s) => {
            const resource = {
                name: s.name,
                aliases: s.aliases ?? [],
                kind: { mime: 'application/javascript' },
                content: base64(s.fn.toString()),
                dependencies: s.dependencies ?? [],
            };
            if (s.requiresTrust === true) resource.permission = 1;
            return resource;
        });
        window.__uboResult = JSON.stringify(resources);
    }).catch((error) => {
        window.__uboResult = 'error:' + (error && error.name ? error.name : 'unknown');
    });
    return 'started';
})()
