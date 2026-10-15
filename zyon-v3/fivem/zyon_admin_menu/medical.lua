-- Shared provider selection. Only the standard, documented medical resources
-- are supported; resource/event names never come from an HTTP or client input.
ZyonMedical = {}
local M = ZyonMedical
local dependencies = {
    esx = {'es_extended'},
    qbcore = {'qb-core', 'qb-ambulancejob'}
}

local function started(resource)
    if type(GetResourceState) ~= 'function' then
        return nil, 'Não foi possível consultar os recursos médicos do servidor.'
    end
    local ok, state = pcall(GetResourceState, resource)
    if not ok or type(state) ~= 'string' then
        return nil, 'Não foi possível consultar o recurso médico ' .. resource .. '.'
    end
    return state == 'started', nil, state
end

local function requiresEsxDeath()
    if type(GetResourceMetadata) ~= 'function' then return false end
    -- Modern ESX declares this dependency in its manifest. If it is stopped,
    -- do not mistake the missing modern lifecycle for the legacy event API.
    for index = 0, 63 do
        local dependency = GetResourceMetadata('esx_ambulancejob', 'dependency', index)
        if dependency == nil then break end
        if dependency == 'esx_death' then return true end
    end
    return false
end

function M.available(provider, variant)
    if provider == 'standalone' then return true end
    if not dependencies[provider] then return nil, 'Integração médica desconhecida.' end
    for _, resource in ipairs(dependencies[provider]) do
        local ready, errorMessage = started(resource)
        if ready == nil then return nil, errorMessage end
        if not ready then
            return nil, 'Reviver requer ' .. resource .. ' iniciado; nomes personalizados precisam de uma integração própria.'
        end
    end
    if provider == 'esx' then
        local modern, errorMessage, deathState = started('esx_death')
        if modern == nil then return nil, errorMessage end
        if variant == 'esx_death' then
            if not modern then return nil, 'Reviver ESX requer esx_death iniciado.' end
        elseif variant == 'legacy' then
            local ambulance
            ambulance, errorMessage = started('esx_ambulancejob')
            if ambulance == nil then return nil, errorMessage end
            if not ambulance then
                return nil, 'Reviver ESX legado requer esx_ambulancejob iniciado; nomes personalizados precisam de uma integração própria.'
            end
            if deathState ~= 'missing' or requiresEsxDeath() then
                return nil, 'O ciclo médico ESX mudou; selecione novamente antes de reviver.'
            end
        else
            return nil, 'Integração ESX inválida.'
        end
    elseif variant ~= 'qbcore' then
        return nil, 'Integração QBCore inválida.'
    end
    return true
end

function M.select(configuration)
    if configuration ~= 'auto' and configuration ~= 'standalone'
        and configuration ~= 'esx' and configuration ~= 'qbcore' then
        return nil, nil, 'zyon_menu_medical_provider deve ser auto, standalone, esx ou qbcore.'
    end
    if configuration == 'standalone' then
        return 'standalone', 'native', 'Reviver standalone: confirma apenas vida nativa, sem estado de hospital.'
    end
    local esxCore, errorMessage = started('es_extended')
    if esxCore == nil then return nil, nil, errorMessage end
    local qbCore
    qbCore, errorMessage = started('qb-core')
    if qbCore == nil then return nil, nil, errorMessage end
    if configuration == 'auto' then
        if esxCore and qbCore then
            return nil, nil, 'ESX e QBCore estão iniciados juntos; configure zyon_menu_medical_provider explicitamente.'
        end
        if esxCore then configuration = 'esx'
        elseif qbCore then configuration = 'qbcore'
        else
            -- Installed frameworks remain medical integrations during a stop
            -- or restart. Only 'missing' establishes an absent resource; using
            -- native revive while a hospital is stopped would bypass its state.
            for _, resource in ipairs({'es_extended', 'qb-core', 'esx_ambulancejob', 'qb-ambulancejob', 'esx_death'}) do
                local ready, resourceState
                ready, errorMessage, resourceState = started(resource)
                if ready == nil then return nil, nil, errorMessage end
                if resourceState ~= 'missing' then
                    return nil, nil, 'O recurso médico ' .. resource .. ' está ' .. resourceState
                        .. ' e a integração está incompleta; reviver bloqueado sem fallback nativo.'
                end
            end
            return 'standalone', 'native', 'Reviver standalone: confirma apenas vida nativa, sem estado de hospital.'
        end
    end
    local variant = 'qbcore'
    if configuration == 'esx' then
        local modern, deathState
        modern, errorMessage, deathState = started('esx_death')
        if modern == nil then return nil, nil, errorMessage end
        if not modern and deathState ~= 'missing' then
            return nil, nil, 'esx_death está ' .. deathState .. '; aguarde o recurso iniciar antes de reviver.'
        end
        if not modern and requiresEsxDeath() then
            return nil, nil, 'Esta versão ESX requer esx_death iniciado; não existe fallback nativo.'
        end
        variant = modern and 'esx_death' or 'legacy'
    end
    local ready
    ready, errorMessage = M.available(configuration, variant)
    if not ready then return nil, nil, errorMessage end
    return configuration, variant, configuration == 'esx'
        and (variant == 'esx_death' and 'Reviver ESX com esx_death e confirmação médica.'
            or 'Reviver ESX legado com esx_ambulancejob e confirmação persistida pelo callback oficial.')
        or 'Reviver QBCore com qb-ambulancejob e confirmação de isdead/inlaststand.'
end

function M.isDependency(resource)
    return resource == 'es_extended' or resource == 'esx_ambulancejob' or resource == 'esx_death'
        or resource == 'qb-core' or resource == 'qb-ambulancejob'
end
