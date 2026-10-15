-- Lua / fivem/zyon_admin_menu/client.lua / FiveM Lua 5.4 / GTA V authorised admin resource.
-- Native contracts: citizenfx/natives and citizenfx/fivem/ext/native-decls.
-- Server-only event convention: citizenfx/fivem-docs/content/docs/developers/server-security.md.
local T = ZyonTargeting
local M = ZyonMedical
local MEDICAL_WAIT_MS = 8000
local medicalEpoch = 0
local LEASE_MS, ENUMERATE_MS, MAX_ESP_ENTITIES = 15000, 250, 64
local settings, revision, lastLease, lastReviveTicket, lastSuicideTicket = nil, -1, nil, 0, 0
local trackedPed, invincibility, flight, visibility = nil, nil, nil, nil
local weaponStates, ammoStates, candidates = {}, {}, {}
local adminIds, mutedPlayers, lastVoiceUpdate = {}, {}, nil
local lastEnumeration, failedRevision = nil, nil
local boneIds = { Head = 0x796E, Neck = 0x9995, Chest = 0x60F2 }
local activationControls = { RMB = 25, LMB = 24, Alt = 19, Shift = 21, Ctrl = 36 }
local skeletonEdges = {
    {0x796E, 0x9995}, {0x9995, 0x60F2}, {0x60F2, 0x2E28},
    {0x60F2, 0xB1C5}, {0xB1C5, 0xEEEB}, {0xEEEB, 0x49D9},
    {0x60F2, 0x9D4D}, {0x9D4D, 0x6E5C}, {0x6E5C, 0xDEAD},
    {0x2E28, 0xE39F}, {0xE39F, 0xF9BB}, {0xF9BB, 0x3779},
    {0x2E28, 0xCA72}, {0xCA72, 0x9000}, {0x9000, 0xCC4D}
}

local function defaults()
    local function aim()
        return { enabled = false, onlyVisible = true, onlyPlayers = true,
            targetBone = 'Head', activationKey = 'RMB', smooth = 1, drawFov = false, fov = 100 }
    end
    return { aim = aim(), silent = aim(),
        weapons = { noRecoil = false, noSpread = false, freezeAmmo = false },
        player = { godMode = false, noclip = false, noclipSpeed = 5, invisible = false, muteVoice = false },
        esp = { enabled = false, boxes = true, skeletons = false, names = true, distance = true,
            onlyPlayers = true, onlyVisible = true, maxDistance = 250, admins = false } }
end
settings = defaults()

local function elapsed(now, before)
    return before and (now - before) % 4294967296 or math.huge
end

local function validPed(ped)
    return type(ped) == 'number' and ped ~= 0 and DoesEntityExist(ped) and IsEntityAPed(ped)
end

local function isAlive(ped)
    return validPed(ped) and not IsPedDeadOrDying(ped, true)
end

local function nativeAvailable(name)
    return type(_G[name]) == 'function'
end

local function checkedSettings(input)
    if type(input) ~= 'table' then return nil, 'Settings must be an object.' end
    local expected = defaults()
    for group, fields in pairs(expected) do
        if type(input[group]) ~= 'table' then return nil, 'Missing settings group: ' .. group end
        for key, default in pairs(fields) do
            if type(input[group][key]) ~= type(default) then
                return nil, 'Invalid setting type: ' .. group .. '.' .. key
            end
        end
        for key in pairs(input[group]) do
            if fields[key] == nil then return nil, 'Unknown setting: ' .. group .. '.' .. tostring(key) end
        end
    end
    for key in pairs(input) do
        if expected[key] == nil then return nil, 'Unknown settings group.' end
    end
    for _, group in ipairs({'aim', 'silent'}) do
        local s = input[group]
        if not boneIds[s.targetBone] or not activationControls[s.activationKey] then
            return nil, 'Invalid targeting option.'
        end
        if not T.finite(s.smooth) or s.smooth < 1 or s.smooth > 20
            or not T.finite(s.fov) or s.fov < 10 or s.fov > 500 then
            return nil, 'Invalid targeting range.'
        end
    end
    if input.silent.enabled then return nil, 'Silent aim is not supported by this resource.' end
    if not T.finite(input.esp.maxDistance) or input.esp.maxDistance < 25 or input.esp.maxDistance > 1000 then
        return nil, 'Invalid ESP range.'
    end
    if not T.finite(input.player.noclipSpeed) or input.player.noclipSpeed % 1 ~= 0
        or input.player.noclipSpeed < 1 or input.player.noclipSpeed > 50 then
        return nil, 'Invalid noclip speed.'
    end
    if input.player.invisible and (not nativeAvailable('IsEntityVisible') or not nativeAvailable('SetEntityVisible')) then
        return nil, 'This FiveM build has no entity-visibility getter/setter.'
    end
    if input.player.muteVoice and not nativeAvailable('MumbleSetVolumeOverride') then
        return nil, 'This FiveM build has no supported local voice-volume override.'
    end
    if input.weapons.noRecoil and (not nativeAvailable('GetWeaponRecoilShakeAmplitude')
        or not nativeAvailable('SetWeaponRecoilShakeAmplitude')) then
        return nil, 'This FiveM build has no recoil-amplitude getter/setter.'
    end
    if input.weapons.noSpread and (not nativeAvailable('GetWeaponAccuracySpread')
        or not nativeAvailable('SetWeaponAccuracySpread')) then
        return nil, 'This FiveM build has no accuracy-spread getter/setter.'
    end
    if input.player.noclip and not nativeAvailable('IsEntityPositionFrozen') then
        return nil, 'This FiveM build has no frozen-position getter.'
    end
    if input.player.godMode and (not nativeAvailable('GetEntityCanBeDamaged')
        or not nativeAvailable('SetEntityCanBeDamaged')) then
        return nil, 'This FiveM build has no entity-damageability getter/setter.'
    end
    -- Copy primitives rather than keeping an event argument shared by another script.
    for group, fields in pairs(expected) do
        for key in pairs(fields) do expected[group][key] = input[group][key] end
    end
    return expected
end

local function restoreFlight()
    if not flight then return end
    if validPed(flight.ped) then
        SetEntityVelocity(flight.ped, 0.0, 0.0, 0.0)
        SetEntityCollision(flight.ped, not flight.collisionDisabled, true)
        FreezeEntityPosition(flight.ped, flight.frozen)
    end
    flight = nil
end

local function restoreInvincibility()
    if not invincibility then return end
    -- Use the precisely observable damageability property, leaving existing
    -- invincibility/ragdoll/proof flags alone. Direct scripted health changes
    -- and RP framework death states are outside this native's contract.
    if validPed(invincibility.ped) and not GetEntityCanBeDamaged(invincibility.ped) then
        SetEntityCanBeDamaged(invincibility.ped, invincibility.canBeDamaged)
    end
    invincibility = nil
end

local function restoreVisibility()
    if not visibility then return end
    if validPed(visibility.ped) and not IsEntityVisible(visibility.ped) then
        SetEntityVisible(visibility.ped, visibility.visible, false)
    end
    visibility = nil
end

local function restoreVoice()
    if nativeAvailable('MumbleSetVolumeOverride') then
        for player in pairs(mutedPlayers) do MumbleSetVolumeOverride(player, -1.0) end
    end
    mutedPlayers, lastVoiceUpdate = {}, nil
end

local function updateVoice(now)
    if not settings.player.muteVoice then restoreVoice(); return end
    if elapsed(now, lastVoiceUpdate) < ENUMERATE_MS then return end
    lastVoiceUpdate = now
    local present = {}
    for _, player in ipairs(GetActivePlayers()) do
        if player ~= PlayerId() then present[player] = GetPlayerServerId(player) end
    end
    for player, serverId in pairs(mutedPlayers) do
        if present[player] ~= serverId then
            MumbleSetVolumeOverride(player, -1.0)
            mutedPlayers[player] = nil
        end
    end
    for player, serverId in pairs(present) do
        MumbleSetVolumeOverride(player, 0.0)
        mutedPlayers[player] = serverId
    end
end

local function restoreWeaponProperty(entry, name, getter, setter)
    if entry[name] ~= nil then
        -- Avoid overwriting an observable newer change made by another resource.
        if getter(entry.hash) == 0.0 then setter(entry.hash, entry[name]) end
        entry[name] = nil
    end
end

local function restoreWeapons()
    for _, entry in pairs(weaponStates) do
        restoreWeaponProperty(entry, 'recoil', GetWeaponRecoilShakeAmplitude, SetWeaponRecoilShakeAmplitude)
        restoreWeaponProperty(entry, 'spread', GetWeaponAccuracySpread, SetWeaponAccuracySpread)
    end
    weaponStates, ammoStates = {}, {}
end

local function releaseEffects()
    restoreFlight()
    restoreInvincibility()
    restoreVisibility()
    restoreVoice()
    restoreWeapons()
    trackedPed, candidates, lastEnumeration = nil, {}, nil
end

local function stopEffects()
    settings = defaults()
    lastLease = nil
    -- Run cleanup independently: a failed native must not skip other restoration.
    for _, cleanup in ipairs({restoreFlight, restoreInvincibility, restoreVisibility, restoreVoice, restoreWeapons}) do
        local ok, err = pcall(cleanup)
        if not ok then print('[zyon_admin_menu] Cleanup failed: ' .. tostring(err)) end
    end
    trackedPed, candidates, lastEnumeration = nil, {}, nil
    adminIds = {}
end

local function updatePedEffects(ped, advance)
    if ped ~= trackedPed then
        releaseEffects()
        trackedPed = ped
    end
    if settings.player.godMode and validPed(ped) then
        if not invincibility then
            invincibility = { ped = ped, canBeDamaged = GetEntityCanBeDamaged(ped) }
            SetEntityCanBeDamaged(ped, false)
        end
    else
        restoreInvincibility()
    end
    if settings.player.invisible and validPed(ped) then
        if not visibility then visibility = {ped = ped, visible = IsEntityVisible(ped)} end
        SetEntityVisible(ped, false, false)
    else
        restoreVisibility()
    end
    local canFly = settings.player.noclip and isAlive(ped)
        and not IsPedInAnyVehicle(ped, false) and not IsEntityAttached(ped)
        and NetworkHasControlOfEntity(ped)
    if not canFly then restoreFlight() return end
    if not flight then
        flight = { ped = ped, frozen = IsEntityPositionFrozen(ped),
            collisionDisabled = GetEntityCollisionDisabled(ped) }
        FreezeEntityPosition(ped, true)
        SetEntityCollision(ped, false, false)
    end
    -- HTTP settings can arrive between rendered frames. They update properties
    -- only; advancing here would add a movement step for every slider packet.
    if not advance then return end
    for _, control in ipairs({30, 31, 22, 36, 21, 24, 25}) do DisableControlAction(0, control, true) end
    local forward = -GetDisabledControlNormal(0, 31)
    local right = GetDisabledControlNormal(0, 30)
    local up = IsDisabledControlPressed(0, 22) and 1.0 or 0.0
    local down = IsDisabledControlPressed(0, 36) and 1.0 or 0.0
    local rotation = GetGameplayCamRot(2)
    local x, y, z = T.flightDirection(rotation.x, rotation.z, forward, right, up - down)
    local speed = settings.player.noclipSpeed * (IsDisabledControlPressed(0, 21) and 3.0 or 1.0)
    local dt = T.clamp(GetFrameTime(), 0.001, 0.1)
    local position = GetEntityCoords(ped)
    SetEntityVelocity(ped, 0.0, 0.0, 0.0)
    if x ~= 0.0 or y ~= 0.0 or z ~= 0.0 then
        SetEntityCoordsNoOffset(ped, position.x + x * speed * dt,
            position.y + y * speed * dt, position.z + z * speed * dt, true, true, false)
    end
end

local function updateWeapons(ped)
    for _, entry in pairs(weaponStates) do
        if not settings.weapons.noRecoil then
            restoreWeaponProperty(entry, 'recoil', GetWeaponRecoilShakeAmplitude, SetWeaponRecoilShakeAmplitude)
        end
        if not settings.weapons.noSpread then
            restoreWeaponProperty(entry, 'spread', GetWeaponAccuracySpread, SetWeaponAccuracySpread)
        end
    end
    if not settings.weapons.freezeAmmo then ammoStates = {} end
    if not isAlive(ped) then return end
    local hash = GetSelectedPedWeapon(ped)
    if hash == GetHashKey('WEAPON_UNARMED') or not HasPedGotWeapon(ped, hash, false) then return end
    local entry = weaponStates[hash]
    if not entry then entry = { hash = hash } weaponStates[hash] = entry end
    if settings.weapons.noRecoil and entry.recoil == nil then
        local initial = GetWeaponRecoilShakeAmplitude(hash)
        if not T.finite(initial) then error('Invalid native weapon recoil amplitude.') end
        entry.recoil = initial
        SetWeaponRecoilShakeAmplitude(hash, 0.0)
    end
    if settings.weapons.noSpread and entry.spread == nil then
        local initial = GetWeaponAccuracySpread(hash)
        if not T.finite(initial) then error('Invalid native weapon accuracy spread.') end
        entry.spread = initial
        SetWeaponAccuracySpread(hash, 0.0)
    end
    if not settings.weapons.freezeAmmo then return end
    local total = GetAmmoInPedWeapon(ped, hash)
    local hasClip, clip = GetAmmoInClip(ped, hash)
    local ammo = ammoStates[hash]
    if not ammo then
        ammo = { total = total, clip = hasClip and clip or nil }
        ammoStates[hash] = ammo
    end
    -- No infinite-ammo flags: there is no verified native getter for their old
    -- state. Replenish observed consumption and preserve observed ammo pickups.
    if total > ammo.total then ammo.total = total end
    if hasClip and (ammo.clip == nil or clip > ammo.clip) then ammo.clip = clip end
    if total < ammo.total then SetPedAmmo(ped, hash, ammo.total) end
    if hasClip and ammo.clip and clip < ammo.clip then
        SetAmmoInClip(ped, hash, math.min(ammo.clip, ammo.total))
    end
end

local function enumerateCandidates(localPed, now)
    if elapsed(now, lastEnumeration) < ENUMERATE_MS then return end
    lastEnumeration = now
    local origin, records, byPed = GetEntityCoords(localPed), {}, {}
    for _, player in ipairs(GetActivePlayers()) do
        local ped = GetPlayerPed(player)
        if ped ~= localPed and validPed(ped) then
            local record = { ped = ped, player = player, isPlayer = true,
                distance = T.distance(origin, GetEntityCoords(ped)) }
            records[#records + 1], byPed[ped] = record, true
        end
    end
    if (settings.aim.enabled and not settings.aim.onlyPlayers)
        or (settings.esp.enabled and not settings.esp.onlyPlayers) then
        for _, ped in ipairs(GetGamePool('CPed')) do
            if ped ~= localPed and not byPed[ped] and validPed(ped) and not IsPedAPlayer(ped) then
                records[#records + 1] = { ped = ped, isPlayer = false,
                    distance = T.distance(origin, GetEntityCoords(ped)) }
            end
        end
    end
    table.sort(records, function(a, b) return a.distance < b.distance end)
    candidates = records
end

local function bonePosition(ped, id, offsetZ)
    if GetPedBoneIndex(ped, id) == -1 then return nil end
    return GetPedBoneCoords(ped, id, 0.0, 0.0, offsetZ or 0.0)
end

local function projected(position)
    if not position then return false end
    return GetScreenCoordFromWorldCoord(position.x, position.y, position.z)
end

local function line2d(x1, y1, x2, y2, r, g, b, alpha)
    -- Project a line onto the rendering camera's near screen plane. This gives
    -- a native-drawn 2D overlay without injection, memory reads, or NUI assets.
    local a, na = GetWorldCoordFromScreenCoord(x1, y1)
    local c, nc = GetWorldCoordFromScreenCoord(x2, y2)
    DrawLine(a.x + na.x, a.y + na.y, a.z + na.z,
        c.x + nc.x, c.y + nc.y, c.z + nc.z, r, g, b, alpha)
end

local function drawFov(radius, width, height)
    local steps = 96
    local oldX, oldY = 0.5 + radius / width, 0.5
    for i = 1, steps do
        local angle = i * 2.0 * math.pi / steps
        local x, y = 0.5 + math.cos(angle) * radius / width, 0.5 + math.sin(angle) * radius / height
        line2d(oldX, oldY, x, y, 220, 240, 255, 200)
        oldX, oldY = x, y
    end
end

local function safeName(text)
    return tostring(text):gsub('[~%c]', ''):sub(1, 70)
end

local function drawText(x, y, text)
    SetTextFont(0)
    SetTextScale(0.0, 0.28)
    SetTextColour(220, 240, 255, 230)
    SetTextCentre(true)
    SetTextOutline()
    BeginTextCommandDisplayText('STRING')
    AddTextComponentSubstringPlayerName(safeName(text))
    EndTextCommandDisplayText(x, y)
end

local function drawEspRecord(record, localPed, width, height)
    local options, ped = settings.esp, record.ped
    if not isAlive(ped) or (options.onlyPlayers and not record.isPlayer) then return false end
    if record.isPlayer and (not IsPedAPlayer(ped) or GetPlayerPed(record.player) ~= ped) then return false end
    local distance = T.distance(GetEntityCoords(localPed), GetEntityCoords(ped))
    if distance > options.maxDistance then return false end
    if options.onlyVisible and not HasEntityClearLosToEntity(localPed, ped, 17) then return false end
    local head = bonePosition(ped, boneIds.Head, 0.25)
    local position = GetEntityCoords(ped)
    local hOk, hx, hy = projected(head)
    local fOk, fx, fy = GetScreenCoordFromWorldCoord(position.x, position.y, position.z - 1.0)
    if not hOk or not fOk or hx < 0.0 or hx > 1.0 or hy < 0.0 or hy > 1.0 then return false end
    local screenHeight = math.abs(fy - hy)
    if screenHeight < 0.0005 then return false end
    if options.boxes then
        local boxWidth = screenHeight * height / width * 0.38
        local left, right = hx - boxWidth * 0.5, hx + boxWidth * 0.5
        line2d(left, hy, right, hy, 34, 180, 230, 220)
        line2d(right, hy, right, fy, 34, 180, 230, 220)
        line2d(right, fy, left, fy, 34, 180, 230, 220)
        line2d(left, fy, left, hy, 34, 180, 230, 220)
    end
    if options.skeletons then
        for _, edge in ipairs(skeletonEdges) do
            local ok1, x1, y1 = projected(bonePosition(ped, edge[1]))
            local ok2, x2, y2 = projected(bonePosition(ped, edge[2]))
            if ok1 and ok2 then line2d(x1, y1, x2, y2, 230, 240, 250, 200) end
        end
    end
    local label = ''
    if options.names then
        label = record.isPlayer and ('#' .. GetPlayerServerId(record.player) .. ' ' .. (GetPlayerName(record.player) or 'Player')) or 'NPC'
    end
    if options.admins and record.isPlayer and adminIds[GetPlayerServerId(record.player)] then
        label = 'ADM' .. (label ~= '' and ' | ' .. label or '')
    end
    if options.distance then label = label .. (label ~= '' and ' | ' or '') .. string.format('%.0fm', distance) end
    if label ~= '' then drawText(hx, math.min(0.98, fy + 3.0 / height), label) end
    return true
end

local function updateVisualsAndAim(localPed, now)
    if IsPauseMenuActive() or not IsGameplayCamRendering() or not isAlive(localPed) then return end
    local width, height = GetActualScreenResolution()
    if width <= 0 or height <= 0 then return end
    if settings.aim.drawFov then drawFov(settings.aim.fov, width, height) end
    -- The unsupported silent panel only supports a visual FOV preview.
    if settings.silent.drawFov then drawFov(settings.silent.fov, width, height) end
    if not settings.aim.enabled and not settings.esp.enabled then return end
    enumerateCandidates(localPed, now)
    if settings.esp.enabled then
        local rendered = 0
        for _, record in ipairs(candidates) do
            if drawEspRecord(record, localPed, width, height) then rendered = rendered + 1 end
            if rendered >= MAX_ESP_ENTITIES then break end
        end
    end
    if not settings.aim.enabled or flight or IsPedInAnyVehicle(localPed, false) then return end
    if not IsControlPressed(0, activationControls[settings.aim.activationKey]) then return end
    local choices = {}
    for _, record in ipairs(candidates) do
        if isAlive(record.ped) then
            local isPlayer = IsPedAPlayer(record.ped)
            local position = bonePosition(record.ped, boneIds[settings.aim.targetBone])
            local ok, x, y = projected(position)
            choices[#choices + 1] = { ped = record.ped, exists = true, dead = false,
                isPlayer = isPlayer, visible = not settings.aim.onlyVisible
                    or HasEntityClearLosToEntity(localPed, record.ped, 17),
                projected = ok, screenX = x, screenY = y, position = position }
        end
    end
    local target = T.select(choices, settings.aim, localPed, width, height)
    if not target then return end
    local desiredPitch, desiredYaw = T.angles(GetGameplayCamCoord(), target.position)
    if not desiredPitch then return end
    local rotation = GetGameplayCamRot(2)
    local pitchStep, yawStep = T.cameraStep(rotation.x, rotation.z,
        desiredPitch, desiredYaw, settings.aim.smooth, GetFrameTime())
    SetGameplayCamRelativeHeading(T.wrapDegrees(GetGameplayCamRelativeHeading() + yawStep))
    SetGameplayCamRelativePitch(T.clamp(GetGameplayCamRelativePitch() + pitchStep, -89.0, 89.0), 1.0)
end

RegisterNetEvent('zyon_admin_menu:apply', function(input, nextRevision)
    if source ~= 65535 then return end
    if not T.finite(nextRevision) or nextRevision % 1 ~= 0 or nextRevision < 0 or nextRevision < revision then return end
    local value, message = checkedSettings(input)
    if not value then
        stopEffects()
        revision, failedRevision = nextRevision, nextRevision
        TriggerServerEvent('zyon_admin_menu:ack', nextRevision, false, message)
        return
    end
    if nextRevision == failedRevision then return end
    lastLease = GetGameTimer()
    if nextRevision ~= revision then
        local candidatesChanged = settings.aim.enabled ~= value.aim.enabled
            or settings.esp.enabled ~= value.esp.enabled
            or settings.aim.onlyPlayers ~= value.aim.onlyPlayers
            or settings.esp.onlyPlayers ~= value.esp.onlyPlayers
        settings, revision = value, nextRevision
        if candidatesChanged then lastEnumeration = nil end
        local ok, err = pcall(function()
            local ped = PlayerPedId()
            updatePedEffects(ped)
            updateWeapons(ped)
            updateVoice(GetGameTimer())
        end)
        if not ok then
            stopEffects()
            failedRevision = nextRevision
            TriggerServerEvent('zyon_admin_menu:ack', nextRevision, false, tostring(err):sub(1, 120))
            return
        end
    end
    if not settings.esp.admins then adminIds = {} end
    TriggerServerEvent('zyon_admin_menu:clientCapabilities', {muteVoice = nativeAvailable('MumbleSetVolumeOverride')})
    TriggerServerEvent('zyon_admin_menu:ack', nextRevision, true, 'Native settings applied.')
end)

RegisterNetEvent('zyon_admin_menu:admins', function(ids)
    if source ~= 65535 or type(ids) ~= 'table' then return end
    if not lastLease or elapsed(GetGameTimer(), lastLease) > LEASE_MS or not settings.esp.admins then
        adminIds = {}; return
    end
    local accepted, count = {}, 0
    for _, id in pairs(ids) do
        count = count + 1
        if count > 2048 or not T.finite(id) or id < 1 or id % 1 ~= 0 then return end
        accepted[id] = true
    end
    adminIds = accepted
end)

local function currentRevive(expectedRevision, provider, variant, epoch)
    if not T.finite(expectedRevision) or expectedRevision ~= revision or failedRevision == revision
        or not lastLease or elapsed(GetGameTimer(), lastLease) > LEASE_MS then
        error('Reviver não corresponde mais à revisão e à sessão autorizadas.', 0)
    end
    if epoch ~= medicalEpoch then error('Um recurso médico foi interrompido durante o pedido de reviver.', 0) end
    local ready, reason = M.available(provider, variant)
    if not ready then error(reason, 0) end
end

local function medicalClientGetter(provider, variant)
    local function callable(value)
        if type(value) == 'function' then return true end
        local meta = type(value) == 'table' and getmetatable(value)
        return type(meta) == 'table' and type(meta.__call) == 'function'
    end
    if provider == 'qbcore' then
        local core = exports['qb-core']:GetCoreObject()
        if type(core) ~= 'table' or type(core.Functions) ~= 'table'
            or not callable(core.Functions.GetPlayerData) then
            error('QBCore não expõe o getter oficial de PlayerData; reviver bloqueado.', 0)
        end
        return function()
            local data = core.Functions.GetPlayerData()
            local metadata = type(data) == 'table' and data.metadata
            return type(metadata) == 'table' and metadata.isdead == false and metadata.inlaststand == false
        end
    end
    if variant == 'esx_death' then
        return function()
            local data = exports['esx_death']:GetDeathState()
            return type(data) == 'table' and data.dead == false
        end
    end
    local esx = exports['es_extended']:getSharedObject()
    if type(esx) ~= 'table' or not callable(esx.TriggerServerCallback) then
        error('ESX legado não expõe o callback oficial de estado de morte; reviver bloqueado.', 0)
    end
    local observed, requested, queriedAt, generation = nil, false, nil, 0
    local function getter(now)
        if not requested and elapsed(now, queriedAt) >= 250 then
            requested, queriedAt = true, now
            -- The callback belongs to the legacy ambulance implementation. If
            -- this server removed it, the deadline reports unconfirmed medical
            -- state; a native-alive ped is never presented as medical success.
            local queryGeneration = generation
            esx.TriggerServerCallback('esx_ambulancejob:getDeathStatus', function(dead)
                if queryGeneration == generation then observed, requested = dead, false end
            end)
        end
        return observed == false or observed == 0
    end
    local function observedState()
        return type(observed) == 'boolean' or observed == 0 or observed == 1
    end
    local function resetObservation()
        observed, requested, queriedAt, generation = nil, false, nil, generation + 1
    end
    return getter, observedState, resetObservation
end

RegisterNetEvent('zyon_admin_menu:revive', function(ticket, expectedRevision, provider, variant, waitBudget)
    if source ~= 65535 then return end
    if not T.finite(ticket) or ticket % 1 ~= 0 or ticket <= lastReviveTicket then return end
    lastReviveTicket = ticket
    -- Missing provider is accepted only for older standalone menu events. The
    -- current server always dispatches an explicit provider and variant.
    provider, variant = provider or 'standalone', variant or 'native'
    waitBudget = waitBudget or MEDICAL_WAIT_MS
    local epoch, receivedAt, finished = medicalEpoch, GetGameTimer(), false
    local function requestCurrent()
        currentRevive(expectedRevision, provider, variant, epoch)
        if ticket ~= lastReviveTicket or finished then
            error('O pedido de reviver foi substituído ou encerrado.', 0)
        end
    end
    local function acknowledge(ok, message, validation)
        TriggerServerEvent('zyon_admin_menu:reviveAck', ticket, ok, tostring(message):sub(1, 120),
            validation, expectedRevision, provider)
    end
    if provider ~= 'standalone' and provider ~= 'esx' and provider ~= 'qbcore' then
        acknowledge(false, 'Integração médica desconhecida.', 'unconfirmed'); return
    end
    if provider ~= 'standalone' then
        CreateThread(function()
            local ok, message = pcall(function()
                requestCurrent()
                if not validPed(PlayerPedId()) then error('O personagem está indisponível.', 0) end
                if not T.finite(waitBudget) or waitBudget % 1 ~= 0 or waitBudget < 1 or waitBudget > MEDICAL_WAIT_MS then
                    error('Prazo médico inválido.', 0)
                end
                local getter, observedState, resetObservation = medicalClientGetter(provider, variant)
                requestCurrent()
                local started, dispatchError, attempted = receivedAt, nil, variant == 'esx_death'
                if elapsed(GetGameTimer(), started) >= waitBudget then
                    error('O prazo de reviver expirou ao consultar a integração médica; o hospital não foi acionado.', 0)
                end
                if provider == 'esx' and variant == 'legacy' then
                    -- Probe before performing any medical action. Recent ESX
                    -- legacy variants removed this callback and cannot safely
                    -- use the older client event as their complete lifecycle.
                    while not observedState() do
                        requestCurrent()
                        local now = GetGameTimer()
                        getter(now)
                        requestCurrent()
                        if elapsed(GetGameTimer(), started) >= waitBudget then
                            error('ESX sem callback médico legado; reviver não foi disparado. Requer integração do hospital instalado.', 0)
                        end
                        if observedState() then break end
                        Wait(100)
                    end
                    resetObservation()
                end
                releaseEffects()
                if variant ~= 'esx_death' then
                    -- Observe independently so a framework's yielding event
                    -- handler cannot suspend this resource's bounded deadline.
                    CreateThread(function()
                        local dispatched, detail = pcall(function()
                            requestCurrent()
                            if elapsed(GetGameTimer(), started) >= waitBudget then
                                error('O prazo de reviver expirou antes de acionar o hospital.', 0)
                            end
                            attempted = true
                            TriggerEvent(provider == 'esx' and 'esx_ambulancejob:revive' or 'hospital:client:Revive')
                        end)
                        if not dispatched then dispatchError = tostring(detail) end
                    end)
                end
                -- Even a natively alive ped may remain medically dead or in
                -- last stand: always dispatch the framework lifecycle first.
                while true do
                    requestCurrent()
                    if dispatchError then error(dispatchError, 0) end
                    local now, ped = GetGameTimer(), PlayerPedId()
                    local nativeAlive, medicalAlive = isAlive(ped), getter(now)
                    requestCurrent()
                    now, ped = GetGameTimer(), PlayerPedId()
                    nativeAlive = isAlive(ped)
                    if elapsed(now, started) >= waitBudget then
                        error(nativeAlive and 'Vida nativa confirmada, mas o estado médico não foi confirmado no prazo; verifique a API do hospital.'
                            or 'O hospital não confirmou vida nativa e estado médico no prazo; nenhum fallback nativo foi usado.', 0)
                    end
                    if attempted and nativeAlive and medicalAlive then
                        requestCurrent()
                        updatePedEffects(ped)
                        return provider == 'qbcore'
                            and 'Vida nativa e estados médicos QBCore confirmados; aguardando validação do servidor.'
                            or 'Vida nativa e estado médico ESX confirmados; aguardando validação do servidor.'
                    end
                    Wait(100)
                end
            end)
            finished = true
            acknowledge(ok, message, ok and 'medical' or 'unconfirmed')
        end)
        return
    end
    local ok, message = pcall(function()
        requestCurrent()
        if variant ~= 'native' then
            error('Integração standalone inválida.', 0)
        end
        local ped = PlayerPedId()
        if not validPed(ped) then error('Player ped is unavailable.') end
        if not IsPedDeadOrDying(ped, true) then return 'Player is already alive.' end
        releaseEffects()
        local position, heading = GetEntityCoords(ped), GetEntityHeading(ped)
        NetworkResurrectLocalPlayer(position.x, position.y, position.z, heading, 0, false)
        local resurrected = PlayerPedId()
        if not validPed(resurrected) then error('Resurrected player ped is unavailable.') end
        SetEntityHealth(resurrected, GetEntityMaxHealth(resurrected))
        ClearPedBloodDamage(resurrected)
        updatePedEffects(resurrected)
        if IsPedDeadOrDying(resurrected, true) then error('Native resurrection did not clear the death state.') end
        return 'Native revive applied; RP-framework death state is not changed.'
    end)
    acknowledge(ok, message, ok and 'native' or 'unconfirmed')
end)

RegisterNetEvent('zyon_admin_menu:suicide', function(ticket, expectedRevision)
    if source ~= 65535 or not T.finite(ticket) or ticket % 1 ~= 0 or ticket <= lastSuicideTicket then return end
    lastSuicideTicket = ticket
    CreateThread(function()
        local ok, message = pcall(function()
            if not T.finite(expectedRevision) or expectedRevision ~= revision or failedRevision == revision
                or not lastLease or elapsed(GetGameTimer(), lastLease) > LEASE_MS then
                error('Suicide command no longer matches the active control revision.')
            end
            local ped = PlayerPedId()
            if not validPed(ped) then error('Player ped is unavailable.') end
            if IsPedDeadOrDying(ped, true) then return 'Player is already dead.' end
            settings.player.godMode, settings.player.noclip = false, false
            restoreFlight()
            restoreInvincibility()
            SetEntityHealth(ped, 0)
            local started = GetGameTimer()
            while not IsPedDeadOrDying(ped, true) do
                if not lastLease or expectedRevision ~= revision or failedRevision == revision
                    or PlayerPedId() ~= ped or not validPed(ped)
                    or elapsed(GetGameTimer(), started) >= 2000 then
                    error('Native suicide did not confirm the death state.')
                end
                Wait(0)
            end
            return 'Native death state confirmed; RP-framework integration is server-specific.'
        end)
        TriggerServerEvent('zyon_admin_menu:suicideAck', ticket, ok, tostring(message):sub(1, 120))
    end)
end)

AddEventHandler('onResourceStop', function(resource)
    if M.isDependency(resource) then medicalEpoch = medicalEpoch + 1 end
    if resource == GetCurrentResourceName() then stopEffects() end
end)

CreateThread(function()
    while true do
        local now = GetGameTimer()
        if lastLease then
            if elapsed(now, lastLease) > LEASE_MS then
                local expired = revision
                stopEffects()
                failedRevision = expired
                TriggerServerEvent('zyon_admin_menu:ack', expired, false, 'Client control lease expired; effects were disabled.')
            else
                local ok, err = pcall(function()
                    local ped = PlayerPedId()
                    updatePedEffects(ped, true)
                    updateWeapons(ped)
                    updateVoice(now)
                    if validPed(ped) then updateVisualsAndAim(ped, now) end
                end)
                if not ok then
                    local rejected = revision
                    stopEffects()
                    failedRevision = rejected
                    TriggerServerEvent('zyon_admin_menu:ack', rejected, false, tostring(err):sub(1, 120))
                end
            end
        end
        Wait(lastLease and 0 or 250)
    end
end)
