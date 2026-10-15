-- Lua / tests/admin_pairing_client.lua / Lua 5.4 / mocked automatic FiveM pairing callback contracts.
local root = arg[1] or (arg[0]:match('^(.*)/tests/[^/]+$') or '.')
local path = root .. '/fivem/zyon_admin_menu/pairing_client.lua'
local nonce = string.rep('a', 64)
local assertions = 0
local function check(condition, message)
    assertions = assertions + 1
    assert(condition, message)
end

local function harness(peer)
    local callbacks, events, handlers, messages, emitted, threads = {}, {}, {}, {}, {}, {}
    local clock = 0
    local env = setmetatable({
        RegisterNUICallback = function(name, fn) callbacks[name] = fn end,
        RegisterNetEvent = function(name, fn) events[name] = fn end,
        AddEventHandler = function(name, fn) handlers[name] = fn end,
        TriggerServerEvent = function(name, value, requestId)
            emitted[#emitted + 1] = {name = name, nonce = value, requestId = requestId}
        end,
        SendNUIMessage = function(message) messages[#messages + 1] = message end,
        GetGameTimer = function() return clock end,
        GetCurrentServerEndpoint = function() return peer end,
        GetCurrentResourceName = function() return 'zyon_admin_menu' end,
        CreateThread = function(fn) threads[#threads + 1] = coroutine.create(fn) end,
        Wait = function() coroutine.yield() end
    }, {__index = _G})
    assert(loadfile(path, 't', env))()
    local result = {ok = true, nonce = nonce, requestId = 1, playerId = 19, playerName = 'Admin', version = '3.1.1'}
    return {callbacks = callbacks, events = events, messages = messages, emitted = emitted,
        start = function(data)
            local reply
            callbacks.pairRequest(data or {nonce = nonce, requestId = 1}, function(value) reply = value end)
            return reply
        end,
        deliver = function(value, sender)
            env.source = sender or 65535
            events['zyon_admin_menu:pairResult'](value or result)
        end,
        result = result,
        now = function(value) clock = value end,
        timeout = function()
            local ok, err = coroutine.resume(threads[1])
            assert(ok, err)
        end,
        stop = function() handlers.onClientResourceStop('zyon_admin_menu') end}
end

do
    local h = harness('127.0.0.1:30120')
    for _, invalid in ipairs({{nonce = '', requestId = 1}, {nonce = string.rep('x', 64), requestId = 1},
        {nonce = nonce, requestId = 0}, {nonce = nonce, requestId = 1.5}, {nonce = nonce, requestId = '1'}}) do
        check(h.start(invalid).accepted == false, 'Invalid callback must be rejected.')
    end
    check(#h.emitted == 0, 'Invalid requests must not reach the server.')
    local accepted = h.start()
    check(accepted.accepted == true and accepted.requestId == 1, 'Callback must acknowledge immediately.')
    check(h.emitted[1].name == 'zyon_admin_menu:pairRequest' and h.emitted[1].nonce == nonce
        and h.emitted[1].requestId == 1, 'Server event protocol includes attempt correlation.')
    check(h.start({nonce = nonce, requestId = 2}).accepted == false, 'No overlapping requests.')
    h.deliver(nil, 0)
    check(#h.messages == 0, 'Local event spoofing must be ignored.')
    h.deliver({ok = true, nonce = string.rep('b', 64), playerId = 19})
    check(#h.messages == 0, 'Unrelated response must be ignored.')
    h.deliver()
    local message = h.messages[1]
    check(message.ok and message.token == nonce and message.nonce == nonce, 'Only accepted results carry credential.')
    check(message.endpoint == 'http://127.0.0.1:30120/zyon_admin_menu/', 'Infer actual loopback host and explicit port.')
    check(message.requestId == 1 and message.version == '3.1.1' and message.playerId == 19, 'Browser correlation and version.')
    h.deliver()
    check(#h.messages == 1, 'A pending request is consumed once.')
end

do
    local h = harness('127.0.0.1:30120')
    h.start()
    h.now(9000)
    check(h.start({nonce = nonce, requestId = 2}).accepted, 'A later attempt can replace an expired pending request.')
    h.deliver()
    check(#h.messages == 0, 'An old reply with the same stable nonce cannot authorize a later attempt.')
    h.result.requestId = 2
    h.deliver()
    check(#h.messages == 1 and h.messages[1].requestId == 2 and h.messages[1].ok, 'The exact new server attempt is accepted.')
end

for _, peer in ipairs({'192.168.1.20:30120', '203.0.113.5:30120', '127.0.0.1', '127.0.0.1:0', '127.0.0.1:99999', ''}) do
    local h = harness(peer)
    h.start()
    h.deliver()
    check(not h.messages[1].ok and h.messages[1].token == '' and h.messages[1].endpoint == '', 'Unsafe peer cannot produce credential: ' .. peer)
    check(type(h.messages[1].error) == 'string' and #h.messages[1].error > 0, 'Invalid peer must explain configuration requirement.')
end

for _, peer in ipairs({'[::1]:30120', '127.0.0.2:30120', 'localhost:30120'}) do
    local h = harness(peer)
    h.start()
    h.deliver()
    check(h.messages[1].ok, 'Supported loopback peer must be accepted: ' .. peer)
    check(h.messages[1].endpoint == 'http://' .. peer .. '/zyon_admin_menu/', 'Preserve loopback address and actual port.')
end

for _, endpoint in ipairs({'https://admin.example.com/zyon_admin_menu/', 'https://admin.example.com/custom-prefix'}) do
    local h = harness('203.0.113.5:30120')
    h.start()
    h.result.endpoint = endpoint
    h.deliver()
    check(h.messages[1].ok and h.messages[1].endpoint:sub(-1) == '/', 'Valid configured HTTPS endpoint must be accepted.')
end

for _, endpoint in ipairs({'http://admin.example.com/zyon_admin_menu/', 'https://user:secret@example.com/',
    'https://admin.example.com/?token=x', 'https://admin.example.com/#x', 'https://admin.example.com:99999/'}) do
    local h = harness('127.0.0.1:30120')
    h.start()
    h.result.endpoint = endpoint
    h.deliver()
    check(not h.messages[1].ok and h.messages[1].token == '', 'Unsafe configured endpoint rejected: ' .. endpoint)
end

do
    local h = harness('127.0.0.1:30120')
    h.start()
    h.result.ok, h.result.error = false, 'Sem permissão ACE zyon.modmenu.'
    h.deliver()
    check(h.messages[1].token == '' and h.messages[1].error == h.result.error, 'Server permission denial is explicit and secret-free.')
end

do
    local h = harness('127.0.0.1:30120')
    h.start()
    h.result.ok, h.result.error = false, 'Erro: ' .. nonce .. ' ' .. string.rep('x', 500)
    h.deliver()
    check(#h.messages[1].error == 240, 'Diagnostics must respect the desktop service error limit.')
    check(not h.messages[1].error:find(nonce, 1, true), 'Diagnostics must not leak a credential.')
    check(h.messages[1].error:find('[omitido]', 1, true) ~= nil, 'Diagnostic credential is redacted.')
end

do
    local h = harness('127.0.0.1:30120')
    h.timeout()
    h.start()
    h.now(5000)
    h.timeout()
    check(#h.messages == 1 and not h.messages[1].ok and h.messages[1].token == '', 'Timeout must report without credentials.')
    h.deliver()
    check(#h.messages == 1, 'Late timed-out result must be ignored.')
end

do
    local h = harness('127.0.0.1:30120')
    h.now(4294967200)
    h.start()
    h.now(100)
    h.deliver()
    check(h.messages[1].ok, 'Game timer wrap preserves a still-valid request.')
end

do
    local h = harness('127.0.0.1:30120')
    h.start()
    h.stop()
    h.deliver()
    check(#h.messages == 0, 'Resource stop clears pending authorization.')
end

print(('Pairing client contract: %d assertions passed.'):format(assertions))
