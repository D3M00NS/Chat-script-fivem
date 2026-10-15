-- Lua 5.4 / mocked FiveM contracts. These checks cannot validate a live city's
-- death animations, hospital customizations, database, or actual GTA natives.
local root = arg[0]:match('^(.*)/tests/[^/]+$') or '.'
local function loadScript(environment, name)
    assert(loadfile(root .. '/fivem/zyon_admin_menu/' .. name, 't', environment))()
end
local function environment(resources)
    local env = setmetatable({}, {__index = _G})
    env._G = env
    env.resources = resources or {}
    env.GetResourceState = function(name) return env.resources[name] or 'missing' end
    env.GetResourceMetadata = function(_, key, index)
        if key == 'dependency' then return env.dependencies and env.dependencies[index + 1] end
    end
    loadScript(env, 'medical.lua')
    return env
end
local function defaults()
    local function aim()
        return {enabled=false, onlyVisible=true, onlyPlayers=true, targetBone='Head',
            activationKey='RMB', smooth=1, drawFov=false, fov=100}
    end
    return {aim=aim(), silent=aim(), weapons={noRecoil=false,noSpread=false,freezeAmmo=false},
        player={godMode=false,noclip=false,noclipSpeed=5,invisible=false,muteVoice=false},
        esp={enabled=false,boxes=true,skeletons=false,names=true,distance=true,
            onlyPlayers=true,onlyVisible=true,maxDistance=250,admins=false}}
end
local function has(list, value)
    for _, item in ipairs(list) do if item == value then return true end end
    return false
end
local function callable(callback)
    return setmetatable({__cfx_functionReference='mock-reference'}, {__call=function(_, ...) return callback(...) end})
end

local function clientFixture(options)
    options = options or {}
    local env = environment(options.resources)
    local f = {env=env, clock=0, alive=options.alive == true, metadata={isdead=true,inlaststand=true},
        esxDead=true, events={}, handlers={}, sent={}, threads={}, frameworkCalls={}, nativeCalls=0,
        heal=options.heal ~= false, callbackRespond=options.callbackRespond ~= false, callbackCalls=0}
    env.RegisterNetEvent = function(name, callback) f.events[name] = callback end
    env.AddEventHandler = function(name, callback) f.handlers[name] = callback end
    env.GetCurrentResourceName = function() return 'zyon_admin_menu' end
    env.GetGameTimer = function() return f.clock end
    env.CreateThread = function(fn) f.threads[#f.threads+1] = coroutine.create(fn) end
    env.Wait = function(ms) coroutine.yield(ms) end
    env.TriggerServerEvent = function(name, ...) f.sent[#f.sent+1] = {name=name,args={...}} end
    env.TriggerEvent = function(name)
        f.frameworkCalls[#f.frameworkCalls+1] = name
        assert(name == 'esx_ambulancejob:revive' or name == 'hospital:client:Revive', 'Arbitrary medical event')
        if f.heal then
            f.alive, f.esxDead, f.metadata.isdead, f.metadata.inlaststand = true, false, false, false
        end
    end
    env.PlayerPedId = function() return 10 end
    env.PlayerId = function() return 0 end
    env.DoesEntityExist = function(ped) return ped == 10 end
    env.IsEntityAPed = function(ped) return ped == 10 end
    env.IsPedDeadOrDying = function() return not f.alive end
    env.GetSelectedPedWeapon = function() return 1 end
    env.GetHashKey = function() return 1 end
    env.GetEntityCoords = function() return {x=1,y=2,z=3} end
    env.GetEntityHeading = function() return 90 end
    env.NetworkResurrectLocalPlayer = function() f.nativeCalls=f.nativeCalls+1; f.alive=true end
    env.SetEntityHealth = function(_, health) f.alive=health>0 end
    env.GetEntityMaxHealth = function() return 200 end
    env.ClearPedBloodDamage = function() end
    env.exports = {
        ['qb-core']={GetCoreObject=function() return {Functions={GetPlayerData=callable(function()
            return {metadata=f.metadata}
        end)}} end},
        ['es_extended']={getSharedObject=function() return {TriggerServerCallback=callable(function(name, callback)
            assert(name == 'esx_ambulancejob:getDeathStatus', 'Arbitrary medical callback')
            f.callbackCalls=f.callbackCalls+1
            if f.callbackRespond then callback(f.esxDead) end
        end)} end},
        ['esx_death']={GetDeathState=function() return {dead=f.esxDead} end}
    }
    loadScript(env, 'targeting.lua')
    loadScript(env, 'client.lua')
    function f:event(name, ...)
        self.env.source=65535
        self.events[name](...)
    end
    function f:step(milliseconds)
        self.clock=self.clock+(milliseconds or 0)
        -- Thread 1 is the ordinary render loop. These tests independently
        -- schedule medical work, including nested framework-dispatch threads.
        for index=2,#self.threads do
            local thread=self.threads[index]
            if coroutine.status(thread) ~= 'dead' then
                local ok, reason=coroutine.resume(thread)
                assert(ok, reason)
            end
        end
    end
    function f:finish()
        for _=1,100 do self:step(100) end
    end
    function f:ack()
        for index=#self.sent,1,-1 do
            if self.sent[index].name == 'zyon_admin_menu:reviveAck' then return self.sent[index].args end
        end
    end
    f:event('zyon_admin_menu:apply',defaults(),1)
    return f
end

local function serverFixture(options)
    options = options or {}
    local env = environment(options.resources)
    local f = {env=env,clock=0,config=options.config or 'auto',events={},handlers={},threads={},sent={},
        token=string.rep('a',64),metadata={isdead=true,inlaststand=true},esxState={ok=true,dead=true},
        exportRevives=0,reviveAccepted=options.reviveAccepted ~= false,bodyCount=0,bodies={}}
    env.GetCurrentResourceName=function() return 'zyon_admin_menu' end
    env.GetGameTimer=function() return f.clock end
    env.GetConvar=function(name,fallback)
        if name == 'zyon_menu_medical_provider' then return f.config end
        return fallback
    end
    env.GetPlayers=function() return {'7'} end
    env.GetPlayerName=function() return 'Administrator' end
    env.GetPlayerIdentifiers=function() return {'license:'..string.rep('b',40)} end
    env.IsPlayerAceAllowed=function(player,ace) return player==7 and ace=='zyon.modmenu' end
    env.RegisterNetEvent=function(name,callback) f.events[name]=callback end
    env.AddEventHandler=function(name,callback) f.handlers[name]=callback end
    env.CreateThread=function(fn) f.threads[#f.threads+1]=coroutine.create(fn) end
    env.Wait=function(ms) coroutine.yield(ms) end
    env.SetTimeout=function() end
    env.SetHttpHandler=function(fn) f.http=fn end
    env.TriggerClientEvent=function(name,player,...) f.sent[#f.sent+1]={name=name,player=player,args={...}} end
    env.json={encode=function(data) return data end,decode=function(body) return f.bodies[body],#body+1,nil end}
    local function patient() return {PlayerData={metadata=f.metadata}} end
    env.exports={
        ['qb-core']={GetPlayer=patient,GetCoreObject=function() return {Functions={GetPlayer=callable(patient)}} end},
        ['esx_death']={GetDeathState=function() return f.esxState end,Revive=function(_,player,reason)
            assert(player==7 and reason=='admin')
            f.exportRevives=f.exportRevives+1
            if f.reviveAccepted then f.esxState.dead=false end
            if f.onExport then f:onExport() end
            return f.reviveAccepted,true
        end}
    }
    loadScript(env,'server.lua')
    function f:event(name,...)
        self.env.source=7
        self.events[name](...)
    end
    function f:step(milliseconds)
        self.clock=self.clock+(milliseconds or 0)
        for index=1,#self.threads do
            local thread=self.threads[index]
            if coroutine.status(thread)~='dead' then assert(coroutine.resume(thread)) end
        end
    end
    function f:request(path,method,payload)
        self.bodyCount=self.bodyCount+1
        local body='body-'..self.bodyCount
        self.bodies[body]=payload or {}
        local result={}
        local request={path=path,method=method or 'GET',headers={Authorization='Bearer '..self.token,
            ['Content-Type']='application/json'},setDataHandler=function(fn) result.dataHandler=fn end,
            setCancelHandler=function() end}
        self.http(request,{writeHead=function(code) result.code=code end,send=function(data) result.data=data end})
        if result.dataHandler then result.dataHandler(body) end
        return result
    end
    function f:status() return self:request('/api/status').data end
    function f:dispatch()
        for index=#self.sent,1,-1 do
            if self.sent[index].name=='zyon_admin_menu:revive' then return self.sent[index] end
        end
    end
    function f:receipt(success,validation)
        local dispatch=assert(self:dispatch())
        self:event('zyon_admin_menu:reviveAck',dispatch.args[1],success,'client report',validation or 'medical',
            dispatch.args[2],dispatch.args[3])
    end
    f:event('zyon_admin_menu:pairRequest',f.token,1)
    return f
end

local passed=0
local function test(name,fn)
    local ok,reason=pcall(fn)
    assert(ok,name..': '..tostring(reason))
    passed=passed+1
    print('PASS '..name)
end

test('auto chooses standalone, ESX legacy, modern ESX and QBCore independently',function()
    local cases={
        {{},'standalone','native'},
        {{es_extended='started',esx_ambulancejob='started'},'esx','legacy'},
        {{es_extended='started',esx_death='started'},'esx','esx_death'},
        {{['qb-core']='started',['qb-ambulancejob']='started'},'qbcore','qbcore'}
    }
    for _,case in ipairs(cases) do
        local provider,variant=environment(case[1]).ZyonMedical.select('auto')
        assert(provider==case[2] and variant==case[3])
    end
end)
test('auto refuses both cores and incomplete medical dependencies',function()
    for _,resources in ipairs({{es_extended='started',['qb-core']='started'},
        {es_extended='started'},{['qb-core']='started'},{esx_ambulancejob='started'}}) do
        assert(environment(resources).ZyonMedical.select('auto')==nil)
    end
    local env=environment({es_extended='started',esx_ambulancejob='started'})
    env.dependencies={'esx_death'}
    assert(env.ZyonMedical.select('auto')==nil)
end)
test('auto cannot confuse stopped or restarting medical resources with missing resources',function()
    for _,state in ipairs({'stopped','starting','stopping','unknown'}) do
        for _,resource in ipairs({'es_extended','qb-core','esx_ambulancejob','qb-ambulancejob','esx_death'}) do
            local env=environment({[resource]=state})
            assert(env.ZyonMedical.select('auto')==nil)
            assert(env.ZyonMedical.select('standalone')=='standalone')
        end
        local env=environment({es_extended='started',esx_ambulancejob='started',esx_death=state})
        assert(env.ZyonMedical.select('auto')==nil and env.ZyonMedical.select('esx')==nil)
    end
end)
test('explicit selection requires its resources and never guesses custom names',function()
    local env=environment({es_extended='started',esx_ambulancejob='started',['qb-core']='started',['qb-ambulancejob']='started'})
    assert(env.ZyonMedical.select('qbcore')=='qbcore' and env.ZyonMedical.select('esx')=='esx')
    assert(environment().ZyonMedical.select('esx')==nil)
    assert(environment().ZyonMedical.select('qbcore')==nil)
    assert(env.ZyonMedical.select('custom:hospital')==nil)
    assert(environment().ZyonMedical.select('standalone')=='standalone')
end)
test('unavailable resource-state API cannot silently choose native auto',function()
    local env=environment()
    env.GetResourceState=false
    assert(env.ZyonMedical.select('auto')==nil)
end)
test('server reports provider and removes revive capability for missing integration',function()
    local f=serverFixture({resources={['qb-core']='started'}})
    local status=f:status()
    assert(status.medicalProvider=='' and status.medicalMessage:find('qb%-ambulancejob'))
    assert(not has(status.capabilities,'player.revive') and has(status.capabilities,'player.suicide'))
    assert(f:request('/api/revive','POST',{}).code==503 and not f:dispatch())
end)
test('a fresh revive during complete framework restart never falls back to native',function()
    for _,resources in ipairs({{es_extended='started',esx_death='started'},
        {['qb-core']='started',['qb-ambulancejob']='started'}}) do
        local f=serverFixture({resources=resources})
        assert(f:status().medicalProvider~='standalone')
        for resource in pairs(resources) do
            resources[resource]='stopped'
            f.handlers.onResourceStop(resource)
        end
        local status=f:status()
        assert(status.medicalProvider=='' and not has(status.capabilities,'player.revive'))
        assert(f:request('/api/revive','POST',{}).code==503 and not f:dispatch() and f.exportRevives==0)
    end
end)
test('standalone synchronous behavior keeps old event compatibility',function()
    local f=clientFixture()
    f:event('zyon_admin_menu:revive',1,1)
    assert(f.nativeCalls==1 and f:ack()[2]==true and f:ack()[4]=='native')
    assert(#f.frameworkCalls==0)
end)
test('QBCore runs framework flow when ped is already natively alive',function()
    local f=clientFixture({alive=true,resources={['qb-core']='started',['qb-ambulancejob']='started'}})
    f:event('zyon_admin_menu:revive',1,1,'qbcore','qbcore')
    f:finish()
    assert(#f.frameworkCalls==1 and f.frameworkCalls[1]=='hospital:client:Revive')
    assert(f.nativeCalls==0 and f:ack()[2]==true and f:ack()[4]=='medical')
end)
test('QBCore cannot acknowledge native life while medical flags remain dead',function()
    local f=clientFixture({alive=true,heal=false,resources={['qb-core']='started',['qb-ambulancejob']='started'}})
    f:event('zyon_admin_menu:revive',1,1,'qbcore','qbcore')
    f:finish()
    assert(f:ack()[2]==false and f:ack()[3]:find('estado médico'))
end)
test('missing medical resource never triggers framework or native fallback',function()
    local f=clientFixture({resources={['qb-core']='started'}})
    f:event('zyon_admin_menu:revive',1,1,'qbcore','qbcore')
    f:finish()
    assert(f:ack()[2]==false and #f.frameworkCalls==0 and f.nativeCalls==0)
end)
test('stale revision and resource stop fence pending framework work',function()
    for _,stop in ipairs({false,true}) do
        local f=clientFixture({resources={['qb-core']='started',['qb-ambulancejob']='started'}})
        f:event('zyon_admin_menu:revive',1,1,'qbcore','qbcore')
        f:step(100)
        if stop then f.handlers.onResourceStop('qb-ambulancejob')
        else f:event('zyon_admin_menu:apply',defaults(),2) end
        f:finish()
        assert(f:ack()[2]==false and #f.frameworkCalls==0 and f.nativeCalls==0)
    end
end)
test('local source cannot forge framework revive events',function()
    local f=clientFixture({resources={['qb-core']='started',['qb-ambulancejob']='started'}})
    f.env.source=0
    f.events['zyon_admin_menu:revive'](1,1,'qbcore','qbcore')
    f:finish()
    assert(not f:ack() and #f.frameworkCalls==0)
end)
test('ESX legacy preflight and fresh callback confirm its supported lifecycle',function()
    local f=clientFixture({resources={es_extended='started',esx_ambulancejob='started'}})
    f:event('zyon_admin_menu:revive',1,1,'esx','legacy')
    f:finish()
    assert(f.callbackCalls>=2 and #f.frameworkCalls==1 and f:ack()[2]==true)
    assert(f.nativeCalls==0)
end)
test('ESX without its legacy callback times out before any revive effect',function()
    local f=clientFixture({callbackRespond=false,resources={es_extended='started',esx_ambulancejob='started'}})
    f:event('zyon_admin_menu:revive',1,1,'esx','legacy')
    f:finish()
    assert(f:ack()[2]==false and f:ack()[3]:find('não foi disparado'))
    assert(#f.frameworkCalls==0 and f.nativeCalls==0 and not f.alive)
end)
test('modern ESX client observes protected server lifecycle without legacy dispatch',function()
    local f=clientFixture({alive=true,resources={es_extended='started',esx_death='started'}})
    f.esxDead=false
    f:event('zyon_admin_menu:revive',1,1,'esx','esx_death')
    f:finish()
    assert(f:ack()[2]==true and #f.frameworkCalls==0 and f.nativeCalls==0)
end)
test('server dispatch includes selected provider, revision and bounded budget',function()
    local f=serverFixture({resources={['qb-core']='started',['qb-ambulancejob']='started'}})
    local response=f:request('/api/revive','POST',{})
    local dispatch=f:dispatch()
    assert(response.code==200 and dispatch.args[1]==response.data.ticket)
    assert(dispatch.args[2]==f:status().desiredRevision and dispatch.args[3]=='qbcore'
        and dispatch.args[4]=='qbcore' and dispatch.args[5]<=8000)
end)
test('server checks QBCore flags after client medical ACK',function()
    local f=serverFixture({resources={['qb-core']='started',['qb-ambulancejob']='started'}})
    f:request('/api/revive','POST',{})
    f:receipt(true)
    f:step(100)
    assert(not f:status().message:find('confirmados no servidor'))
    f.metadata.isdead,f.metadata.inlaststand=false,false
    f:step(100)
    assert(f:status().message:find('confirmados no servidor'))
end)
test('client native-only ACK cannot certify a QBCore medical revive',function()
    local f=serverFixture({resources={['qb-core']='started',['qb-ambulancejob']='started'}})
    f:request('/api/revive','POST',{})
    f.metadata.isdead,f.metadata.inlaststand=false,false
    f:receipt(true,'native')
    f:step(100)
    assert(not f:status().message:find('confirmados no servidor'))
end)
test('server expires pending medical verification at ten seconds',function()
    local f=serverFixture({resources={['qb-core']='started',['qb-ambulancejob']='started'}})
    f:request('/api/revive','POST',{})
    f:receipt(true)
    f:step(100)
    f:step(10000)
    assert(f:status().message:find('10 segundos'))
end)
test('stale server revision rejects medical ACK despite healthy flags',function()
    local f=serverFixture({resources={['qb-core']='started',['qb-ambulancejob']='started'}})
    f:request('/api/revive','POST',{})
    f:request('/api/settings','POST',{settings=defaults()})
    f.metadata.isdead,f.metadata.inlaststand=false,false
    f:receipt(true)
    assert(f:status().message:find('cancelado'))
end)
test('modern ESX invokes only official server export then validates cached medical state',function()
    local f=serverFixture({resources={es_extended='started',esx_death='started'}})
    f:request('/api/revive','POST',{})
    f:step(100)
    assert(f.exportRevives==1 and f:dispatch().args[4]=='esx_death')
    f:receipt(true)
    f:step(100)
    assert(f:status().message:find('estado médico confirmado no servidor'))
end)
test('modern ESX missing cached state is an error and never falls back',function()
    local f=serverFixture({resources={es_extended='started',esx_death='started'}})
    f.esxState=nil
    f:request('/api/revive','POST',{})
    f:step(100)
    assert(f.exportRevives==0 and not f:dispatch() and f:status().message:find('estado médico'))
end)
test('medical restart or revision change while ESX export yields fences completion',function()
    local f=serverFixture({resources={es_extended='started',esx_death='started'}})
    f.onExport=function() coroutine.yield() end
    f:request('/api/revive','POST',{})
    f:step(100)
    f.env.source=0
    f.handlers.onResourceStop('esx_death')
    f:step(100)
    assert(not f:dispatch() and f:status().message:find('interrompido'))
end)
test('yielding medical getter cannot reset the receipt deadline or trigger expired work',function()
    local f=clientFixture({alive=true,resources={['qb-core']='started',['qb-ambulancejob']='started'}})
    f.env.exports['qb-core'].GetCoreObject=function()
        coroutine.yield()
        return {Functions={GetPlayerData=callable(function() return {metadata={isdead=false,inlaststand=false}} end)}}
    end
    f:event('zyon_admin_menu:revive',1,1,'qbcore','qbcore')
    f:step(100)
    f:step(8000)
    assert(f:ack()[2]==false and #f.frameworkCalls==0 and f.nativeCalls==0)

    local g=clientFixture({alive=true,resources={['qb-core']='started',['qb-ambulancejob']='started'}})
    g.env.exports['qb-core'].GetCoreObject=function()
        return {Functions={GetPlayerData=callable(function()
            coroutine.yield()
            return {metadata={isdead=false,inlaststand=false}}
        end)}}
    end
    g:event('zyon_admin_menu:revive',1,1,'qbcore','qbcore')
    g:step(100)
    g:step(8000)
    assert(g:ack()[2]==false and #g.frameworkCalls==0 and g.nativeCalls==0)
end)
print(('PASS %d medical contract tests'):format(passed))
