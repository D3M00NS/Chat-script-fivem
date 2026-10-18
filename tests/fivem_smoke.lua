-- Minimal server-side FiveM harness. Identifier, networking, persistence, and
-- connection APIs are intentionally unavailable to this simulation resource.
local commands = {}
local console = {}
local messages = {}
local passed, failed = 0, 0

local function equal(actual, expected, context)
    if actual ~= expected then
        error(('%s: expected %s, got %s'):format(context or 'value', tostring(expected), tostring(actual)), 2)
    end
end

local function contains(value, expected)
    assert(value:find(expected, 1, true), ('expected output to contain %q; got %q'):format(expected, value))
end

local function test(name, run)
    local ok, err = pcall(run)
    if ok then
        passed = passed + 1
        io.write('ok - ', name, '\n')
    else
        failed = failed + 1
        io.stderr:write('FAIL - ', name, ': ', tostring(err), '\n')
    end
end

function GetCurrentResourceName()
    return 'hwid_lab'
end

function LoadResourceFile(resource, path)
    equal(resource, 'hwid_lab', 'resource file scope')
    equal(path, 'lib/simulator.lua', 'loaded resource module')
    local file = assert(io.open(path, 'rb'))
    local contents = file:read('*a')
    file:close()
    return contents
end

function RegisterCommand(name, handler, restricted)
    assert(not commands[name], 'command cannot be registered twice')
    commands[name] = { handler = handler, restricted = restricted }
end

function TriggerClientEvent(event, target, payload)
    equal(event, 'chat:addMessage', 'player response event')
    assert(type(target) == 'number' and target > 0, 'player responses must target an individual player')
    assert(type(payload) == 'table' and type(payload.args) == 'table', 'chat response must contain args')
    local values = {}
    for _, value in ipairs(payload.args) do
        values[#values + 1] = tostring(value)
    end
    messages[#messages + 1] = { target = target, text = table.concat(values, ' ') }
end

print = function(...)
    local values = {}
    for index = 1, select('#', ...) do
        values[index] = tostring(select(index, ...))
    end
    console[#console + 1] = table.concat(values, '\t')
end

assert(loadfile('server.lua'))()

local function invoke(source, args)
    local command = assert(commands.hwidlab, 'hwidlab command must exist')
    local consoleStart, messageStart = #console, #messages
    command.handler(source, args, 'hwidlab ' .. table.concat(args, ' '))
    local output = {}
    if source == 0 then
        equal(#messages, messageStart, 'console command cannot send player messages')
        for index = consoleStart + 1, #console do
            output[#output + 1] = console[index]
        end
    else
        for index = messageStart + 1, #messages do
            equal(messages[index].target, source, 'response recipient')
            output[#output + 1] = messages[index].text
        end
    end
    assert(#output > 0, 'command must produce a response')
    return table.concat(output, '\n')
end

test('command registration requires ACE authorization', function()
    equal(commands.hwidlab.restricted, true)
    equal(type(commands.hwidlab.handler), 'function')
end)

test('console commands create and show a simulated profile', function()
    local created = invoke(0, { 'create', 'Perfil', 'console' })
    contains(created, 'SIM-HWID-000001')
    local shown = invoke(0, { 'show', 'SIM-HWID-000001' })
    contains(shown, 'SIM-HWID-000001')
    contains(shown, 'Perfil console')
    local checked = invoke(0, { 'check', 'SIM-HWID-000001' })
    contains(checked, 'SIM-HWID-000001')
    contains(checked, 'PERMITIDO')
end)

test('player responses are sent only to the invoking player', function()
    local created = invoke(17, { 'create', 'Perfil', 'jogador' })
    contains(created, 'SIM-HWID-000002')
    local listed = invoke(17, { 'list' })
    contains(listed, 'SIM-HWID-000001')
    contains(listed, 'SIM-HWID-000002')
end)

test('ban and unban commands change the simulated registry', function()
    local id = 'SIM-HWID-000001'
    contains(invoke(0, { 'ban', id, 'Motivo', 'simulado' }), id)
    local checked = invoke(0, { 'check', id })
    contains(checked, 'NEGADO')
    contains(checked, 'Motivo simulado')
    contains(invoke(0, { 'show', id }), 'Motivo simulado')
    contains(invoke(17, { 'unban', id }), id)
    local after = invoke(0, { 'check', id })
    contains(after, id)
    contains(after, 'PERMITIDO')
    assert(not after:find('Motivo simulado', 1, true), 'unban must clear the simulated ban reason')
end)

test('missing arguments and unknown identifiers return responses without crashing', function()
    local cases = {
        {}, { 'desconhecido' }, { 'create' }, { 'show' }, { 'check' },
        { 'ban' }, { 'ban', 'SIM-HWID-000001' }, { 'unban' },
        { 'show', 'license:real-identifier' },
        { 'ban', 'SIM-HWID-999999', 'Motivo' },
        { 'unban', 'SIM-HWID-999999' }, { 'check', 'SIM-HWID-999999' },
    }
    for _, args in ipairs(cases) do
        invoke(0, args)
        invoke(17, args)
    end
    contains(invoke(0, { 'show', 'SIM-HWID-000001' }), 'Perfil console')
end)

test('reset requires the explicit confirmation and preserves the ID sequence', function()
    invoke(0, { 'reset' })
    contains(invoke(0, { 'list' }), 'SIM-HWID-000001')
    invoke(17, { 'reset', 'sim' })
    contains(invoke(0, { 'list' }), 'SIM-HWID-000002')
    invoke(0, { 'reset', 'CONFIRMAR' })
    local listed = invoke(0, { 'list' })
    assert(not listed:find('SIM-HWID-000001', 1, true), 'reset must remove the first simulated profile')
    assert(not listed:find('SIM-HWID-000002', 1, true), 'reset must remove the second simulated profile')
    contains(invoke(0, { 'create', 'Após', 'reset' }), 'SIM-HWID-000003')
end)

io.write(('\n%d passed; %d failed\n'):format(passed, failed))
if failed > 0 then
    os.exit(1)
end
