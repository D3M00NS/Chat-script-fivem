-- Lua 5.4 / server.lua / FiveM FXServer / explicitly authorized administrative resource.
local RESOURCE = GetCurrentResourceName()
local M = ZyonMedical
local medicalEpoch = 0
local VERSION = '3.1.1'
local ACE = 'zyon.modmenu'
local MAX_BODY = 32768
local LEASE_MS = 15000
local MAX_EVENTS = 100
local PAIR_LEASE_MS = 120000
local MAX_PAIR_ATTEMPTS = 256
local CAPABILITIES = {
    'aim', 'aim.playersOnly', 'aim.visibility',
    'weapons.noRecoil', 'weapons.noSpread', 'weapons.freezeAmmo',
    'player.godMode', 'player.revive', 'player.suicide', 'player.noclip', 'player.invisible',
    'esp', 'esp.boxes', 'esp.skeletons', 'esp.names', 'esp.distance',
    'esp.playersOnly', 'esp.visibility', 'esp.admins'
}

local function defaults()
    return {
        aim = { enabled = false, onlyVisible = true, onlyPlayers = true, targetBone = 'Head',
            activationKey = 'RMB', smooth = 1, drawFov = false, fov = 100 },
        silent = { enabled = false, onlyVisible = true, onlyPlayers = true, targetBone = 'Head',
            activationKey = 'RMB', smooth = 1, drawFov = false, fov = 100 },
        weapons = { noRecoil = false, noSpread = false, freezeAmmo = false },
        player = { godMode = false, noclip = false, noclipSpeed = 5, invisible = false, muteVoice = false },
        esp = { enabled = false, boxes = true, skeletons = false, names = true, distance = true,
            onlyPlayers = true, onlyVisible = true, maxDistance = 250, admins = false }
    }
end

local state = {
    player = nil, active = false, desired = defaults(), desiredRevision = 0,
    appliedRevision = 0, heartbeat = nil, lastDispatch = nil, reviveTicket = 0,
    pendingRevive = nil, suicideTicket = 0, pendingSuicide = nil,
    clientCapabilities = {}, cursor = 0, events = {}, buckets = {}, mutationGeneration = 0,
    pair = nil, pairAttempts = {},
    message = 'Abra o programa e entre no servidor com a permissão zyon.modmenu.'
}

local function now()
    return GetGameTimer() % 4294967296
end

local function elapsed(current, previous)
    return (current - previous) % 4294967296
end

local function addEvent(kind, message)
    state.cursor = state.cursor + 1
    state.events[#state.events + 1] = { id = state.cursor, type = kind, message = message }
    if #state.events > MAX_EVENTS then table.remove(state.events, 1) end
    state.message = message
end

local function selectedMedicalProvider()
    local ok, provider, variant, message = pcall(M.select, GetConvar('zyon_menu_medical_provider', 'auto'))
    if not ok then return nil, nil, 'Não foi possível consultar a integração médica; reviver bloqueado.' end
    return provider, variant, message
end

local function serverMedicalAlive(provider, variant, player)
    local ok, alive = pcall(function()
        if provider == 'esx' and variant == 'esx_death' then
            local data = exports['esx_death']:GetDeathState(player)
            if type(data) ~= 'table' or data.ok ~= true or type(data.dead) ~= 'boolean' then return nil end
            return data.dead == false
        elseif provider == 'qbcore' then
            local direct, patient = pcall(function() return exports['qb-core']:GetPlayer(player) end)
            if not direct or not patient then
                local core = exports['qb-core']:GetCoreObject()
                patient = core.Functions.GetPlayer(player)
            end
            local metadata = patient and patient.PlayerData and patient.PlayerData.metadata
            if type(metadata) ~= 'table' or type(metadata.isdead) ~= 'boolean'
                or type(metadata.inlaststand) ~= 'boolean' then return nil end
            return metadata.isdead == false and metadata.inlaststand == false
        end
    end)
    if not ok or alive == nil then
        return nil, 'A vida nativa foi observada, mas o servidor não consegue confirmar o estado médico oficial.'
    end
    return alive
end

local function playerIdentity(player)
    local online = false
    for _, candidate in ipairs(GetPlayers()) do
        if tonumber(candidate) == player then online = true; break end
    end
    if not online then return nil, 'O jogador está desconectado.' end
    if not IsPlayerAceAllowed(player, ACE) then
        return nil, 'Sua conta não possui a permissão ACE zyon.modmenu.'
    end
    for _, identifier in ipairs(GetPlayerIdentifiers(player)) do
        if type(identifier) == 'string' then
            local license = identifier:lower()
            if #license == 48 and license:match('^license:%x+$') then return license end
        end
    end
    return nil, 'O servidor não encontrou um identificador license: válido para sua conta.'
end

local function clearSession(message, notify)
    local prior = state.player
    state.mutationGeneration = state.mutationGeneration + 1
    state.desiredRevision = state.desiredRevision + 1
    state.desired = defaults()
    state.appliedRevision = 0
    state.player = nil
    state.active = false
    state.heartbeat = nil
    state.lastDispatch = nil
    state.pendingRevive = nil
    state.pendingSuicide = nil
    if notify ~= false and prior then
        TriggerClientEvent('zyon_admin_menu:apply', prior, state.desired, state.desiredRevision)
        TriggerClientEvent('zyon_admin_menu:admins', prior, {})
    end
    addEvent('session', message)
end

local function revokePair(message, notify)
    if not state.pair then return end
    state.pair = nil
    state.clientCapabilities = {}
    clearSession(message, notify)
end

local function boundPlayer()
    local pair = state.pair
    if not pair then return nil, 'O programa ainda não foi pareado com um administrador no FiveM.' end
    local license, reason = playerIdentity(pair.player)
    if not license then revokePair(reason, true); return nil, reason end
    if license ~= pair.license then
        reason = 'A identidade do jogador mudou; pareamento encerrado.'
        revokePair(reason, true); return nil, reason
    end
    if elapsed(now(), pair.lastSeen) >= PAIR_LEASE_MS then
        reason = 'Pareamento automático expirado; abra o programa para reconectar.'
        revokePair(reason, true); return nil, reason
    end
    return pair.player
end

local function sendAdminRoster(target)
    if not target or not state.active or state.player ~= target or not state.desired.esp.admins then
        if target then TriggerClientEvent('zyon_admin_menu:admins', target, {}) end
        return
    end
    -- Status comes from the server permission system and is sent only to the
    -- paired administrator. Never broadcast or accept client-supplied labels.
    local admins = {}
    for _, candidate in ipairs(GetPlayers()) do
        local player = tonumber(candidate)
        if player and IsPlayerAceAllowed(player, ACE) then admins[#admins + 1] = player end
    end
    TriggerClientEvent('zyon_admin_menu:admins', target, admins)
end

local function validateSession()
    local selected, reason = boundPlayer()
    if not state.active then return end
    if selected ~= state.player then
        clearSession(reason or 'O jogador autorizado foi alterado.', true)
    elseif not state.heartbeat or elapsed(now(), state.heartbeat) >= LEASE_MS then
        clearSession('Sessão expirada; desativação solicitada ao cliente.', true)
    end
end

local function reviveIsCurrent(pending)
    validateSession()
    if state.pendingRevive ~= pending or not state.active or state.player ~= pending.player
        or boundPlayer() ~= pending.player or pending.revision ~= state.desiredRevision
        or pending.medicalEpoch ~= medicalEpoch or elapsed(now(), pending.sent) >= 10000 then return false end
    local provider, variant = selectedMedicalProvider()
    return provider == pending.provider and variant == pending.variant
end

local function reviveError(pending, message)
    if state.pendingRevive == pending then
        state.pendingRevive = nil
        addEvent('error', message)
    end
end

local function dispatchRevive(pending)
    if not reviveIsCurrent(pending) then
        reviveError(pending, 'Reviver cancelado: a sessão, revisão ou integração médica mudou.'); return
    end
    if pending.provider ~= 'esx' or pending.variant ~= 'esx_death' then
        TriggerClientEvent('zyon_admin_menu:revive', pending.player, pending.ticket, pending.revision,
            pending.provider, pending.variant, math.min(8000, 10000 - elapsed(now(), pending.sent)))
        return
    end
    -- ESX 1.16 owns death and persistence in esx_death. Its protected client
    -- recover event may only originate at the server. Do not call legacy revive
    -- as well, and do not imitate the framework's SQL or medical flags.
    CreateThread(function()
        local ok, message = pcall(function()
            if not reviveIsCurrent(pending) then error('A sessão médica mudou antes de reviver.', 0) end
            local alive, reason = serverMedicalAlive('esx', 'esx_death', pending.player)
            if not reviveIsCurrent(pending) then error('A sessão médica mudou durante a consulta do hospital.', 0) end
            if alive == nil then error(reason, 0) end
            if not alive then
                local revived = exports['esx_death']:Revive(pending.player, 'admin')
                if not revived then error('esx_death recusou reviver; estado ou persistência médicos indisponíveis.', 0) end
            end
            if not reviveIsCurrent(pending) then error('O pedido expirou ou a integração médica mudou durante a ação.', 0) end
            TriggerClientEvent('zyon_admin_menu:revive', pending.player, pending.ticket, pending.revision,
                pending.provider, pending.variant, math.min(8000, 10000 - elapsed(now(), pending.sent)))
        end)
        if not ok then reviveError(pending, tostring(message):gsub('[%c]', ''):sub(1, 120)) end
    end)
end

local function constantEqual(left, right)
    local difference = #left ~ #right
    for i = 1, math.max(#left, #right) do
        difference = difference | ((left:byte(i) or 0) ~ (right:byte(i) or 0))
    end
    return difference == 0
end

local function header(request, wanted)
    for name, value in pairs(request.headers or {}) do
        if type(name) == 'string' and name:lower() == wanted then
            if type(value) == 'string' then return value end
            return nil
        end
    end
    return nil
end

local function authenticated(request)
    if not boundPlayer() or not state.pair then return false end
    local authorization = header(request, 'authorization')
    if not authorization or #authorization > 519 then return false end
    local supplied = authorization:match('^Bearer ([^%s]+)$')
    return supplied and constantEqual(supplied, state.pair.token) or false
end

local function pairResult(player, nonce, requestId, ok, errorMessage)
    local payload = { ok = ok, nonce = type(nonce) == 'string' and #nonce <= 64 and nonce or '',
        requestId = requestId, version = VERSION }
    if ok then
        payload.endpoint = GetConvar('zyon_menu_public_url', '')
        payload.playerId = player
        payload.playerName = GetPlayerName(player) or ''
    else payload.error = errorMessage end
    TriggerClientEvent('zyon_admin_menu:pairResult', player, payload)
end

local function allowPairRequest(player)
    local timestamp, count, oldest, oldestAge = now(), 0, nil, -1
    for candidate, last in pairs(state.pairAttempts) do
        local age = elapsed(timestamp, last)
        if age >= PAIR_LEASE_MS then state.pairAttempts[candidate] = nil
        else
            count = count + 1
            if age > oldestAge then oldest, oldestAge = candidate, age end
        end
    end
    local prior = state.pairAttempts[player]
    if prior and elapsed(timestamp, prior) < 2000 then return false end
    if not prior and count >= MAX_PAIR_ATTEMPTS and oldest then state.pairAttempts[oldest] = nil end
    state.pairAttempts[player] = timestamp
    return true
end

RegisterNetEvent('zyon_admin_menu:pairRequest', function(nonce, requestId)
    if type(requestId) ~= 'number' or requestId < 1 or requestId > 9007199254740991
        or requestId ~= math.floor(requestId) then return end
    local player = tonumber(source)
    if not player or player <= 0 or player ~= math.floor(player) then return end
    if not allowPairRequest(player) then
        pairResult(player, nonce, requestId, false, 'Aguarde dois segundos antes de renovar o pareamento.'); return
    end
    if type(nonce) ~= 'string' or #nonce ~= 64 or not nonce:match('^%x+$') then
        pairResult(player, nonce, requestId, false, 'O pareamento recebeu um identificador automático inválido.'); return
    end
    local license, reason = playerIdentity(player)
    if not license then pairResult(player, nonce, requestId, false, reason); return end
    local endpoint = GetConvar('zyon_menu_public_url', '')
    if endpoint ~= '' then
        local authority = endpoint:match('^https://([^/?#]+)[^?#]*$')
        if #endpoint > 2048 or not authority or authority:find('@', 1, true)
            or endpoint:find('%s') or endpoint:find('%c') then
            pairResult(player, nonce, requestId, false, 'zyon_menu_public_url deve ser uma URL HTTPS sem credenciais ou consulta.'); return
        end
    end
    validateSession()
    local pair = state.pair
    if pair and pair.player == player and pair.license == license and constantEqual(pair.token, nonce) then
        pair.lastSeen = now()
        pairResult(player, nonce, requestId, true)
        return
    end
    if pair and pair.player ~= player and state.active then
        pairResult(player, nonce, requestId, false, 'Outro administrador já controla uma sessão ativa.'); return
    end
    if pair and pair.player ~= player and constantEqual(pair.token, nonce) then
        pairResult(player, nonce, requestId, false, 'O novo pareamento deve gerar outro identificador automático.'); return
    end
    if pair then clearSession('Novo pareamento aceito; desativação da sessão anterior solicitada.', true)
    else state.mutationGeneration = state.mutationGeneration + 1 end
    state.pair = { player = player, license = license, token = nonce, lastSeen = now() }
    state.clientCapabilities = {}
    state.buckets = {}
    state.message = 'Pareamento automático autorizado; ative as funções pelo programa.'
    pairResult(player, nonce, requestId, true)
end)

local rateLimits = {
    ['/api/status'] = { burst = 8, perSecond = 4 },
    ['/api/events'] = { burst = 4, perSecond = 2 },
    ['/api/settings'] = { burst = 20, perSecond = 20 },
    ['/api/revive'] = { burst = 1, perSecond = 0.5 },
    ['/api/suicide'] = { burst = 1, perSecond = 0.5 },
    ['/api/disconnect'] = { burst = 2, perSecond = 1 }
}

local function allowRequest(path)
    local limit = rateLimits[path]
    if not limit then return true end
    local timestamp = now()
    local bucket = state.buckets[path]
    if not bucket then bucket = { tokens = limit.burst, updated = timestamp }; state.buckets[path] = bucket end
    bucket.tokens = math.min(limit.burst, bucket.tokens + elapsed(timestamp, bucket.updated) * limit.perSecond / 1000)
    bucket.updated = timestamp
    if bucket.tokens < 1 then return false end
    bucket.tokens = bucket.tokens - 1
    return true
end

local function respond(response, status, payload, extra)
    local headers = {
        ['Content-Type'] = 'application/json; charset=utf-8',
        ['Cache-Control'] = 'no-store', ['X-Content-Type-Options'] = 'nosniff'
    }
    for key, value in pairs(extra or {}) do headers[key] = value end
    response.writeHead(status, headers)
    response.send(json.encode(payload))
end

local function fail(response, status, message, extra)
    respond(response, status, { message = message }, extra)
end

local function schemaObject(value, keys, name)
    if type(value) ~= 'table' then return nil, name .. ' deve ser um objeto.' end
    local meta = getmetatable(value)
    if type(meta) == 'table' and meta.__jsontype == 'array' then return nil, name .. ' deve ser um objeto.' end
    local expected = {}
    for _, key in ipairs(keys) do expected[key] = true end
    for key in pairs(value) do
        if not expected[key] then return nil, name .. ' contém campo desconhecido.' end
    end
    for _, key in ipairs(keys) do
        if value[key] == nil then return nil, name .. '.' .. key .. ' é obrigatório.' end
    end
    return true
end

local function booleanFields(value, keys, name)
    for _, key in ipairs(keys) do
        if type(value[key]) ~= 'boolean' then return nil, name .. '.' .. key .. ' deve ser booleano.' end
    end
    return true
end

local function integer(value, low, high)
    return type(value) == 'number' and value == math.floor(value) and value >= low and value <= high
end

local function validateSettings(settings)
    local valid, message = schemaObject(settings, { 'aim', 'silent', 'weapons', 'player', 'esp' }, 'settings')
    if not valid then return nil, message end
    for _, name in ipairs({ 'aim', 'silent' }) do
        local item = settings[name]
        valid, message = schemaObject(item, { 'enabled', 'onlyVisible', 'onlyPlayers', 'targetBone',
            'activationKey', 'smooth', 'drawFov', 'fov' }, name)
        if not valid then return nil, message end
        valid, message = booleanFields(item, { 'enabled', 'onlyVisible', 'onlyPlayers', 'drawFov' }, name)
        if not valid then return nil, message end
        if item.targetBone ~= 'Head' and item.targetBone ~= 'Neck' and item.targetBone ~= 'Chest' then
            return nil, name .. '.targetBone deve ser Head, Neck ou Chest.'
        end
        if item.activationKey ~= 'RMB' and item.activationKey ~= 'LMB' and item.activationKey ~= 'Alt'
            and item.activationKey ~= 'Shift' and item.activationKey ~= 'Ctrl' then
            return nil, name .. '.activationKey inválida.'
        end
        if not integer(item.smooth, 1, 20) or not integer(item.fov, 10, 500) then
            return nil, name .. '.smooth ou fov fora dos limites.'
        end
    end
    valid, message = schemaObject(settings.weapons, { 'noRecoil', 'noSpread', 'freezeAmmo' }, 'weapons')
    if not valid then return nil, message end
    valid, message = booleanFields(settings.weapons, { 'noRecoil', 'noSpread', 'freezeAmmo' }, 'weapons')
    if not valid then return nil, message end
    valid, message = schemaObject(settings.player, { 'godMode', 'noclip', 'noclipSpeed', 'invisible', 'muteVoice' }, 'player')
    if not valid then return nil, message end
    valid, message = booleanFields(settings.player, { 'godMode', 'noclip', 'invisible', 'muteVoice' }, 'player')
    if not valid then return nil, message end
    if not integer(settings.player.noclipSpeed, 1, 50) then return nil, 'player.noclipSpeed fora dos limites.' end
    if settings.player.muteVoice and not state.clientCapabilities.muteVoice then
        return nil, 'Silenciar áudio recebido não é suportado pelo cliente conectado.'
    end
    valid, message = schemaObject(settings.esp, { 'enabled', 'boxes', 'skeletons', 'names', 'distance',
        'onlyPlayers', 'onlyVisible', 'maxDistance', 'admins' }, 'esp')
    if not valid then return nil, message end
    valid, message = booleanFields(settings.esp, { 'enabled', 'boxes', 'skeletons', 'names', 'distance',
        'onlyPlayers', 'onlyVisible', 'admins' }, 'esp')
    if not valid then return nil, message end
    if not integer(settings.esp.maxDistance, 25, 1000) then return nil, 'esp.maxDistance fora dos limites.' end
    if settings.silent.enabled then return nil, 'Silent Aim não é suportado por este recurso.' end
    return settings
end

local function selectForCommand(response)
    validateSession()
    local target, reason = boundPlayer()
    if not target then fail(response, 409, reason); return nil end
    if state.player and state.player ~= target then clearSession('O jogador autorizado foi alterado.', true) end
    state.player = target
    state.active = true
    state.heartbeat = now()
    return target
end

local function withJson(request, response, callback)
    -- A newer authenticated mutation invalidates earlier bodies still arriving over HTTP.
    state.mutationGeneration = state.mutationGeneration + 1
    local generation = state.mutationGeneration
    local acceptedLicense = state.pair and state.pair.license
    local acceptedPlayer = boundPlayer()
    local contentLength = header(request, 'content-length')
    if contentLength then
        if not contentLength:match('^%d+$') then fail(response, 400, 'Content-Length inválido.'); return end
        if tonumber(contentLength) > MAX_BODY then fail(response, 413, 'Corpo excede 32 KiB.'); return end
    end
    local contentType = header(request, 'content-type')
    local mediaType = contentType and contentType:lower():match('^%s*([^;]+)')
    if not mediaType or mediaType:gsub('%s+$', '') ~= 'application/json' then
        fail(response, 415, 'Content-Type deve ser application/json.'); return
    end
    local completed = false
    request.setCancelHandler(function() completed = true end)
    SetTimeout(5000, function()
        if not completed then completed = true; fail(response, 408, 'Tempo limite de recebimento do JSON.'); end
    end)
    request.setDataHandler(function(body)
        if completed then return end
        completed = true
        if not authenticated(request) then fail(response, 401, 'Autenticação necessária.'); return end
        validateSession()
        if generation ~= state.mutationGeneration then
            fail(response, 409, 'O comando foi substituído ou a sessão foi encerrada.'); return
        end
        local currentLicense = state.pair and state.pair.license
        if currentLicense ~= acceptedLicense or boundPlayer() ~= acceptedPlayer then
            fail(response, 409, 'A licença, conexão ou permissão do jogador foi alterada.'); return
        end
        if type(body) ~= 'string' or #body > MAX_BODY then fail(response, 413, 'Corpo excede 32 KiB.'); return end
        local decoded, payload, position, decodeError = pcall(json.decode, body)
        if not decoded or decodeError or type(payload) ~= 'table' or type(position) ~= 'number'
            or body:sub(position):find('%S') then fail(response, 400, 'JSON inválido.'); return end
        callback(payload)
    end)
end

SetHttpHandler(function(request, response)
    if not authenticated(request) then
        fail(response, 401, 'Autenticação necessária.', { ['WWW-Authenticate'] = 'Bearer' }); return
    end
    local rawPath = request.path or ''
    local path, query = rawPath:match('^([^?]*)%??(.*)$')
    local expectedMethod = ({ ['/api/status'] = 'GET', ['/api/events'] = 'GET',
        ['/api/settings'] = 'POST', ['/api/revive'] = 'POST', ['/api/suicide'] = 'POST', ['/api/disconnect'] = 'POST' })[path]
    if not expectedMethod then fail(response, 404, 'Endpoint desconhecido.'); return end
    if request.method ~= expectedMethod then
        fail(response, 405, 'Método não permitido.', { ['Allow'] = expectedMethod }); return
    end
    if not allowRequest(path) then fail(response, 429, 'Limite de requisições excedido.', { ['Retry-After'] = '1' }); return end
    validateSession()
    if path == '/api/status' then
        local target, reason = boundPlayer()
        if state.active and target == state.player then state.heartbeat = now() end
        local capabilities = {}
        local medicalProvider, _, medicalMessage = selectedMedicalProvider()
        for _, capability in ipairs(CAPABILITIES) do
            if capability ~= 'player.revive' or medicalProvider then capabilities[#capabilities + 1] = capability end
        end
        if state.clientCapabilities.muteVoice then capabilities[#capabilities + 1] = 'player.muteVoice' end
        respond(response, 200, { version = VERSION, ready = target ~= nil,
            sessionActive = state.active, playerOnline = target ~= nil,
            playerId = target or 0, playerName = target and GetPlayerName(target) or '',
            desiredRevision = state.desiredRevision, appliedRevision = state.appliedRevision,
            message = reason or state.message, capabilities = capabilities,
            medicalProvider = medicalProvider or '', medicalMessage = medicalMessage })
    elseif path == '/api/events' then
        local sinceText = query:match('^since=(%d+)$')
        if query ~= '' and not sinceText then fail(response, 400, 'Use somente ?since=<inteiro>.'); return end
        local since = sinceText and tonumber(sinceText) or 0
        if not since or since > 9007199254740991 then fail(response, 400, 'Cursor inválido.'); return end
        local events = {}
        for _, event in ipairs(state.events) do if event.id > since then events[#events + 1] = event end end
        respond(response, 200, { cursor = state.cursor, events = events })
    elseif path == '/api/settings' then
        withJson(request, response, function(payload)
            local valid, message = schemaObject(payload, { 'settings' }, 'request')
            if not valid then fail(response, 422, message); return end
            local settings
            settings, message = validateSettings(payload.settings)
            if not settings then fail(response, 422, message); return end
            local target = selectForCommand(response)
            if not target then return end
            state.desired = settings
            state.desiredRevision = state.desiredRevision + 1
            state.lastDispatch = now()
            TriggerClientEvent('zyon_admin_menu:apply', target, settings, state.desiredRevision)
            sendAdminRoster(target)
            addEvent('requested', 'Configuração enviada; aguardando confirmação do cliente FiveM.')
            respond(response, 200, { revision = state.desiredRevision, message = state.message })
        end)
    elseif path == '/api/revive' or path == '/api/suicide' then
        withJson(request, response, function(payload)
            local valid, message = schemaObject(payload, {}, 'request')
            if not valid then fail(response, 422, message); return end
            if (state.pendingRevive and elapsed(now(), state.pendingRevive.sent) < 10000)
                or (state.pendingSuicide and elapsed(now(), state.pendingSuicide.sent) < 10000) then
                fail(response, 409, 'Aguarde a confirmação da ação anterior ou o prazo de 10 segundos.'); return
            end
            local provider, variant
            if path == '/api/revive' then
                provider, variant, message = selectedMedicalProvider()
                if not provider then fail(response, 503, message); return end
            end
            local target = selectForCommand(response)
            if not target then return end
            if not state.lastDispatch then
                if state.desiredRevision == 0 then state.desiredRevision = 1 end
                state.lastDispatch = now()
                TriggerClientEvent('zyon_admin_menu:apply', target, state.desired, state.desiredRevision)
            end
            if path == '/api/suicide' then
                -- Disable protection in the authoritative snapshot, so a later
                -- heartbeat cannot restore this menu's GodMode after suicide.
                state.desired.player.godMode = false
                state.desired.player.noclip = false
                state.desiredRevision = state.desiredRevision + 1
                state.lastDispatch = now()
                TriggerClientEvent('zyon_admin_menu:apply', target, state.desired, state.desiredRevision)
                state.suicideTicket = state.suicideTicket + 1
                state.pendingSuicide = { ticket = state.suicideTicket, player = target, sent = now() }
                TriggerClientEvent('zyon_admin_menu:suicide', target, state.suicideTicket, state.desiredRevision)
                addEvent('requested', 'Suicídio solicitado; aguardando confirmação da morte no cliente.')
                respond(response, 200, { ticket = state.suicideTicket, message = state.message })
            else
                state.reviveTicket = state.reviveTicket + 1
                state.pendingRevive = { ticket = state.reviveTicket, player = target, sent = now(),
                    revision = state.desiredRevision, provider = provider, variant = variant, medicalEpoch = medicalEpoch }
                dispatchRevive(state.pendingRevive)
                addEvent('requested', provider == 'standalone'
                    and 'Reviver nativo solicitado; aguardando confirmação do cliente.'
                    or 'Reviver ' .. (provider == 'esx' and 'ESX' or 'QBCore') .. ' solicitado; aguardando estado médico.')
                respond(response, 200, { ticket = state.reviveTicket, message = state.message })
            end
        end)
    elseif path == '/api/disconnect' then
        withJson(request, response, function(payload)
            local valid, message = schemaObject(payload, {}, 'request')
            if not valid then fail(response, 422, message); return end
            clearSession('Menu desconectado; desativação solicitada ao cliente.', true)
            respond(response, 200, { revision = state.desiredRevision, message = state.message })
        end)
    end
end)

RegisterNetEvent('zyon_admin_menu:ack', function(revision, success, message)
    local player = tonumber(source)
    validateSession()
    if not state.active or player ~= state.player or boundPlayer() ~= player then return end
    if not integer(revision, 1, 9007199254740991) or revision ~= state.desiredRevision then return end
    if success ~= nil and type(success) ~= 'boolean' then return end
    if success == false then
        local detail = type(message) == 'string' and message:gsub('[%c]', ''):sub(1, 120) or ''
        clearSession(detail ~= '' and detail or 'O cliente rejeitou a configuração; desativação solicitada.', true)
        return
    end
    if state.appliedRevision ~= revision then
        state.appliedRevision = revision
        addEvent('applied', 'Configuração confirmada pelo cliente FiveM.')
    end
end)

RegisterNetEvent('zyon_admin_menu:reviveAck', function(ticket, success, message, validation, expectedRevision, provider)
    local player = tonumber(source)
    validateSession()
    local pending = state.pendingRevive
    if not state.active or not pending or player ~= state.player or pending.player ~= player
        or boundPlayer() ~= player or ticket ~= pending.ticket or elapsed(now(), pending.sent) >= 10000
        or type(success) ~= 'boolean' then return end
    -- Old standalone clients did not include these fields. Framework ACKs must
    -- bind their provider and revision, and never treat native-only validation
    -- as a successful medical revive.
    if expectedRevision ~= nil and expectedRevision ~= pending.revision then return end
    if provider ~= nil and provider ~= pending.provider then return end
    if pending.provider ~= 'standalone' and (expectedRevision ~= pending.revision or provider ~= pending.provider
        or (success and validation ~= 'medical')) then return end
    if pending.ackReceived then return end
    if not reviveIsCurrent(pending) then
        reviveError(pending, 'Reviver cancelado: a sessão, revisão ou integração médica mudou.'); return
    end
    if type(message) ~= 'string' then message = '' end
    message = message:gsub('[%c]', ''):sub(1, 120)
    if not success then reviveError(pending, message ~= '' and message or 'O cliente não confirmou o estado médico.'); return end
    if pending.provider == 'standalone' then
        if validation ~= nil and validation ~= 'native' then return end
        state.pendingRevive = nil
        addEvent('revived', message ~= '' and message or 'Vida nativa confirmada; hospital não integrado.')
        return
    elseif pending.provider == 'esx' and pending.variant == 'legacy' then
        state.pendingRevive = nil
        addEvent('revived', 'ESX legado: vida nativa e callback oficial de morte confirmados pelo cliente.')
        return
    end
    pending.ackReceived = true
    CreateThread(function()
        local ok, detail = pcall(function()
            while true do
                if not reviveIsCurrent(pending) then
                    error('Reviver expirou ou a sessão, revisão ou integração médica mudou.', 0)
                end
                local alive, reason = serverMedicalAlive(pending.provider, pending.variant, player)
                if not reviveIsCurrent(pending) then
                    error('Reviver expirou ou a sessão, revisão ou integração médica mudou durante a confirmação.', 0)
                end
                if alive == nil then error(reason, 0) end
                if alive then
                    state.pendingRevive = nil
                    addEvent('revived', pending.provider == 'esx'
                        and 'ESX: vida nativa confirmada pelo cliente e estado médico confirmado no servidor.'
                        or 'QBCore: vida nativa confirmada pelo cliente; isdead/inlaststand confirmados no servidor.')
                    return
                end
                Wait(100)
            end
        end)
        if not ok then reviveError(pending, tostring(detail):gsub('[%c]', ''):sub(1, 120)) end
    end)
end)

RegisterNetEvent('zyon_admin_menu:suicideAck', function(ticket, success, message)
    local player = tonumber(source)
    validateSession()
    local pending = state.pendingSuicide
    if not state.active or not pending or player ~= state.player or pending.player ~= player
        or boundPlayer() ~= player or ticket ~= pending.ticket or elapsed(now(), pending.sent) >= 10000
        or type(success) ~= 'boolean' then return end
    state.pendingSuicide = nil
    local detail = type(message) == 'string' and message:gsub('[%c]', ''):sub(1, 120) or ''
    addEvent(success and 'suicide' or 'error', detail ~= '' and detail
        or (success and 'Morte confirmada pelo cliente.' or 'O cliente não confirmou a morte.'))
end)

RegisterNetEvent('zyon_admin_menu:clientCapabilities', function(capabilities)
    local player = tonumber(source)
    validateSession()
    if not state.active or player ~= state.player or boundPlayer() ~= player
        or type(capabilities) ~= 'table' or type(capabilities.muteVoice) ~= 'boolean' then return end
    for key in pairs(capabilities) do if key ~= 'muteVoice' then return end end
    state.clientCapabilities = { muteVoice = capabilities.muteVoice }
end)

AddEventHandler('playerDropped', function()
    local player = tonumber(source)
    if player then state.pairAttempts[player] = nil end
    if state.pair and player == state.pair.player then revokePair('Jogador desconectado; pareamento encerrado.', false)
    elseif player == state.player then clearSession('Jogador desconectado; sessão encerrada.', false) end
end)

AddEventHandler('onResourceStop', function(resource)
    if M.isDependency(resource) then
        medicalEpoch = medicalEpoch + 1
        if state.pendingRevive and state.pendingRevive.provider ~= 'standalone' then
            reviveError(state.pendingRevive, 'Um recurso médico foi interrompido; reviver cancelado sem fallback nativo.')
        end
    end
    if resource == RESOURCE then
        if state.pair then revokePair('Recurso encerrado; desativação solicitada ao cliente.', true)
        elseif state.active then clearSession('Recurso encerrado; desativação solicitada ao cliente.', true) end
    end
end)

CreateThread(function()
    while true do
        Wait(250)
        validateSession()
        local timestamp = now()
        if state.active and (not state.lastDispatch or elapsed(timestamp, state.lastDispatch) >= 5000) then
            state.lastDispatch = timestamp
            TriggerClientEvent('zyon_admin_menu:apply', state.player, state.desired, state.desiredRevision)
            sendAdminRoster(state.player)
        end
        if state.pendingRevive and elapsed(timestamp, state.pendingRevive.sent) >= 10000 then
            state.pendingRevive = nil
            addEvent('error', 'O cliente não confirmou o pedido de reviver em 10 segundos.')
        elseif state.pendingRevive and not reviveIsCurrent(state.pendingRevive) then
            reviveError(state.pendingRevive, 'Reviver cancelado: a sessão, revisão ou integração médica mudou.')
        end
        if state.pendingSuicide and elapsed(timestamp, state.pendingSuicide.sent) >= 10000 then
            state.pendingSuicide = nil
            addEvent('error', 'O cliente não confirmou o pedido de suicídio em 10 segundos.')
        end
    end
end)
