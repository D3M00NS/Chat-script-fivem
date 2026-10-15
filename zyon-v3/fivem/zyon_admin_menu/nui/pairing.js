// JavaScript / fivem/zyon_admin_menu/nui/pairing.js / FiveM Chromium NUI / invisible local admin pairing.
(() => {
    'use strict';
    const VERSION = '3.1.1';
    const LOCAL_APP = 'http://127.0.0.1:39743';
    const INTERVAL_MS = 3000;
    let nonce = null;
    let startupError = '';
    let pending = null;
    let requestId = 0;
    let stopped = false;
    let timer = null;

    // One cryptographic credential for this browser lifetime. Never use Math.random.
    try {
        if (!globalThis.crypto || typeof globalThis.crypto.getRandomValues !== 'function') {
            throw new Error('Secure browser entropy unavailable.');
        }
        const bytes = new Uint8Array(32);
        globalThis.crypto.getRandomValues(bytes);
        nonce = Array.from(bytes, value => value.toString(16).padStart(2, '0')).join('');
    } catch {
        startupError = 'O FiveM não disponibilizou um gerador criptográfico seguro. Atualize o FiveM e reinicie a cidade.';
    }

    async function fetchJson(url, options, timeoutMs) {
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), timeoutMs);
        try {
            const response = await fetch(url, {...options, signal: controller.signal, cache: 'no-store', credentials: 'omit'});
            if (!response.ok) throw new Error('HTTP request failed.');
            return await response.json();
        } finally {
            clearTimeout(timeout);
        }
    }

    async function appReady() {
        const value = await fetchJson(`${LOCAL_APP}/presence`, {method: 'GET'}, 2000);
        return value !== null && typeof value === 'object' && value.ready === true;
    }

    function errorPayload(message) {
        let error = typeof message === 'string' ? message : 'O recurso retornou um diagnóstico inválido.';
        if (nonce) error = error.split(nonce).join('[omitido]');
        return {endpoint: '', token: '', playerId: 0, playerName: '', error: error.slice(0, 240), version: VERSION};
    }

    function loopback(hostname) {
        const host = hostname.toLowerCase();
        if (host === 'localhost' || host === '[::1]' || host === '::1') return true;
        const parts = host.split('.');
        return parts.length === 4 && parts[0] === '127'
            && parts.every(part => /^\d{1,3}$/.test(part) && Number(part) <= 255);
    }

    function checkedPayload(data) {
        if (!data || data.version !== VERSION || data.ok !== true) {
            return errorPayload(typeof data?.error === 'string' && data.error.length > 0
                ? data.error : 'O servidor não autorizou a conexão. Verifique a permissão ACE zyon.modmenu.');
        }
        if (typeof data.endpoint !== 'string' || data.endpoint.length > 2048
            || /[\s\\@?#]/.test(data.endpoint) || data.token !== nonce
            || !Number.isSafeInteger(data.playerId) || data.playerId < 1 || typeof data.playerName !== 'string') {
            return errorPayload('O recurso retornou dados de conexão inválidos.');
        }
        try {
            const endpoint = new URL(data.endpoint);
            if (endpoint.username || endpoint.password || endpoint.search || endpoint.hash
                || (endpoint.protocol !== 'https:' && !(endpoint.protocol === 'http:' && loopback(endpoint.hostname)))) {
                return errorPayload('Configure zyon_menu_public_url com a URL HTTPS pública do recurso no servidor.');
            }
            return {endpoint: endpoint.href.endsWith('/') ? endpoint.href : `${endpoint.href}/`, token: nonce,
                playerId: data.playerId, playerName: data.playerName.slice(0, 128), error: '', version: VERSION};
        } catch {
            return errorPayload('A URL pública do recurso é inválida.');
        }
    }

    async function requestPair() {
        const id = ++requestId;
        let settle;
        const result = new Promise(resolve => { settle = resolve; });
        const current = {id, settle, timeout: null};
        current.timeout = setTimeout(() => {
            if (pending === current) pending = null;
            settle(errorPayload('O recurso não respondeu. Verifique se zyon_admin_menu está iniciado no servidor.'));
        }, 5500);
        pending = current;
        try {
            const resource = typeof GetParentResourceName === 'function' ? GetParentResourceName() : '';
            if (resource !== 'zyon_admin_menu') throw new Error('Unexpected resource name.');
            const accepted = await fetchJson(`https://${resource}/pairRequest`, {
                method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify({nonce, requestId: id})
            }, 5000);
            if (accepted?.accepted !== true || accepted.requestId !== id) {
                settle(errorPayload(typeof accepted?.error === 'string'
                    ? accepted.error : 'O recurso recusou a solicitação local.'));
            }
            return await result;
        } catch {
            return errorPayload('Não foi possível comunicar com o recurso FiveM. Verifique se zyon_admin_menu está iniciado na cidade.');
        } finally {
            clearTimeout(current.timeout);
            if (pending === current) pending = null;
        }
    }

    window.addEventListener('message', event => {
        const data = event.data;
        if (!pending || !data || data.type !== 'pairResult' || data.nonce !== nonce || data.requestId !== pending.id) return;
        pending.settle(checkedPayload(data));
    });

    async function cycle() {
        try {
            if (!stopped && await appReady()) {
                const payload = startupError ? errorPayload(startupError) : await requestPair();
                // A server reply may arrive after Disconnect. Recheck the app immediately
                // before delivering it; the loopback service also rejects disabled pairing.
                if (!stopped && await appReady()) {
                    await fetchJson(`${LOCAL_APP}/pair`, {
                        method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify(payload)
                    }, 2000);
                }
            }
        } catch {
            // Closing/starting the desktop app is normal; retry without logging credentials.
        } finally {
            if (!stopped) timer = setTimeout(cycle, INTERVAL_MS);
        }
    }

    window.addEventListener('beforeunload', () => {
        stopped = true;
        clearTimeout(timer);
        if (pending) {
            clearTimeout(pending.timeout);
            pending.settle(errorPayload('O recurso foi encerrado.'));
            pending = null;
        }
    });
    void cycle();
})();
