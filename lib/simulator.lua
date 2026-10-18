-- Pure Lua test registry. IDs belong only to this simulator.
local Simulator = {}

local function copy(profile)
    return {
        id = profile.id,
        label = profile.label,
        banned = profile.banned,
        reason = profile.reason,
    }
end

local function cleanText(value, limit, field)
    if type(value) ~= "string" or value:find("%c") then
        return nil, field .. " deve ser um texto sem caracteres de controle."
    end

    value = value:match("^%s*(.-)%s*$")
    if #value == 0 or #value > limit then
        return nil, field .. " deve ter entre 1 e " .. limit .. " bytes."
    end

    return value
end

function Simulator.new()
    local profiles = {}
    local order = {}
    local nextNumber = 1
    local lab = {}

    local function lookup(id)
        if type(id) ~= "string" or not id:match("^SIM%-HWID%-%d%d%d%d%d%d$") then
            return nil, "Use um ID ficticio no formato SIM-HWID-000001."
        end
        if not profiles[id] then
            return nil, "Perfil ficticio nao encontrado."
        end
        return profiles[id]
    end

    function lab:create(label)
        local cleaned, err = cleanText(label, 64, "Nome")
        if not cleaned then
            return nil, err
        end
        if nextNumber > 999999 then
            return nil, "Limite de IDs atingido. Reinicie o recurso para uma nova sessao."
        end

        local id = string.format("SIM-HWID-%06d", nextNumber)
        nextNumber = nextNumber + 1
        local profile = { id = id, label = cleaned, banned = false }
        profiles[id] = profile
        order[#order + 1] = id
        return copy(profile)
    end

    function lab:get(id)
        local profile, err = lookup(id)
        if not profile then
            return nil, err
        end
        return copy(profile)
    end

    function lab:list()
        local result = {}
        for i, id in ipairs(order) do
            result[i] = copy(profiles[id])
        end
        return result
    end

    function lab:ban(id, reason)
        local profile, err = lookup(id)
        if not profile then
            return nil, err
        end
        local cleaned, textError = cleanText(reason, 200, "Motivo")
        if not cleaned then
            return nil, textError
        end
        profile.banned = true
        profile.reason = cleaned
        return copy(profile)
    end

    function lab:unban(id)
        local profile, err = lookup(id)
        if not profile then
            return nil, err
        end
        profile.banned = false
        profile.reason = nil
        return copy(profile)
    end

    function lab:check(id)
        local profile, err = lookup(id)
        if not profile then
            return nil, err
        end
        return { id = profile.id, allowed = not profile.banned, reason = profile.reason }
    end

    function lab:reset()
        profiles = {}
        order = {}
        -- Keep the counter so stale IDs cannot refer to a new profile this session.
        return true
    end

    return lab
end

return Simulator
