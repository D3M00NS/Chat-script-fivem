local Simulator = assert(loadfile('lib/simulator.lua'))()

local passed = 0
local failed = 0

local function equal(actual, expected, context)
    if actual ~= expected then
        error(('%s: expected %s, got %s'):format(context or 'value', tostring(expected), tostring(actual)), 2)
    end
end

local function rejected(value, err)
    equal(value, nil, 'rejected result')
    assert(type(err) == 'string' and #err > 0, 'rejection must include an error message')
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

test('profiles are fictional, trimmed, and sequential', function()
    local lab = Simulator.new()
    local first = assert(lab:create('  Perfil de teste  '))
    local second = assert(lab:create('Segundo'))
    equal(first.id, 'SIM-HWID-000001')
    equal(second.id, 'SIM-HWID-000002')
    equal(first.label, 'Perfil de teste')
    equal(first.banned, false)
    equal(first.reason, nil)
end)

test('separate laboratories have isolated state', function()
    local one, two = Simulator.new(), Simulator.new()
    local profile = assert(one:create('Um'))
    equal(#two:list(), 0)
    equal(assert(two:create('Dois')).id, profile.id)
    assert(one:ban(profile.id, 'Teste'))
    equal(assert(two:get(profile.id)).banned, false)
end)

test('labels enforce types, content, and the byte limit', function()
    local lab = Simulator.new()
    rejected(lab:create(nil))
    rejected(lab:create(false))
    rejected(lab:create(42))
    rejected(lab:create({}))
    rejected(lab:create(''))
    rejected(lab:create('   '))
    rejected(lab:create(string.rep('a', 65)))
    rejected(lab:create('a\0b'))
    rejected(lab:create('a\nb'))
    rejected(lab:create('a\tb'))
    rejected(lab:create('\tNome'))
    rejected(lab:create('Nome\n'))
    rejected(lab:create('\rNome '))
    rejected(lab:create('a' .. string.char(127) .. 'b'))
    equal(assert(lab:create(string.rep('a', 64))).label, string.rep('a', 64))
    equal(assert(lab:create(string.rep('á', 32))).label, string.rep('á', 32))
    rejected(lab:create(string.rep('á', 33)))
    equal(#lab:list(), 2, 'invalid input cannot create profiles')
end)

test('list preserves creation order and returns independent copies', function()
    local lab = Simulator.new()
    local first = assert(lab:create('Primeiro'))
    local second = assert(lab:create('Segundo'))
    first.label = 'Mutação externa'
    first.banned = true
    first.reason = 'Mutação externa'
    local listed = lab:list()
    equal(listed[1].id, first.id)
    equal(listed[1].label, 'Primeiro')
    equal(listed[1].banned, false)
    equal(listed[1].reason, nil)
    equal(listed[2].id, second.id)
    listed[1].label = 'Mutação da lista'
    listed[2] = nil
    local listedAgain = lab:list()
    equal(#listedAgain, 2)
    equal(listedAgain[1].label, 'Primeiro')
end)

test('get returns copies and rejects unrecognized identifiers', function()
    local lab = Simulator.new()
    local profile = assert(lab:create('Consulta'))
    local copy = assert(lab:get(profile.id))
    copy.id = 'SIM-HWID-999999'
    copy.label = 'Mutação'
    copy.banned = true
    equal(assert(lab:get(profile.id)).label, 'Consulta')
    equal(assert(lab:get(profile.id)).banned, false)
    local invalid = {
        '', 'license:real-identifier', 'steam:110000000000000',
        'SIM-HWID-000000', 'SIM-HWID-999999', 'sim-hwid-000001',
        'SIM-HWID-1', 'SIM-HWID-0000001', ' SIM-HWID-000001',
        'SIM-HWID-000001 ', 'SIM-HWID-000001\n', 'SIM-HWID-000001\0',
        123, false, {},
    }
    for _, id in ipairs(invalid) do
        rejected(lab:get(id))
        rejected(lab:ban(id, 'Teste'))
        rejected(lab:unban(id))
        rejected(lab:check(id))
    end
    rejected(lab:get(nil))
    rejected(lab:ban(nil, 'Teste'))
    rejected(lab:unban(nil))
    rejected(lab:check(nil))
    equal(assert(lab:get(profile.id)).banned, false, 'invalid operations preserve known profiles')
end)

test('ban and unban affect only the selected simulated profile', function()
    local lab = Simulator.new()
    local first = assert(lab:create('Primeiro'))
    local second = assert(lab:create('Segundo'))
    local allowed = assert(lab:check(first.id))
    equal(allowed.id, first.id)
    equal(allowed.allowed, true)
    equal(allowed.reason, nil)
    local banned = assert(lab:ban(first.id, '  Banimento de laboratório  '))
    equal(banned.banned, true)
    equal(banned.reason, 'Banimento de laboratório')
    local denied = assert(lab:check(first.id))
    equal(denied.id, first.id)
    equal(denied.allowed, false)
    equal(denied.reason, 'Banimento de laboratório')
    equal(assert(lab:check(second.id)).allowed, true)
    banned.banned = false
    banned.reason = 'Mutação'
    denied.reason = 'Outra mutação'
    equal(assert(lab:get(first.id)).banned, true)
    equal(assert(lab:check(first.id)).reason, 'Banimento de laboratório')
    local unbanned = assert(lab:unban(first.id))
    equal(unbanned.banned, false)
    equal(unbanned.reason, nil)
    equal(assert(lab:check(first.id)).allowed, true)
    equal(assert(lab:check(first.id)).reason, nil)
    unbanned.banned = true
    equal(assert(lab:get(first.id)).banned, false)
    assert(lab:ban(first.id, 'Segundo ciclo'))
    equal(assert(lab:check(first.id)).reason, 'Segundo ciclo')
end)

test('invalid reasons leave existing state unchanged', function()
    local lab = Simulator.new()
    local profile = assert(lab:create('Validação'))
    local invalid = { '', '   ', false, 123, {}, string.rep('r', 201),
        'a\0b', 'a\nb', 'a\tb', '\tMotivo', 'Motivo\n',
        'a' .. string.char(127) .. 'b' }
    rejected(lab:ban(profile.id, nil))
    for _, reason in ipairs(invalid) do
        rejected(lab:ban(profile.id, reason))
    end
    equal(assert(lab:check(profile.id)).allowed, true)
    local maximum = string.rep('r', 200)
    equal(assert(lab:ban(profile.id, maximum)).reason, maximum)
    rejected(lab:ban(profile.id, ''))
    equal(assert(lab:check(profile.id)).reason, maximum)
    equal(assert(lab:ban(profile.id, string.rep('á', 100))).reason, string.rep('á', 100))
    rejected(lab:ban(profile.id, string.rep('á', 101)))
end)

test('reset clears in-memory state without reusing old identifiers', function()
    local lab = Simulator.new()
    local first = assert(lab:create('Primeiro'))
    local second = assert(lab:create('Segundo'))
    assert(lab:ban(first.id, 'Teste'))
    equal(lab:reset(), true)
    equal(#lab:list(), 0)
    rejected(lab:get(first.id))
    rejected(lab:check(second.id))
    local nextProfile = assert(lab:create('Após reset'))
    equal(nextProfile.id, 'SIM-HWID-000003')
    equal(nextProfile.banned, false)
    equal(lab:reset(), true)
    equal(lab:reset(), true)
    equal(assert(lab:create('Após dois resets')).id, 'SIM-HWID-000004')
end)

io.write(('\n%d passed; %d failed\n'):format(passed, failed))
if failed > 0 then
    os.exit(1)
end
