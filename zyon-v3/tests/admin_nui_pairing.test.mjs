// JavaScript / tests/admin_nui_pairing.test.mjs / Node.js 18+ / mocked FiveM Chromium pairing contracts.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';

const code = fs.readFileSync(new URL('../fivem/zyon_admin_menu/nui/pairing.js', import.meta.url), 'utf8');
const html = fs.readFileSync(new URL('../fivem/zyon_admin_menu/nui/index.html', import.meta.url), 'utf8');

function harness(options = {}) {
    const requests = [], pairs = [], callbacks = [], events = new Map(), timers = new Map();
    let nextTimer = 0, randomCalls = 0;
    const readyStates = [...(options.readyStates ?? [true])];
    let readiness = readyStates[0];
    const crypto = options.noCrypto ? undefined : {getRandomValues(bytes) {
        randomCalls++;
        assert.equal(bytes.length, 32);
        for (let i = 0; i < bytes.length; i++) bytes[i] = i;
        return bytes;
    }};
    const respond = body => ({ok: true, async json() { return body; }});
    const emit = data => {
        for (const fn of events.get('message') ?? []) fn({data});
    };
    const successfulResult = request => ({type: 'pairResult', nonce: request.nonce, requestId: request.requestId,
        token: request.nonce, ok: true, endpoint: options.endpoint ?? 'http://127.0.0.1:30120/zyon_admin_menu/',
        playerId: 19, playerName: 'Admin', version: '3.1.1', error: ''});
    const context = vm.createContext({
        crypto, Uint8Array, AbortController, URL, JSON, Number, Array, Error,
        Math: {...Math, random() { assert.fail('Weak RNG must never be used.'); }},
        setTimeout(fn, delay) { const id = ++nextTimer; timers.set(id, {fn, delay}); return id; },
        clearTimeout(id) { timers.delete(id); },
        GetParentResourceName() { return options.resource ?? 'zyon_admin_menu'; },
        window: {addEventListener(name, fn) { events.set(name, [...(events.get(name) ?? []), fn]); }},
        async fetch(url, init) {
            requests.push({url, init});
            if (url.endsWith('/presence')) {
                if (readyStates.length) readiness = readyStates.shift();
                if (options.presenceThrows) throw new Error('No app.');
                if (options.presenceHangs) {
                    return await new Promise((resolve, reject) => {
                        init.signal.addEventListener('abort', () => reject(new Error('Presence timeout.')), {once: true});
                    });
                }
                return respond({ready: readiness});
            }
            if (url.endsWith('/pairRequest')) {
                const data = JSON.parse(init.body);
                callbacks.push(data);
                if (options.callbackThrows) throw new Error('Resource missing.');
                if (options.callbackHangs) {
                    return await new Promise((resolve, reject) => {
                        init.signal.addEventListener('abort', () => reject(new Error('Callback timeout.')), {once: true});
                    });
                }
                if (options.onCallback) options.onCallback(data, emit, successfulResult);
                else emit(successfulResult(data));
                return respond(options.callbackResponse ?? {accepted: true, requestId: data.requestId});
            }
            if (url.endsWith('/pair')) {
                pairs.push(JSON.parse(init.body));
                return respond({ok: true});
            }
            throw new Error('Unexpected destination.');
        }
    });
    vm.runInContext(code, context, {filename: 'pairing.js'});
    async function flush() {
        for (let i = 0; i < 20; i++) await Promise.resolve();
        await new Promise(resolve => setImmediate(resolve));
    }
    async function fire(delay) {
        const entry = [...timers].find(([, value]) => value.delay === delay);
        assert.ok(entry, `Expected a ${delay} ms timer.`);
        timers.delete(entry[0]);
        entry[1].fn();
        await flush();
    }
    return {requests, pairs, callbacks, timers, emit, flush, fire,
        get randomCalls() { return randomCalls; }, events};
}

test('a secure 32-byte credential is generated once and renewed without rotation', async () => {
    const h = harness();
    await h.flush();
    assert.equal(h.randomCalls, 1);
    assert.equal(h.callbacks.length, 1);
    assert.match(h.callbacks[0].nonce, /^[0-9a-f]{64}$/);
    assert.equal(h.callbacks[0].nonce, Array.from({length: 32}, (_, i) => i.toString(16).padStart(2, '0')).join(''));
    assert.equal(h.pairs.length, 1);
    assert.equal(h.pairs[0].token, h.callbacks[0].nonce);
    await h.fire(3000);
    assert.equal(h.randomCalls, 1);
    assert.equal(h.callbacks[1].nonce, h.callbacks[0].nonce);
    assert.ok(h.callbacks[1].requestId > h.callbacks[0].requestId);
    assert.equal(h.pairs[1].token, h.pairs[0].token);
    for (const request of h.requests) {
        assert.equal(request.init.cache, 'no-store');
        assert.equal(request.init.credentials, 'omit');
        assert.ok(!request.url.includes(h.pairs[0].token));
    }
});

test('disabled app never registers or renews a credential', async () => {
    const h = harness({readyStates: [false]});
    await h.flush();
    await h.fire(3000);
    assert.equal(h.callbacks.length, 0);
    assert.equal(h.pairs.length, 0);
    assert.ok(h.requests.every(request => request.url.endsWith('/presence')));
});

test('disconnect during authorization prevents posting a stale result', async () => {
    const h = harness({readyStates: [true, false]});
    await h.flush();
    assert.equal(h.callbacks.length, 1);
    assert.equal(h.pairs.length, 0);
    await h.fire(3000);
    assert.equal(h.callbacks.length, 1);
});

test('nonce and per-attempt correlation reject unrelated server responses', async () => {
    const h = harness({onCallback(request, emit, result) {
        emit({...result(request), nonce: 'f'.repeat(64)});
        emit({...result(request), requestId: request.requestId + 1});
    }});
    await h.flush();
    assert.equal(h.pairs.length, 0);
    // No next cycle is scheduled while this one is pending.
    assert.ok(![...h.timers.values()].some(timer => timer.delay === 3000));
    await h.fire(5500);
    assert.equal(h.pairs.length, 1);
    assert.equal(h.pairs[0].token, '');
    assert.match(h.pairs[0].error, /não respondeu/);
});

test('missing secure entropy reports an error without a weak fallback or server registration', async () => {
    const h = harness({noCrypto: true});
    await h.flush();
    assert.equal(h.callbacks.length, 0);
    assert.equal(h.pairs.length, 1);
    assert.equal(h.pairs[0].token, '');
    assert.match(h.pairs[0].error, /criptográfico/);
});

test('missing server resource produces an explicit app diagnostic', async () => {
    const h = harness({callbackThrows: true});
    await h.flush();
    assert.equal(h.pairs.length, 1);
    assert.equal(h.pairs[0].token, '');
    assert.match(h.pairs[0].error, /recurso FiveM/);
});

test('unavailable app keeps polling without creating server sessions', async () => {
    const h = harness({presenceThrows: true});
    await h.flush();
    await h.fire(3000);
    assert.equal(h.callbacks.length, 0);
    assert.equal(h.pairs.length, 0);
});

test('a hung discovery request is aborted after two seconds without registration', async () => {
    const h = harness({presenceHangs: true});
    await h.flush();
    assert.equal(h.requests.length, 1);
    await h.fire(2000);
    assert.equal(h.requests[0].init.signal.aborted, true);
    assert.equal(h.callbacks.length, 0);
    assert.equal(h.pairs.length, 0);
    assert.ok([...h.timers.values()].some(timer => timer.delay === 3000));
});

test('a hung NUI callback is aborted after five seconds and reports the missing resource', async () => {
    const h = harness({callbackHangs: true});
    await h.flush();
    await h.fire(5000);
    const request = h.requests.find(value => value.url.endsWith('/pairRequest'));
    assert.equal(request.init.signal.aborted, true);
    assert.equal(h.pairs.length, 1);
    assert.equal(h.pairs[0].token, '');
    assert.match(h.pairs[0].error, /recurso FiveM/);
});

test('explicit authorization failure reaches the app without credentials', async () => {
    const h = harness({onCallback(request, emit) {
        emit({type: 'pairResult', nonce: request.nonce, requestId: request.requestId, ok: false,
            error: 'Sem permissão ACE zyon.modmenu.', version: '3.1.1'});
    }});
    await h.flush();
    assert.equal(h.pairs[0].token, '');
    assert.equal(h.pairs[0].endpoint, '');
    assert.equal(h.pairs[0].error, 'Sem permissão ACE zyon.modmenu.');
});

test('untrusted diagnostics redact the credential and respect the desktop service error limit', async () => {
    const h = harness({onCallback(request, emit) {
        emit({type: 'pairResult', nonce: request.nonce, requestId: request.requestId, ok: false,
            error: `Erro: ${request.nonce} ${'x'.repeat(500)}`, version: '3.1.1'});
    }});
    await h.flush();
    assert.equal(h.pairs[0].error.length, 240);
    assert.ok(h.pairs[0].error.includes('[omitido]'));
    assert.ok(!h.pairs[0].error.includes(h.callbacks[0].nonce));
    assert.equal(h.pairs[0].token, '');
});

for (const endpoint of ['http://192.168.1.15:30120/zyon_admin_menu/', 'http://public.example/zyon_admin_menu/',
    'https://user:secret@example.com/zyon_admin_menu/', 'https://example.com/zyon_admin_menu/?token=oops',
    'file:///zyon_admin_menu/', 'https://example.com/zyon_admin_menu/#oops']) {
    test(`unsafe endpoint rejected: ${endpoint}`, async () => {
        const h = harness({endpoint});
        await h.flush();
        assert.equal(h.pairs.length, 1);
        assert.equal(h.pairs[0].token, '');
        assert.ok(h.pairs[0].error.length > 0);
    });
}

for (const endpoint of ['https://admin.example.com/custom-resource/', 'http://127.0.0.2:30120/zyon_admin_menu/',
    'http://[::1]:30120/zyon_admin_menu/', 'http://localhost:30120/zyon_admin_menu/']) {
    test(`supported endpoint accepted: ${endpoint}`, async () => {
        const h = harness({endpoint});
        await h.flush();
        assert.equal(h.pairs.length, 1);
        assert.equal(h.pairs[0].error, '');
        assert.equal(h.pairs[0].endpoint, endpoint);
        assert.equal(h.pairs[0].token, h.callbacks[0].nonce);
    });
}

test('NUI is invisible, has no focus changes or credential persistence', () => {
    assert.match(html, /<body hidden><\/body>/);
    assert.match(html, /Content-Security-Policy/);
    assert.doesNotMatch(code, /localStorage|sessionStorage|document\.cookie|console\.|SetNuiFocus/);
    assert.doesNotMatch(fs.readFileSync(new URL('../fivem/zyon_admin_menu/pairing_client.lua', import.meta.url), 'utf8'), /SetNuiFocus|print\(/);
});

test('page unload clears pending work without reconnecting', async () => {
    const h = harness({onCallback() {}});
    await h.flush();
    for (const fn of h.events.get('beforeunload') ?? []) fn();
    await h.flush();
    assert.equal(h.pairs.length, 0);
    assert.ok(![...h.timers.values()].some(timer => timer.delay === 3000));
});
