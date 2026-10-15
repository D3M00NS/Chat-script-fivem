-- Lua 5.4 / admin_server.lua / mocked FXServer / HTTP and authorization contract checks.
-- Run from the project root: lua tests/admin_server.lua
local serverPath = (arg[0]:match('^(.*)/tests/[^/]+$') or '.') .. '/fivem/zyon_admin_menu/server.lua'
local function copy(value)
    if type(value) ~= 'table' then return value end
    local result = {}
    for key, item in pairs(value) do result[key] = copy(item) end
    setmetatable(result, getmetatable(value))
    return result
end

local function fixture(options)
    options = options or {}
    local f = { clock = 0, token = string.rep('a', 64), license = 'license:' .. string.rep('b', 40),
        players = { '7', '9' }, allowed = { [7] = true, [9] = true }, handlers = {}, threads = {},
        timeouts = {}, effects = {}, pairResults = {}, adminResults = {}, decoded = {}, parseCalls = 0, responseCalls = 0,
        requestId = 0, pairAttemptId = 0 }
    f.identifiers = { [7] = { f.license }, [9] = { 'license:' .. string.rep('c', 40) } }
    function GetCurrentResourceName() return 'zyon_admin_menu' end
    function GetResourceState() return 'missing' end
    function GetResourceMetadata() return nil end
    function GetGameTimer() return f.clock end
    function GetConvar(name, fallback)
        if name == 'zyon_menu_token' then return f.token end
        if name == 'zyon_menu_license' then return f.license end
        if name == 'zyon_menu_public_url' then return f.publicUrl or '' end
        return fallback
    end
    function GetPlayers() return f.players end
    function GetPlayerIdentifiers(player) return f.identifiers[player] or {} end
    function GetPlayerName(player) return 'Player ' .. player end
    function IsPlayerAceAllowed(player, ace) return ace == 'zyon.modmenu' and f.allowed[player] or false end
    function TriggerClientEvent(event, player, ...)
        local effects = event == 'zyon_admin_menu:pairResult' and f.pairResults
            or event == 'zyon_admin_menu:admins' and f.adminResults or f.effects
        effects[#effects + 1] = { event = event, player = player, args = copy({ ... }) }
    end
    function RegisterNetEvent(name, handler) f.handlers[name] = handler end
    function AddEventHandler(name, handler) f.handlers[name] = handler end
    function CreateThread(fn) f.threads[#f.threads + 1] = coroutine.create(fn) end
    function Wait() coroutine.yield() end
    function SetTimeout(delay, fn) f.timeouts[#f.timeouts + 1] = { at = f.clock + delay, fn = fn } end
    function SetHttpHandler(handler) f.http = handler end
    json = {
        encode = function(value) return copy(value) end,
        decode = function(body)
            f.parseCalls = f.parseCalls + 1
            if not f.decoded[body] then error('invalid JSON') end
            return copy(f.decoded[body]), #body + 1, nil
        end
    }
    dofile(serverPath:gsub('/server.lua$', '/medical.lua'))
    dofile(serverPath)
    for _, thread in ipairs(f.threads) do assert(coroutine.resume(thread)) end
    function f:advance(milliseconds)
        self.clock = self.clock + milliseconds
        for _, thread in ipairs(self.threads) do assert(coroutine.resume(thread)) end
        for _, timeout in ipairs(self.timeouts) do
            if not timeout.done and self.clock >= timeout.at then timeout.done = true; timeout.fn() end
        end
    end
    function f:request(path, method, payload, options)
        options = options or {}
        local result = {}
        self.requestId = self.requestId + 1
        local body = options.body
        if payload then
            body = 'fixture-' .. self.requestId
            self.decoded[body] = copy(payload)
        end
        local request = {
            path = path, method = method, address = '127.0.0.1', headers = {
                Authorization = options.authorization or ('Bearer ' .. self.token),
                ['Content-Type'] = options.contentType or 'application/json'
            },
            setDataHandler = function(callback) result.dataHandler = callback end,
            setCancelHandler = function(callback) result.cancelHandler = callback end
        }
        if options.length then request.headers['Content-Length'] = options.length end
        local response = {
            writeHead = function(status, headers)
                assert(not result.status, 'response headers written twice')
                result.status = status; result.headers = headers
            end,
            send = function(value)
                assert(not result.sent, 'response sent twice')
                result.sent = true; result.payload = value; self.responseCalls = self.responseCalls + 1
            end
        }
        self.http(request, response)
        if result.dataHandler and not options.delayed then result.dataHandler(body or '') end
        result.request = request
        result.body = body
        return result
    end
    function f:event(name, player, ...)
        source = player
        self.handlers[name](...)
        source = nil
    end
    function f:status() return self:request('/api/status', 'GET').payload end
    function f:pair(player, nonce, requestId)
        if requestId == nil then self.pairAttemptId = self.pairAttemptId + 1; requestId = self.pairAttemptId end
        local before = #self.pairResults
        self:event('zyon_admin_menu:pairRequest', player or 7, nonce or self.token, requestId)
        if #self.pairResults == before then return nil end
        local result = self.pairResults[#self.pairResults]
        assert(result.args[1].requestId == requestId, 'pair reply lost request correlation')
        return result and result.args[1]
    end
    if options.pair ~= false then assert(f:pair().ok) end
    return f
end

local function settings()
    return {
        aim = { enabled = false, onlyVisible = true, onlyPlayers = true, targetBone = 'Head',
            activationKey = 'RMB', smooth = 1, drawFov = false, fov = 100 },
        silent = { enabled = false, onlyVisible = true, onlyPlayers = true, targetBone = 'Head',
            activationKey = 'RMB', smooth = 1, drawFov = false, fov = 100 },
        weapons = { noRecoil = false, noSpread = false, freezeAmmo = false },
        player = { godMode = true, noclip = false, noclipSpeed = 5, invisible = false, muteVoice = false },
        esp = { enabled = false, boxes = true, skeletons = false, names = true, distance = true,
            onlyPlayers = true, onlyVisible = true, maxDistance = 250, admins = false }
    }
end

local passed = 0
local function test(name, callback)
    local ok, err = pcall(callback)
    if not ok then error(name .. ': ' .. tostring(err), 0) end
    passed = passed + 1
    print('PASS ' .. name)
end

test('authentication happens before JSON decoding or dispatch', function()
    local f = fixture()
    local result = f:request('/api/settings', 'POST', { settings = settings() }, { authorization = 'Bearer bad' })
    assert(result.status == 401 and f.parseCalls == 0 and #f.effects == 0)
    f.token = 'short'
    assert(f:request('/api/status', 'GET').status == 401)
end)

test('legacy static credential configuration is ignored', function()
    local f = fixture()
    local pairedNonce = f.token
    f.token = 'ignored-legacy-token'
    f.license = 'bad-identifier'
    local state = f:request('/api/status', 'GET', nil, { authorization = 'Bearer ' .. pairedNonce }).payload
    assert(state.ready == true and state.playerOnline == true and state.version == '3.1.1')
end)

test('license and ACE checks select only configured online player', function()
    local f = fixture()
    local result = f:request('/api/settings', 'POST', { settings = settings() })
    assert(result.status == 200 and #f.effects == 1 and f.effects[1].player == 7)
    f:advance(1000)
    f.allowed[7] = false
    local denied = f:request('/api/settings', 'POST', { settings = settings() })
    assert(denied.status == 401)
    assert(f.effects[#f.effects].player == 7 and f.effects[#f.effects].args[1].player.godMode == false)
end)

test('exact license binding rejects partial or wrong identifiers', function()
    local f = fixture()
    f.identifiers[7] = { f.license .. '0', 'steam:1234' }
    assert(f:request('/api/settings', 'POST', { settings = settings() }).status == 401)
    assert(#f.effects == 0)
end)

test('ACK only accepts current revision from configured permitted player', function()
    local f = fixture()
    local result = f:request('/api/settings', 'POST', { settings = settings() })
    local revision = result.payload.revision
    f:event('zyon_admin_menu:ack', 9, revision)
    assert(f:status().appliedRevision == 0)
    f:event('zyon_admin_menu:ack', 7, revision - 1)
    assert(f:status().appliedRevision == 0)
    f:event('zyon_admin_menu:ack', 7, revision)
    assert(f:status().appliedRevision == revision)
end)

test('failed client ACK restores defaults without claiming application', function()
    local f = fixture()
    local revision = f:request('/api/settings', 'POST', { settings = settings() }).payload.revision
    f:event('zyon_admin_menu:ack', 7, revision, false, 'Native unavailable')
    local state = f:status()
    assert(state.appliedRevision == 0 and state.desiredRevision > revision)
    assert(f.effects[#f.effects].args[1].player.godMode == false)
end)

test('heartbeat expires and resets modifiers after 15 seconds', function()
    local f = fixture()
    f:request('/api/settings', 'POST', { settings = settings() })
    f:advance(14000)
    assert(f.effects[#f.effects].args[1].player.godMode == true)
    f:advance(1000)
    assert(f.effects[#f.effects].args[1].player.godMode == false)
    assert(f:status().message:find('expirada'))
end)

test('status renews session and periodic applies renew client lease', function()
    local f = fixture()
    f:request('/api/settings', 'POST', { settings = settings() })
    f:advance(10000)
    assert(f:status().playerOnline)
    f:advance(10000)
    assert(f.effects[#f.effects].args[1].player.godMode == true)
    f:advance(5000)
    assert(f.effects[#f.effects].args[1].player.godMode == false)
end)

test('malformed, oversized and misleading MIME bodies are rejected', function()
    local f = fixture()
    assert(f:request('/api/settings', 'POST', nil, { body = '{broken' }).status == 400)
    assert(f:request('/api/settings', 'POST', nil, { body = string.rep('x', 32769) }).status == 413)
    assert(f:request('/api/settings', 'POST', { settings = settings() }, { contentType = 'application/jsonbad' }).status == 415)
    assert(f:request('/api/settings', 'POST', nil, { length = '32769' }).status == 413)
    assert(#f.effects == 0)
end)

test('silent aim rejection is atomic and bounds/unknown fields are enforced', function()
    local f = fixture()
    local value = settings(); value.silent.enabled = true
    assert(f:request('/api/settings', 'POST', { settings = value }).status == 422)
    value = settings(); value.aim.fov = 501
    assert(f:request('/api/settings', 'POST', { settings = value }).status == 422)
    value = settings(); value.player.arbitraryTarget = 9
    assert(f:request('/api/settings', 'POST', { settings = value }).status == 422)
    assert(#f.effects == 0)
end)

test('decoder errors, trailing data and JSON arrays are rejected', function()
    local f = fixture()
    json.decode = function() return {}, 3, 'decoder failure' end
    assert(f:request('/api/settings', 'POST', nil, { body = '{}' }).status == 400)
    json.decode = function() return {}, 3, nil end
    assert(f:request('/api/settings', 'POST', nil, { body = '{} trailing' }).status == 400)
    json.decode = function(body) return setmetatable({}, { __jsontype = 'array' }), #body + 1, nil end
    assert(f:request('/api/revive', 'POST', nil, { body = '[]' }).status == 422)
    assert(#f.effects == 0)
end)

test('endpoint rate limits and allowed methods are bounded', function()
    local f = fixture()
    assert(f:request('/api/status', 'POST', {}).status == 405)
    assert(f:request('/api/console', 'POST', {}).status == 404)
    for _ = 1, 8 do assert(f:request('/api/status', 'GET').status == 200) end
    assert(f:request('/api/status', 'GET').status == 429)
    f:advance(1000)
    assert(f:request('/api/status', 'GET').status == 200)
end)

test('revive tickets reject forgery and permit a single matching ACK', function()
    local f = fixture()
    f:event('zyon_admin_menu:reviveAck', 7, 1, true, 'forged')
    assert(#f:request('/api/events', 'GET').payload.events == 0)
    local result = f:request('/api/revive', 'POST', {})
    assert(result.status == 200)
    local ticket = result.payload.ticket
    f:event('zyon_admin_menu:reviveAck', 9, ticket, true, 'forged')
    f:event('zyon_admin_menu:reviveAck', 7, ticket + 1, true, 'forged')
    f:event('zyon_admin_menu:reviveAck', 7, ticket, true, 'revived')
    local cursor = f:request('/api/events', 'GET').payload.cursor
    f:event('zyon_admin_menu:reviveAck', 7, ticket, true, 'duplicate')
    assert(f:request('/api/events', 'GET').payload.cursor == cursor)
    assert(f.effects[1].event == 'zyon_admin_menu:apply' and f.effects[1].args[2] > 0)
end)

test('credential rotation during pending body prevents execution', function()
    local f = fixture()
    local result = f:request('/api/settings', 'POST', { settings = settings() }, { delayed = true })
    f:advance(2000)
    f.token = string.rep('d', 64)
    assert(f:pair().ok)
    result.dataHandler('ignored')
    assert(result.status == 401 and f.parseCalls == 0 and #f.effects == 0)
end)

test('body timeout and cancellation cannot produce duplicate responses', function()
    local f = fixture()
    local result = f:request('/api/settings', 'POST', { settings = settings() }, { delayed = true })
    f:advance(5000)
    assert(result.status == 408)
    result.dataHandler('ignored')
    assert(#f.effects == 0)
    local canceled = f:request('/api/settings', 'POST', { settings = settings() }, { delayed = true })
    canceled.cancelHandler()
    f:advance(5000)
    canceled.dataHandler('ignored')
    assert(not canceled.status and #f.effects == 0)
end)

test('disconnect and resource shutdown restore active modifiers', function()
    local f = fixture()
    f:request('/api/settings', 'POST', { settings = settings() })
    assert(f:request('/api/disconnect', 'POST', {}).status == 200)
    assert(f.effects[#f.effects].args[1].player.godMode == false)
    f:advance(1000)
    f:request('/api/settings', 'POST', { settings = settings() })
    f:event('onResourceStop', 0, 'zyon_admin_menu')
    assert(f.effects[#f.effects].args[1].player.godMode == false)
end)

test('newer mutation fences settings bodies received in reverse order', function()
    local f = fixture()
    local older = f:request('/api/settings', 'POST', { settings = settings() }, { delayed = true })
    local newerSettings = settings(); newerSettings.player.godMode = false
    local newer = f:request('/api/settings', 'POST', { settings = newerSettings })
    assert(newer.status == 200)
    older.dataHandler(older.body)
    assert(older.status == 409 and #f.effects == 1)
    assert(f.effects[1].args[1].player.godMode == false)
end)

test('disconnect fences older settings, revive and suicide bodies', function()
    for _, path in ipairs({ '/api/settings', '/api/revive', '/api/suicide' }) do
        local f = fixture()
        local payload = path == '/api/settings' and { settings = settings() } or {}
        local pending = f:request(path, 'POST', payload, { delayed = true })
        assert(f:request('/api/disconnect', 'POST', {}).status == 200)
        pending.dataHandler(pending.body)
        assert(pending.status == 409 and #f.effects == 0)
        assert(f:status().sessionActive == false)
    end
end)

test('expiry and permission loss fence pending authenticated mutation', function()
    for _, change in ipairs({ 'expiry', 'permission' }) do
        local f = fixture()
        f:request('/api/settings', 'POST', { settings = settings() })
        local pending = f:request('/api/settings', 'POST', { settings = settings() }, { delayed = true })
        if change == 'expiry' then
            -- Place arrival after lease expiry without firing the HTTP receive timeout first.
            f.clock = 15000
        else f.allowed[7] = false end
        pending.dataHandler(pending.body)
        assert(pending.status == (change == 'permission' and 401 or 409))
        assert(f.effects[#f.effects].args[1].player.godMode == false)
        if change == 'permission' then assert(f:request('/api/status', 'GET').status == 401)
        else assert(f:status().sessionActive == false) end
    end
end)

test('a newer malformed mutation still invalidates an older pending body', function()
    local f = fixture()
    local older = f:request('/api/settings', 'POST', { settings = settings() }, { delayed = true })
    assert(f:request('/api/settings', 'POST', nil, { body = '{broken' }).status == 400)
    older.dataHandler(older.body)
    assert(older.status == 409 and #f.effects == 0)
end)

test('license change and reconnect cannot retarget an older HTTP body', function()
    for _, change in ipairs({ 'license', 'reconnect' }) do
        local f = fixture()
        local pending = f:request('/api/settings', 'POST', { settings = settings() }, { delayed = true })
        if change == 'license' then f.identifiers[7] = { f.identifiers[9][1] }
        else f.players = { '9' }; f.identifiers[9] = { f.license } end
        pending.dataHandler(pending.body)
        assert(pending.status == 401 and #f.effects == 0)
    end
end)

test('an unpaired API cannot use legacy configured credentials', function()
    local f = fixture({ pair = false })
    assert(f:request('/api/status', 'GET').status == 401)
    assert(f:request('/api/settings', 'POST', { settings = settings() }).status == 401)
    assert(f.parseCalls == 0 and #f.effects == 0)
end)

test('pair registration requires actual online actor, ACE and license', function()
    for _, change in ipairs({ 'offline', 'ace', 'license' }) do
        local f = fixture({ pair = false })
        if change == 'offline' then f.players = { '9' }
        elseif change == 'ace' then f.allowed[7] = false
        else f.identifiers[7] = { 'steam:12345', 'license2:' .. string.rep('b', 40) } end
        local result = f:pair()
        assert(result.ok == false and type(result.error) == 'string')
        assert(#f.pairResults == 1 and f.pairResults[1].player == 7)
        assert(f:request('/api/status', 'GET').status == 401 and #f.effects == 0)
    end
end)

test('pair nonce format and bounded error echo reject malformed input', function()
    for _, nonce in ipairs({ string.rep('a', 63), string.rep('a', 65), string.rep('g', 64), 42, {} }) do
        local f = fixture({ pair = false })
        local result = f:pair(7, nonce)
        assert(result.ok == false and #result.nonce <= 64)
        assert(f:request('/api/status', 'GET').status == 401 and #f.effects == 0)
    end
end)

test('valid registration replies only to source with full configured HTTPS endpoint', function()
    local f = fixture({ pair = false })
    f.publicUrl = 'https://admin.example.com/zyon_admin_menu/'
    local result = f:pair()
    assert(result.ok and result.nonce == f.token and result.playerId == 7)
    assert(result.endpoint == f.publicUrl and result.version == '3.1.1')
    assert(#f.pairResults == 1 and f.pairResults[1].player == 7)
    assert(f:status().ready and not f:status().sessionActive)
end)

test('public pairing endpoint rejects HTTP, credentials and URL queries', function()
    for _, endpoint in ipairs({ 'http://admin.example.com/', 'https://u:p@admin.example.com/',
        'https://admin.example.com/?key=value', 'https://admin.example.com/#fragment' }) do
        local f = fixture({ pair = false }); f.publicUrl = endpoint
        assert(f:pair().ok == false and f:request('/api/status', 'GET').status == 401)
    end
end)

test('same actor and nonce renew pairing without resetting active revision', function()
    local f = fixture()
    local revision = f:request('/api/settings', 'POST', { settings = settings() }).payload.revision
    f:event('zyon_admin_menu:ack', 7, revision, true)
    f:advance(3000)
    assert(f:pair().ok)
    local state = f:status()
    assert(state.sessionActive and state.desiredRevision == revision and state.appliedRevision == revision)
    assert(#f.effects == 1)
end)

test('pair requests are limited to one per two seconds for each source', function()
    local f = fixture()
    assert(f:pair().ok == false)
    f:advance(1999)
    assert(f:pair().ok == false)
    f:advance(1)
    assert(f:pair().ok == true)
    assert(f:pair(9, string.rep('d', 64)).ok == true)
end)

test('a different actor cannot replace an active controller', function()
    local f = fixture()
    local revision = f:request('/api/settings', 'POST', { settings = settings() }).payload.revision
    assert(f:pair(9, string.rep('d', 64)).ok == false)
    local state = f:status()
    assert(state.playerId == 7 and state.sessionActive and state.desiredRevision == revision)
    assert(#f.effects == 1)
end)

test('an idle controller can be replaced and old bearer becomes invalid', function()
    local f = fixture()
    local oldToken = f.token
    local newToken = string.rep('d', 64)
    assert(f:pair(9, newToken).ok)
    assert(f:request('/api/status', 'GET', nil, { authorization = 'Bearer ' .. oldToken }).status == 401)
    f.token = newToken
    assert(f:status().playerId == 9 and not f:status().sessionActive)
    assert(#f.effects == 0)
end)

test('the same credential cannot silently retarget an idle pair to another actor', function()
    local f = fixture()
    assert(f:pair(9, f.token).ok == false)
    assert(f:status().playerId == 7)
end)

test('nonce rotation disables prior effects and fences old body and ACK', function()
    local f = fixture()
    local oldToken = f.token
    local revision = f:request('/api/settings', 'POST', { settings = settings() }).payload.revision
    local pending = f:request('/api/revive', 'POST', {}, { delayed = true })
    f:advance(2000)
    f.token = string.rep('d', 64)
    assert(f:pair().ok)
    pending.dataHandler(pending.body)
    assert(pending.status == 401 and f.effects[#f.effects].args[1].player.godMode == false)
    f:event('zyon_admin_menu:ack', 7, revision, true)
    local state = f:status()
    assert(not state.sessionActive and state.appliedRevision == 0 and state.desiredRevision > revision)
    assert(f:request('/api/status', 'GET', nil, { authorization = 'Bearer ' .. oldToken }).status == 401)
end)

test('HTTP traffic cannot extend automatic pairing past 120 seconds', function()
    local f = fixture()
    f:advance(119000)
    assert(f:request('/api/status', 'GET').status == 200)
    f:advance(1000)
    assert(f:request('/api/status', 'GET').status == 401)
    f.token = string.rep('d', 64)
    assert(f:pair().ok and f:status().ready)
end)

test('pair identity changes, permission removal and player drop revoke bearer', function()
    for _, change in ipairs({ 'identity', 'ace', 'drop' }) do
        local f = fixture()
        f:request('/api/settings', 'POST', { settings = settings() })
        if change == 'identity' then f.identifiers[7] = { f.identifiers[9][1] }
        elseif change == 'ace' then f.allowed[7] = false
        else f.players = { '9' }; f:event('playerDropped', 7) end
        assert(f:request('/api/status', 'GET').status == 401)
        if change ~= 'drop' then assert(f.effects[#f.effects].args[1].player.godMode == false) end
        assert(f:pair(9, string.rep('d', 64)).ok)
    end
end)

test('pair attempt identifiers must be positive safe integers and invalid ids produce no reply', function()
    local f = fixture({ pair = false })
    f:event('zyon_admin_menu:pairRequest', 7, f.token)
    for _, requestId in ipairs({ 0, -1, 1.5, 9007199254740992, '1', false, {}, math.huge, 0 / 0 }) do
        f:event('zyon_admin_menu:pairRequest', 7, f.token, requestId)
    end
    assert(#f.pairResults == 0 and #f.effects == 0)
    assert(f:request('/api/status', 'GET').status == 401)
    local result = f:pair(7, f.token, 9007199254740991)
    assert(result.ok and result.requestId == 9007199254740991)
end)

test('all denial paths preserve the corresponding attempt identifier', function()
    local f = fixture({ pair = false })
    local malformed = f:pair(7, 'bad', 101)
    assert(not malformed.ok and malformed.requestId == 101)
    local rateLimited = f:pair(7, f.token, 102)
    assert(not rateLimited.ok and rateLimited.requestId == 102)
    f:advance(2000)
    f.allowed[7] = false
    local noPermission = f:pair(7, f.token, 103)
    assert(not noPermission.ok and noPermission.requestId == 103)
    f:advance(2000)
    f.allowed[7] = true; f.publicUrl = 'http://remote.example.com/'
    local noTls = f:pair(7, f.token, 104)
    assert(not noTls.ok and noTls.requestId == 104)
end)

test('dropped or reordered pair replies retain their original attempt ids', function()
    local f = fixture({ pair = false })
    local oldResult = copy(f:pair(7, f.token, 11))
    f:advance(9000)
    local current = f:pair(7, f.token, 12)
    assert(oldResult.ok and current.ok and oldResult.nonce == current.nonce)
    assert(oldResult.requestId == 11 and current.requestId == 12)
    -- Simulated network delivery in reverse order must retain the old identity.
    local delivered = { current, oldResult }
    assert(delivered[1].requestId == 12 and delivered[2].requestId == 11)
    assert(f:status().desiredRevision == 0 and #f.effects == 0)
end)

test('new player and ESP settings use a complete strict snapshot', function()
    local f = fixture()
    f:request('/api/settings', 'POST', { settings = settings() })
    f:event('zyon_admin_menu:clientCapabilities', 7, { muteVoice = true })
    f:advance(1000)
    local value = settings()
    value.player.noclip, value.player.noclipSpeed = true, 27
    value.player.invisible, value.player.muteVoice, value.esp.admins = true, true, true
    local response = f:request('/api/settings', 'POST', { settings = value })
    assert(response.status == 200)
    local applied = f.effects[#f.effects].args[1]
    assert(applied.player.noclipSpeed == 27 and applied.player.invisible == true
        and applied.player.muteVoice == true and applied.esp.admins == true)
    for _, field in ipairs({ 'noclipSpeed', 'invisible', 'muteVoice' }) do
        local fresh = fixture(); local missing = settings(); missing.player[field] = nil
        assert(fresh:request('/api/settings', 'POST', { settings = missing }).status == 422)
        assert(#fresh.effects == 0)
    end
    local fresh = fixture(); local missing = settings(); missing.esp.admins = nil
    assert(fresh:request('/api/settings', 'POST', { settings = missing }).status == 422)
    assert(#fresh.effects == 0)
end)

test('noclip speed accepts only integer values between one and fifty', function()
    for _, speed in ipairs({ 1, 5, 50 }) do
        local f = fixture(); local value = settings(); value.player.noclipSpeed = speed
        assert(f:request('/api/settings', 'POST', { settings = value }).status == 200)
        assert(f.effects[1].args[1].player.noclipSpeed == speed)
    end
    for _, speed in ipairs({ 0, -1, 51, 5.5, math.huge, -math.huge, 0 / 0, '5', false, {} }) do
        local f = fixture(); local value = settings(); value.player.noclipSpeed = speed
        assert(f:request('/api/settings', 'POST', { settings = value }).status == 422)
        assert(#f.effects == 0)
    end
end)

test('invisibility mute and administrator display reject nonboolean values atomically', function()
    for _, field in ipairs({ 'invisible', 'muteVoice', 'admins' }) do
        for _, wrong in ipairs({ 'true', 1, {} }) do
            local f = fixture(); local value = settings()
            if field == 'admins' then value.esp[field] = wrong else value.player[field] = wrong end
            assert(f:request('/api/settings', 'POST', { settings = value }).status == 422)
            assert(#f.effects == 0 and #f.adminResults == 0)
        end
    end
end)

test('suicide is authenticated and cannot select another player', function()
    local f = fixture()
    assert(f:request('/api/suicide', 'POST', {}, { authorization = 'Bearer bad' }).status == 401)
    assert(f.parseCalls == 0 and #f.effects == 0)
    assert(f:request('/api/suicide', 'POST', { playerId = 9 }).status == 422)
    assert(#f.effects == 0)
    f:advance(2000)
    local response = f:request('/api/suicide', 'POST', {})
    assert(response.status == 200 and type(response.payload.ticket) == 'number')
    local suicide = f.effects[#f.effects]
    assert(suicide.event == 'zyon_admin_menu:suicide' and suicide.player == 7
        and suicide.args[1] == response.payload.ticket)
end)

test('suicide disables GodMode before dispatch and its ticket accepts one bound ACK', function()
    local f = fixture()
    f:request('/api/settings', 'POST', { settings = settings() })
    f:event('zyon_admin_menu:suicideAck', 7, 1, true, 'forged')
    local response = f:request('/api/suicide', 'POST', {})
    assert(response.status == 200)
    local ticket = response.payload.ticket
    assert(f.effects[#f.effects - 1].event == 'zyon_admin_menu:apply')
    assert(f.effects[#f.effects - 1].args[1].player.godMode == false)
    assert(f.effects[#f.effects].event == 'zyon_admin_menu:suicide')
    local cursor = f:request('/api/events', 'GET').payload.cursor
    f:event('zyon_admin_menu:suicideAck', 9, ticket, true, 'forged')
    f:event('zyon_admin_menu:suicideAck', 7, ticket + 1, true, 'forged')
    assert(f:request('/api/events', 'GET').payload.cursor == cursor)
    f:event('zyon_admin_menu:suicideAck', 7, ticket, true, 'dead')
    local acknowledged = f:request('/api/events', 'GET').payload.cursor
    assert(acknowledged > cursor)
    f:event('zyon_admin_menu:suicideAck', 7, ticket, true, 'duplicate')
    assert(f:request('/api/events', 'GET').payload.cursor == acknowledged)
end)

test('suicide ACK loses authority after permission removal and token rotation', function()
    for _, change in ipairs({ 'permission', 'rotation' }) do
        local f = fixture()
        local ticket = f:request('/api/suicide', 'POST', {}).payload.ticket
        if change == 'permission' then f.allowed[7] = false
        else f:advance(2000); f.token = string.rep('d', 64); assert(f:pair().ok) end
        f:event('zyon_admin_menu:suicideAck', 7, ticket, true, 'stale acknowledgement')
        if change == 'permission' then assert(f:request('/api/status', 'GET').status == 401)
        else
            local events = f:request('/api/events', 'GET').payload.events
            for _, event in ipairs(events) do assert(event.message ~= 'stale acknowledgement') end
        end
    end
end)

test('administrator IDs are disclosed only to the paired actor on explicit request', function()
    local f = fixture()
    assert(#f.adminResults == 0)
    f.players = { '7', '9', '12' }; f.allowed[12] = false
    local value = settings(); value.esp.enabled, value.esp.admins = true, true
    assert(f:request('/api/settings', 'POST', { settings = value }).status == 200)
    local result = f.adminResults[#f.adminResults]
    assert(result and result.player == 7)
    local permitted = {}; for _, id in ipairs(result.args[1]) do permitted[id] = true end
    assert(permitted[7] and permitted[9] and not permitted[12])
    for _, event in ipairs(f.adminResults) do assert(event.player == 7 and event.player ~= -1) end
end)

test('administrator list follows live ACE and online state and clears on disable', function()
    local f = fixture(); local value = settings(); value.esp.enabled, value.esp.admins = true, true
    f:request('/api/settings', 'POST', { settings = value })
    f.allowed[9] = false; f:advance(5000)
    local latest = f.adminResults[#f.adminResults]
    for _, id in ipairs(latest.args[1]) do assert(id ~= 9) end
    f.allowed[9] = true; f.players = { '7' }; f:advance(5000)
    latest = f.adminResults[#f.adminResults]
    for _, id in ipairs(latest.args[1]) do assert(id ~= 9) end
    value.esp.admins = false; f:request('/api/settings', 'POST', { settings = value })
    assert(#f.adminResults[#f.adminResults].args[1] == 0)
end)

test('administrator lists clear with session expiry disconnect and permission revocation', function()
    for _, reason in ipairs({ 'expiry', 'disconnect', 'permission' }) do
        local f = fixture(); local value = settings(); value.esp.admins = true
        f:request('/api/settings', 'POST', { settings = value })
        assert(#f.adminResults[#f.adminResults].args[1] > 0)
        if reason == 'expiry' then f:advance(15000)
        elseif reason == 'disconnect' then assert(f:request('/api/disconnect', 'POST', {}).status == 200)
        else f.allowed[7] = false; assert(f:request('/api/status', 'GET').status == 401) end
        assert(#f.adminResults[#f.adminResults].args[1] == 0)
        for _, result in ipairs(f.adminResults) do assert(result.player == 7) end
    end
end)

local function hasCapability(state, name)
    for _, item in ipairs(state.capabilities) do if item == name then return true end end
    return false
end

test('voice capability requires a valid native report from the active bound actor', function()
    local f = fixture()
    assert(not hasCapability(f:status(), 'player.muteVoice'))
    f:event('zyon_admin_menu:clientCapabilities', 7, { muteVoice = true })
    assert(not hasCapability(f:status(), 'player.muteVoice'))
    f:request('/api/settings', 'POST', { settings = settings() })
    f:event('zyon_admin_menu:clientCapabilities', 9, { muteVoice = true })
    assert(not hasCapability(f:status(), 'player.muteVoice'))
    f:event('zyon_admin_menu:clientCapabilities', 7, { muteVoice = 'true' })
    assert(not hasCapability(f:status(), 'player.muteVoice'))
    f:event('zyon_admin_menu:clientCapabilities', 7, { muteVoice = true })
    assert(hasCapability(f:status(), 'player.muteVoice'))
    f:advance(1000)
    f:event('zyon_admin_menu:clientCapabilities', 7, { muteVoice = false })
    assert(not hasCapability(f:status(), 'player.muteVoice'))
end)

test('voice capability report cannot outlive its paired identity', function()
    local f = fixture()
    f:request('/api/settings', 'POST', { settings = settings() })
    f:event('zyon_admin_menu:clientCapabilities', 7, { muteVoice = true })
    assert(hasCapability(f:status(), 'player.muteVoice'))
    f:advance(2000)
    f.token = string.rep('d', 64)
    assert(f:pair().ok)
    assert(not hasCapability(f:status(), 'player.muteVoice'))
end)

test('pending revive and suicide receipts prevent overlapping actions until ACK', function()
    for _, firstPath in ipairs({ '/api/revive', '/api/suicide' }) do
        local f = fixture()
        local secondPath = firstPath == '/api/revive' and '/api/suicide' or '/api/revive'
        local first = f:request(firstPath, 'POST', {})
        assert(first.status == 200)
        local beforeEffects = #f.effects
        assert(f:request(secondPath, 'POST', {}).status == 409)
        assert(#f.effects == beforeEffects)
        f:advance(2000)
        beforeEffects = #f.effects
        assert(f:request(firstPath, 'POST', {}).status == 409)
        assert(#f.effects == beforeEffects)
        f:event(firstPath == '/api/revive' and 'zyon_admin_menu:reviveAck' or 'zyon_admin_menu:suicideAck',
            7, first.payload.ticket, true, 'first action confirmed')
        assert(f:request(secondPath, 'POST', {}).status == 200)
        assert(f.effects[#f.effects].event == (secondPath == '/api/revive' and 'zyon_admin_menu:revive' or 'zyon_admin_menu:suicide'))
    end
end)

test('action receive timeout releases pending receipt and rejects the late ACK', function()
    for _, firstPath in ipairs({ '/api/revive', '/api/suicide' }) do
        local f = fixture()
        local first = f:request(firstPath, 'POST', {})
        assert(first.status == 200)
        f:advance(10000)
        local cursor = f:request('/api/events', 'GET').payload.cursor
        f:event(firstPath == '/api/revive' and 'zyon_admin_menu:reviveAck' or 'zyon_admin_menu:suicideAck',
            7, first.payload.ticket, true, 'late action must not acknowledge')
        assert(f:request('/api/events', 'GET').payload.cursor == cursor)
        local secondPath = firstPath == '/api/revive' and '/api/suicide' or '/api/revive'
        assert(f:request(secondPath, 'POST', {}).status == 200)
    end
end)

test('voice mute cannot be requested without confirmed native support', function()
    local f = fixture(); local value = settings(); value.player.muteVoice = true
    assert(f:request('/api/settings', 'POST', { settings = value }).status == 422)
    assert(#f.effects == 0)
end)

test('native action events bind their ticket to the current desired revision', function()
    local f = fixture()
    local revive = f:request('/api/revive', 'POST', {})
    assert(revive.status == 200)
    local effect = f.effects[#f.effects]
    assert(effect.event == 'zyon_admin_menu:revive' and effect.args[1] == revive.payload.ticket
        and effect.args[2] == f:status().desiredRevision)
    f:event('zyon_admin_menu:reviveAck', 7, revive.payload.ticket, true, 'revived')
    local suicide = f:request('/api/suicide', 'POST', {})
    assert(suicide.status == 200)
    effect = f.effects[#f.effects]
    assert(effect.event == 'zyon_admin_menu:suicide' and effect.args[1] == suicide.payload.ticket
        and effect.args[2] == f:status().desiredRevision)
    assert(f.effects[#f.effects - 1].args[2] == effect.args[2])
    assert(f.effects[#f.effects - 1].args[1].player.godMode == false
        and f.effects[#f.effects - 1].args[1].player.noclip == false)
end)

test('live speed changes every sixty milliseconds remain within the supported settings rate', function()
    local f = fixture()
    for speed = 1, 40 do
        local value = settings(); value.player.noclip, value.player.noclipSpeed = true, speed
        local response = f:request('/api/settings', 'POST', { settings = value })
        assert(response.status == 200 and f.effects[#f.effects].args[1].player.noclipSpeed == speed)
        f:advance(60)
    end
    assert(f.clock > 2000 and #f.effects == 40)
end)

test('settings burst abuse remains bounded and cannot change the last accepted snapshot', function()
    local f = fixture()
    for speed = 1, 20 do
        local value = settings(); value.player.noclipSpeed = speed
        assert(f:request('/api/settings', 'POST', { settings = value }).status == 200)
    end
    local value = settings(); value.player.noclipSpeed = 50
    assert(f:request('/api/settings', 'POST', { settings = value }).status == 429)
    assert(#f.effects == 20 and f.effects[#f.effects].args[1].player.noclipSpeed == 20)
end)

print(string.format('%d server contract checks passed', passed))
