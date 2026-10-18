local resourceName = GetCurrentResourceName()
local moduleSource = assert(
    LoadResourceFile(resourceName, "lib/simulator.lua"),
    "Nao foi possivel carregar lib/simulator.lua"
)
local Simulator = assert(load(moduleSource, "@lib/simulator.lua", "t"))()
local lab = Simulator.new()

local function reply(source, message)
    if source == 0 then
        print("[HWID Lab] " .. message)
    else
        TriggerClientEvent("chat:addMessage", source, {
            color = { 70, 180, 210 },
            args = { "HWID Lab", message },
        })
    end
end

local function describe(profile)
    local status = profile.banned and "BANIDO (simulacao)" or "LIBERADO (simulacao)"
    local result = profile.id .. " | " .. profile.label .. " | " .. status
    if profile.reason then
        result = result .. " | Motivo: " .. profile.reason
    end
    return result
end

local commands = {}

function commands.help(source)
    for _, usage in ipairs({
        "hwidlab create <nome do perfil ficticio>",
        "hwidlab list",
        "hwidlab show <SIM-HWID-000001>",
        "hwidlab ban <SIM-HWID-000001> <motivo>",
        "hwidlab unban <SIM-HWID-000001>",
        "hwidlab check <SIM-HWID-000001>",
        "hwidlab reset CONFIRMAR",
    }) do
        reply(source, usage)
    end
end

function commands.create(source, args)
    local profile, err = lab:create(table.concat(args, " ", 2))
    reply(source, profile and describe(profile) or err)
end

function commands.list(source)
    local profiles = lab:list()
    if #profiles == 0 then
        reply(source, "Nenhum perfil ficticio. Use hwidlab create <nome>.")
        return
    end
    for _, profile in ipairs(profiles) do
        reply(source, describe(profile))
    end
end

function commands.show(source, args)
    local profile, err = lab:get(args[2])
    reply(source, profile and describe(profile) or err)
end

function commands.ban(source, args)
    local profile, err = lab:ban(args[2], table.concat(args, " ", 3))
    reply(source, profile and describe(profile) or err)
end

function commands.unban(source, args)
    local profile, err = lab:unban(args[2])
    reply(source, profile and describe(profile) or err)
end

function commands.check(source, args)
    local result, err = lab:check(args[2])
    if not result then
        reply(source, err)
        return
    end
    local status = result.allowed and "PERMITIDO" or "NEGADO"
    local message = result.id .. " | Acesso simulado: " .. status
    if result.reason then
        message = message .. " | Motivo: " .. result.reason
    end
    reply(source, message)
end

function commands.reset(source, args)
    if args[2] ~= "CONFIRMAR" then
        reply(source, "Para apagar os perfis de teste, use hwidlab reset CONFIRMAR.")
        return
    end
    lab:reset()
    reply(source, "Perfis ficticios removidos. A sequencia de IDs foi preservada nesta sessao.")
end

-- FiveM restricts player use via command.hwidlab; the server console can use it.
RegisterCommand("hwidlab", function(source, args)
    local action = (args[1] or "help"):lower()
    local handler = commands[action]
    if not handler then
        reply(source, "Comando desconhecido. Use hwidlab help.")
        return
    end
    handler(source, args)
end, true)

print("[HWID Lab] Simulador carregado. Use hwidlab help no console.")
