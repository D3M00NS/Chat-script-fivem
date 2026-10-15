-- Lua / fivem/zyon_admin_menu/pairing_client.lua / FiveM Lua 5.4 / automatic local admin pairing.
-- GET_CURRENT_SERVER_ENDPOINT(): citizenfx/fivem/ext/native-decls/GetCurrentServerEndpoint.md.
local VERSION, TIMEOUT_MS = '3.1.1', 5000
local pending

local function validNonce(value)
    return type(value) == 'string' and #value == 64 and value:match('^[0-9a-f]+$') ~= nil
end

local function validPort(value)
    if type(value) ~= 'string' or not value:match('^%d+$') then return false end
    local port = tonumber(value)
    return port ~= nil and port >= 1 and port <= 65535
end

local function loopback(host)
    host = host:lower()
    if host == 'localhost' or host == '::1' then return true end
    local a, b, c, d = host:match('^(%d+)%.(%d+)%.(%d+)%.(%d+)$')
    if not a or tonumber(a) ~= 127 then return false end
    for _, part in ipairs({a, b, c, d}) do
        if #part > 3 or tonumber(part) > 255 then return false end
    end
    return true
end

local function splitAuthority(authority)
    if authority:sub(1, 1) == '[' then
        local host, port = authority:match('^%[([%x:]+)%]:(%d+)$')
        if host then return host, port end
        host = authority:match('^%[([%x:]+)%]$')
        return host, nil
    end
    local host, port = authority:match('^([%w%.%-]+):(%d+)$')
    if host then return host, port end
    return authority:match('^([%w%.%-]+)$'), nil
end

local function checkedEndpoint(value)
    if type(value) ~= 'string' or #value == 0 or #value > 2048
        or value:find('[%s%c]') or value:find('[\\@?#]') then
        return nil, 'A URL pública do recurso é inválida.'
    end
    local scheme, authority, path = value:match('^(https?)://([^/]+)(/.*)$')
    if not scheme then
        scheme, authority = value:match('^(https?)://([^/]+)$')
        path = '/'
    end
    if not scheme or not authority then return nil, 'A URL pública do recurso é inválida.' end
    local host, port = splitAuthority(authority)
    if not host or (port and not validPort(port)) then
        return nil, 'A URL pública do recurso tem host ou porta inválidos.'
    end
    if scheme ~= 'https' and not loopback(host) then
        return nil, 'Configure zyon_menu_public_url com a URL HTTPS pública do recurso no servidor.'
    end
    if path:sub(-1) ~= '/' then value = value .. '/' end
    return value
end

local function serverEndpoint(configured)
    if configured ~= nil and configured ~= '' then return checkedEndpoint(configured) end
    if type(GetCurrentServerEndpoint) ~= 'function' then
        return nil, 'Esta versão do FiveM não informa o endereço do servidor. Configure zyon_menu_public_url com HTTPS.'
    end
    local ok, peer = pcall(GetCurrentServerEndpoint)
    if not ok or type(peer) ~= 'string' or peer == '' or peer:find('[%s%c]') then
        return nil, 'O endereço do servidor ainda não está disponível. Aguarde a conexão da cidade.'
    end
    local host, port = splitAuthority(peer)
    if not host or not port or not validPort(port) then
        return nil, 'O endereço do servidor não informa uma porta válida. Configure zyon_menu_public_url com HTTPS.'
    end
    if not loopback(host) then
        return nil, 'Configure zyon_menu_public_url com a URL HTTPS pública do recurso no servidor.'
    end
    local authority = host:find(':', 1, true) and ('[' .. host .. ']:' .. port) or (host .. ':' .. port)
    return 'http://' .. authority .. '/zyon_admin_menu/'
end

local function elapsed(start)
    return (GetGameTimer() - start) % 4294967296
end

local function reply(current, result)
    local endpoint, errorMessage
    if result.ok == true then endpoint, errorMessage = serverEndpoint(result.endpoint) end
    local playerId = result.playerId
    if type(playerId) ~= 'number' or playerId < 1 or playerId % 1 ~= 0 then playerId = 0 end
    local playerName = type(result.playerName) == 'string' and result.playerName:sub(1, 128) or ''
    local suppliedError = type(result.error) == 'string'
        and result.error:gsub(current.nonce, '[omitido]'):sub(1, 240) or nil
    local accepted = result.ok == true and endpoint ~= nil and playerId > 0
    if not accepted and not errorMessage then
        errorMessage = suppliedError or 'O servidor não autorizou este jogador. Verifique a permissão ACE zyon.modmenu.'
    end
    SendNUIMessage({type = 'pairResult', nonce = current.nonce, requestId = current.requestId,
        ok = accepted, endpoint = accepted and endpoint or '', token = accepted and current.nonce or '',
        playerId = playerId, playerName = playerName, error = accepted and '' or errorMessage, version = VERSION})
end

RegisterNUICallback('pairRequest', function(data, cb)
    if type(data) ~= 'table' or not validNonce(data.nonce)
        or type(data.requestId) ~= 'number' or data.requestId < 1
        or data.requestId % 1 ~= 0 or data.requestId > 9007199254740991 then
        cb({accepted = false, error = 'Solicitação de conexão local inválida.'})
        return
    end
    if pending and elapsed(pending.started) < TIMEOUT_MS then
        cb({accepted = false, error = 'Já existe uma solicitação de conexão pendente.'})
        return
    end
    pending = {nonce = data.nonce, requestId = data.requestId, started = GetGameTimer()}
    -- Resolve the browser callback immediately; the server authorization is asynchronous.
    cb({accepted = true, requestId = data.requestId})
    TriggerServerEvent('zyon_admin_menu:pairRequest', data.nonce, data.requestId)
end)

RegisterNetEvent('zyon_admin_menu:pairResult', function(result)
    if source ~= 65535 or not pending or type(result) ~= 'table'
        or not validNonce(result.nonce) or result.nonce ~= pending.nonce
        or result.requestId ~= pending.requestId then return end
    local current = pending
    pending = nil
    if elapsed(current.started) >= TIMEOUT_MS then return end
    reply(current, result)
end)

CreateThread(function()
    while true do
        Wait(250)
        if pending and elapsed(pending.started) >= TIMEOUT_MS then
            local current = pending
            pending = nil
            reply(current, {ok = false, error = 'O recurso não respondeu. Verifique se zyon_admin_menu está iniciado no servidor.'})
        end
    end
end)

AddEventHandler('onClientResourceStop', function(resource)
    if resource == GetCurrentResourceName() then pending = nil end
end)
