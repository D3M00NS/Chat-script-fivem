-- Lua / tests/admin_client.lua / Lua 5.4 / pure math and mocked FiveM client lifecycle tests.
local root = arg[1] or (arg[0]:match('^(.*)/tests/[^/]+$') or '.')
dofile(root .. '/fivem/zyon_admin_menu/targeting.lua')
local T = ZyonTargeting
local assertions = 0
local function check(condition, message)
    assertions = assertions + 1
    assert(condition, message)
end
local function near(actual, expected, message)
    check(math.abs(actual - expected) < 0.000001, message .. ': ' .. tostring(actual))
end

local pitch, yaw = T.angles({x = 0, y = 0, z = 0}, {x = 0, y = 1, z = 0})
near(pitch, 0, 'Horizontal target pitch')
near(yaw, 0, 'World +Y yaw')
pitch, yaw = T.angles({x = 0, y = 0, z = 0}, {x = 1, y = 0, z = 1})
near(pitch, 45, 'Elevated target pitch')
near(yaw, -90, 'World +X yaw')
check(T.angles({x = 1, y = 2, z = 3}, {x = 1, y = 2, z = 3}) == nil, 'Coincident target has no angle')
near(T.wrapDegrees(181), -179, 'Yaw wrap through 180')
local ps, ys = T.cameraStep(0, 179, 10, -179, 1, 1 / 60)
near(ps, 10, 'Immediate pitch step')
near(ys, 2, 'Shortest yaw across discontinuity')
local at30, at60 = T.smoothingFactor(5, 1 / 30), T.smoothingFactor(5, 1 / 60)
near(1 - at30, (1 - at60) ^ 2, 'Smoothing frame-rate invariance')
near(T.screenDistance(0.5, 0.6, 1920, 1080), 108, 'Vertical FOV in screen pixels')
near(T.screenDistance(0.6, 0.5, 1920, 1080), 192, 'Horizontal FOV in screen pixels')
check(not T.finite(0 / 0) and not T.finite(math.huge), 'Reject non-finite settings')
local x, y, z = T.flightDirection(0, 0, 1, 1, 0)
near(math.sqrt(x * x + y * y + z * z), 1, 'Normalised diagonal flight')
near(x, math.sqrt(0.5), 'Right is world +X')
near(y, math.sqrt(0.5), 'Forward is world +Y')

local options = { onlyPlayers = true, onlyVisible = true, fov = 200 }
local targets = {
    {ped = 10, exists = true, dead = false, isPlayer = true, visible = true, projected = true, screenX = 0.5, screenY = 0.5},
    {ped = 20, exists = true, dead = false, isPlayer = false, visible = true, projected = true, screenX = 0.501, screenY = 0.5},
    {ped = 30, exists = true, dead = false, isPlayer = true, visible = false, projected = true, screenX = 0.51, screenY = 0.5},
    {ped = 40, exists = true, dead = false, isPlayer = true, visible = true, projected = true, screenX = 0.55, screenY = 0.5},
    {ped = 50, exists = true, dead = true, isPlayer = true, visible = true, projected = true, screenX = 0.501, screenY = 0.5}
}
check(T.select(targets, options, 10, 1920, 1080).ped == 40, 'Filter self, NPCs, occluded and dead peds')
options.onlyPlayers = false
check(T.select(targets, options, 10, 1920, 1080).ped == 20, 'NPC selection is real when enabled')
options.onlyPlayers, options.onlyVisible = true, false
check(T.select(targets, options, 10, 1920, 1080).ped == 30, 'Visibility filter is optional')
options.fov = 10
check(T.select(targets, options, 10, 1920, 1080) == nil, 'FOV excludes distant screen targets')
targets[3].projected, targets[3].screenX, targets[3].screenY = false, nil, nil
check(T.select(targets, options, 10, 1920, 1080) == nil, 'Invalid projections do not crash selection')

-- The following mocks verify API usage and state transitions, not game-engine
-- behavior. A live FiveM server/client is required for actual gameplay validation.
local events, handlers, sent, threads = {}, {}, {}, {}
local now, currentPed = 1000, 10
local standardInvincible, ragdollInvincible = false, false
local pedStates = {
    [10] = { alive = true, canBeDamaged = true, frozen = false, collisionDisabled = false, control = true, weapon = 100, total = 120, clip = 30,
        position = {x = 1, y = 2, z = 3}, heading = 90 },
    [11] = { alive = true, canBeDamaged = true, frozen = true, collisionDisabled = true, control = true, weapon = 100, total = 50, clip = 10,
        position = {x = 4, y = 5, z = 6}, heading = 0 }
}
local weapons = { [100] = { recoil = 0.75, spread = 1.25 } }
local reviveCalls, healthCalls, ammoWrites = 0, 0, 0
local controls, pressed, frameTime = {}, {}, 1 / 60
local freezeWrites, collisionWrites, volumeOverrides, volumeWrites = 0, 0, {}, {}
local function entity(ped) return assert(pedStates[ped], 'Missing mock entity ' .. tostring(ped)) end
function RegisterNetEvent(name, callback) events[name] = callback end
function AddEventHandler(name, callback) handlers[name] = callback end
function TriggerServerEvent(name, ...) sent[#sent + 1] = { name = name, arguments = {...} } end
function CreateThread(callback) threads[#threads + 1] = coroutine.create(callback) end
function Wait(ms) coroutine.yield(ms) end
function GetGameTimer() return now end
function PlayerPedId() return currentPed end
function PlayerId() return 0 end
function DoesEntityExist(ped) return pedStates[ped] ~= nil end
function IsEntityAPed(ped) return pedStates[ped] ~= nil end
function IsPedDeadOrDying(ped) return not entity(ped).alive end
function GetEntityCanBeDamaged(ped) return entity(ped).canBeDamaged end
function SetEntityCanBeDamaged(ped, value) entity(ped).canBeDamaged = value end
function IsEntityVisible(ped) return entity(ped).visible ~= false end
function SetEntityVisible(ped, value) entity(ped).visible = value end
function GetActivePlayers() return {0, 2} end
function GetPlayerServerId(player) return player == 0 and 11 or 22 end
function MumbleSetVolumeOverride(player, volume)
    volumeOverrides[player] = volume
    volumeWrites[#volumeWrites + 1] = { player = player, volume = volume }
end
function GetPlayerInvincible() return standardInvincible end
function GetPlayerInvincible_2() return standardInvincible or ragdollInvincible end
function SetPlayerInvincible(_, value) standardInvincible = value end
function SetPlayerInvincibleKeepRagdollEnabled(_, value) ragdollInvincible = value end
function IsPedInAnyVehicle(ped) return entity(ped).vehicle or false end
function IsEntityAttached(ped) return entity(ped).attached or false end
function NetworkHasControlOfEntity(ped) return entity(ped).control end
function IsEntityPositionFrozen(ped) return entity(ped).frozen end
function GetEntityCollisionDisabled(ped) return entity(ped).collisionDisabled end
function FreezeEntityPosition(ped, value) entity(ped).frozen = value; freezeWrites = freezeWrites + 1 end
function SetEntityCollision(ped, value) entity(ped).collisionDisabled = not value; collisionWrites = collisionWrites + 1 end
function SetEntityVelocity(ped, x, y, z) entity(ped).velocity = {x = x, y = y, z = z} end
function DisableControlAction() end
function GetDisabledControlNormal(_, control) return controls[control] or 0 end
function IsDisabledControlPressed(_, control) return pressed[control] or false end
function GetGameplayCamRot() return {x = 0, y = 0, z = 0} end
function GetFrameTime() return frameTime end
function GetEntityCoords(ped) return entity(ped).position end
function GetEntityHeading(ped) return entity(ped).heading end
function SetEntityCoordsNoOffset(ped, x, y, z) entity(ped).position = {x = x, y = y, z = z} end
function GetSelectedPedWeapon(ped) return entity(ped).weapon end
function GetHashKey() return 999 end
function HasPedGotWeapon(ped, hash) return entity(ped).weapon == hash end
function GetWeaponRecoilShakeAmplitude(hash) return weapons[hash].recoil end
function SetWeaponRecoilShakeAmplitude(hash, value) weapons[hash].recoil = value end
function GetWeaponAccuracySpread(hash) return weapons[hash].spread end
function SetWeaponAccuracySpread(hash, value) weapons[hash].spread = value end
function GetAmmoInPedWeapon(ped) return entity(ped).total end
function GetAmmoInClip(ped) return true, entity(ped).clip end
function SetPedAmmo(ped, _, value) entity(ped).total = value ammoWrites = ammoWrites + 1 end
function SetAmmoInClip(ped, _, value) entity(ped).clip = value ammoWrites = ammoWrites + 1 return true end
function IsPauseMenuActive() return false end
function IsGameplayCamRendering() return true end
function GetActualScreenResolution() return 1920, 1080 end
function NetworkResurrectLocalPlayer(x, y, z, heading, immunity, leaveCorpse)
    check(immunity == 0 and leaveCorpse == false, 'Revive has no added immunity or corpse duplicate')
    reviveCalls = reviveCalls + 1
    entity(currentPed).alive = true
    entity(currentPed).position = {x = x, y = y, z = z}
    entity(currentPed).heading = heading
end
function GetEntityMaxHealth() return 200 end
function SetEntityHealth(ped, health)
    entity(ped).health = health
    entity(ped).alive = health > 0
    healthCalls = healthCalls + 1
end
function ClearPedBloodDamage() end
function GetCurrentResourceName() return 'zyon_admin_menu' end
function GetResourceState() return 'missing' end
function GetResourceMetadata() return nil end

local function settings()
    local function aim()
        return { enabled = false, onlyVisible = true, onlyPlayers = true, targetBone = 'Head',
            activationKey = 'RMB', smooth = 1, drawFov = false, fov = 100 }
    end
    return { aim = aim(), silent = aim(), weapons = { noRecoil = false, noSpread = false, freezeAmmo = false },
        player = { godMode = false, noclip = false, noclipSpeed = 5, invisible = false, muteVoice = false },
        esp = {enabled = false, boxes = true, skeletons = false, names = true, distance = true,
            onlyPlayers = true, onlyVisible = true, maxDistance = 250, admins = false} }
end
local function tick()
    local ok, message = coroutine.resume(threads[1])
    check(ok, 'Client frame coroutine: ' .. tostring(message))
end
local function apply(value, revision, eventSource)
    source = eventSource or 65535
    events['zyon_admin_menu:apply'](value, revision)
end
local function lastAck()
    for index = #sent, 1, -1 do
        if sent[index].name ~= 'zyon_admin_menu:clientCapabilities' then return sent[index] end
    end
end
local function runAction()
    local thread = threads[#threads]
    local ok, message = coroutine.resume(thread)
    check(ok, 'Action coroutine: ' .. tostring(message))
    check(coroutine.status(thread) == 'dead', 'Immediate observed action completes its coroutine')
end

dofile(root .. '/fivem/zyon_admin_menu/medical.lua')
dofile(root .. '/fivem/zyon_admin_menu/client.lua')
local all = settings()
all.player.godMode, all.player.noclip = true, true
all.player.invisible, all.player.muteVoice = true, true
all.weapons.noRecoil, all.weapons.noSpread, all.weapons.freezeAmmo = true, true, true
apply(all, 1, 0)
check(not standardInvincible and #sent == 0, 'Local events cannot enable admin effects')
apply(all, 1)
check(not entity(10).canBeDamaged, 'Native damage immunity enabled')
check(entity(10).frozen and entity(10).collisionDisabled, 'On-foot noclip native state enabled')
check(not IsEntityVisible(10), 'Native visibility disabled only on the local ped')
check(volumeOverrides[2] == 0.0 and volumeOverrides[0] == nil, 'Local voice mute silences other players without overriding self')
near(weapons[100].recoil, 0, 'Native recoil shake disabled')
near(weapons[100].spread, 0, 'Native accuracy spread disabled')
check(lastAck().arguments[1] == 1 and lastAck().arguments[2] == true, 'Applied revision acknowledged')
entity(10).total, entity(10).clip = 119, 29
tick()
check(entity(10).total == 120 and entity(10).clip == 30, 'Observed ammo consumption replenished')
entity(10).total, entity(10).clip = 140, 30
tick()
entity(10).total, entity(10).clip = 139, 29
tick()
check(entity(10).total == 140 and entity(10).clip == 30, 'Legitimate observed ammo gains retained')

currentPed = 11
tick()
check(not entity(10).frozen and not entity(10).collisionDisabled, 'Old ped freeze/collision restored after ped change')
check(IsEntityVisible(10) and not IsEntityVisible(11), 'Ped replacement restores old visibility and applies new visibility')
check(entity(11).frozen and entity(11).collisionDisabled, 'New ped baselines preserved during noclip')
apply(settings(), 2)
check(entity(11).canBeDamaged and entity(10).canBeDamaged, 'Godmode original damageability restored on both peds')
check(entity(11).frozen and entity(11).collisionDisabled, 'Existing frozen/collision state survives disable')
check(IsEntityVisible(11) and volumeOverrides[2] == -1.0, 'Disable restores native visibility and releases the voice override')
near(weapons[100].recoil, 0.75, 'Recoil baseline restored')
near(weapons[100].spread, 1.25, 'Spread baseline restored')
check(entity(10).total == 140 and entity(10).clip == 30, 'Disabling does not overwrite ammo pickups')

ragdollInvincible = true
local god = settings()
god.player.godMode = true
apply(god, 3)
apply(settings(), 4)
check(not standardInvincible and ragdollInvincible and entity(11).canBeDamaged, 'Existing invincible keep-ragdoll flags were not touched')
ragdollInvincible = false

local recoilOnly = settings()
recoilOnly.weapons.noRecoil = true
apply(recoilOnly, 5)
weapons[100].recoil = 0.2
apply(settings(), 6)
near(weapons[100].recoil, 0.2, 'A newer weapon change by another resource is not overwritten')
weapons[100].recoil = 0.75

local silent = settings()
silent.silent.enabled = true
apply(silent, 7)
check(lastAck().arguments[2] == false, 'Silent aim request rejected honestly')
local malformed = settings()
malformed.player.inject = true
apply(malformed, 8)
check(lastAck().arguments[2] == false, 'Unknown settings fail closed')
local spreadGetter = GetWeaponAccuracySpread
GetWeaponAccuracySpread = nil
local spreadOnly = settings()
spreadOnly.weapons.noSpread = true
apply(spreadOnly, 9)
check(lastAck().arguments[2] == false, 'Unavailable native getter rejects requested feature')
GetWeaponAccuracySpread = spreadGetter

apply(god, 10)
check(not entity(11).canBeDamaged, 'Godmode enabled for lease test')
now = 6000
apply(god, 10)
now = 20000
tick()
check(not entity(11).canBeDamaged, 'Same-revision heartbeat refreshes client lease')
now = 21001
tick()
check(entity(11).canBeDamaged and lastAck().arguments[2] == false, 'Lease timeout revokes effects locally')
apply(god, 10)
check(entity(11).canBeDamaged, 'Expired revision cannot be re-enabled by an old heartbeat')
apply(god, 11)
check(not entity(11).canBeDamaged, 'New authorised revision reestablishes lease')
apply(settings(), 9)
check(not entity(11).canBeDamaged, 'Stale revision ignored')

entity(11).alive = false
source = 0
events['zyon_admin_menu:revive'](1, 11)
check(reviveCalls == 0, 'Local revive event rejected')
source = 65535
events['zyon_admin_menu:revive'](1, 11)
check(reviveCalls == 1 and healthCalls == 1 and entity(11).health == 200, 'Native revive heals to actual maximum')
check(lastAck().name == 'zyon_admin_menu:reviveAck' and lastAck().arguments[2] == true, 'Native revive acknowledged')
events['zyon_admin_menu:revive'](1, 11)
check(reviveCalls == 1, 'Revive replay ticket ignored')
events['zyon_admin_menu:revive'](2, 11)
check(reviveCalls == 1 and lastAck().arguments[3] == 'Player is already alive.', 'Alive player is not resurrected twice')
handlers.onResourceStop('different_resource')
check(not entity(11).canBeDamaged, 'Other resource stop does not alter effects')
handlers.onResourceStop('zyon_admin_menu')
check(entity(11).canBeDamaged and entity(11).frozen and entity(11).collisionDisabled, 'Own resource stop restores state')
check(ammoWrites >= 4, 'Ammo path exercised actual mock setter calls')

entity(11).frozen, entity(11).collisionDisabled, entity(11).control = false, false, false
local noclip = settings()
noclip.player.noclip = true
apply(noclip, 12)
check(not entity(11).frozen and not entity(11).collisionDisabled, 'Noclip does not mutate an entity without control')
entity(11).control = true
tick()
check(entity(11).frozen and entity(11).collisionDisabled, 'Noclip starts after acquiring legitimate local control')
controls[31] = -1
local flightStart = entity(11).position
tick()
near(entity(11).position.y - flightStart.y, 5 / 60, 'Noclip uses the configured base speed per frame')
local oldFreezeWrites, oldCollisionWrites = freezeWrites, collisionWrites
noclip.player.noclipSpeed = 20
flightStart = entity(11).position
apply(noclip, 13)
near(entity(11).position.x, flightStart.x, 'Applying a speed edit never adds a movement step in X')
near(entity(11).position.y, flightStart.y, 'Applying a speed edit never adds a movement step in Y')
near(entity(11).position.z, flightStart.z, 'Applying a speed edit never adds a movement step in Z')
flightStart = entity(11).position
tick()
near(entity(11).position.y - flightStart.y, 20 / 60, 'Changing the speed applies on the next frame')
check(freezeWrites == oldFreezeWrites and collisionWrites == oldCollisionWrites,
    'A speed edit does not recreate noclip collision or freeze state')
pressed[21] = true
flightStart = entity(11).position
tick()
near(entity(11).position.y - flightStart.y, 60 / 60, 'Shift multiplies the current configured speed by three')
pressed[21] = false
controls[30] = 1
flightStart = entity(11).position
tick()
local moved = entity(11).position
near(math.sqrt((moved.x - flightStart.x)^2 + (moved.y - flightStart.y)^2), 20 / 60,
    'Diagonal flight preserves the configured total speed')
controls[30] = 0
entity(11).position = { x = 100, y = 200, z = 300 }
tick()
near(entity(11).position.y, 200 + 20 / 60, 'Noclip follows current ped coordinates instead of a stale position')
near(entity(11).position.x, 100, 'Live flight coordinates preserve externally changed X')
frameTime = 1.0
flightStart = entity(11).position
tick()
near(entity(11).position.y - flightStart.y, 2.0, 'Frame-time clamp prevents jumps after a stalled frame')
controls, pressed, frameTime = {}, {}, 1 / 60
entity(11).position = { x = 4, y = 5, z = 6 }
entity(11).vehicle = true
tick()
check(not entity(11).frozen and not entity(11).collisionDisabled, 'Entering a vehicle restores on-foot noclip state')
entity(11).vehicle, entity(11).attached = false, true
tick()
check(not entity(11).frozen, 'Attached peds cannot be moved by noclip')
entity(11).attached = false
apply(settings(), 23)
entity(11).total, entity(11).clip = 0, 0
local emptyAmmo = settings()
emptyAmmo.weapons.freezeAmmo = true
local beforeAmmoWrites = ammoWrites
apply(emptyAmmo, 24)
tick()
check(ammoWrites == beforeAmmoWrites and entity(11).total == 0, 'Freeze ammo does not manufacture ammunition from an empty weapon')
apply(settings(), 25)

-- Rendering and camera setters are mocked too: these tests confirm native
-- arguments and selection, while not asserting engine camera/overlay results.
local headingResult, pitchResult, renderedLines, renderedLabels = nil, nil, 0, {}
local enumerations, projectedX = 0, {}
pedStates[20] = {alive = true, canBeDamaged = true, frozen = false, collisionDisabled = false, control = false,
    position = {x = 20, y = 50, z = 1}, heading = 0, visible = true, isPlayer = true}
pedStates[30] = {alive = true, canBeDamaged = true, frozen = false, collisionDisabled = false, control = false,
    position = {x = 1, y = 50, z = 1}, heading = 0, visible = true, isPlayer = false}
function GetActivePlayers() enumerations = enumerations + 1 return {0, 2} end
function GetPlayerPed(player) return player == 0 and currentPed or 20 end
function IsPedAPlayer(ped) return ped == currentPed or entity(ped).isPlayer or false end
function GetGamePool(pool) check(pool == 'CPed', 'NPC enumeration uses the documented CPed pool') return {10, 11, 20, 30} end
function GetPedBoneIndex(_, bone) return bone end
function GetPedBoneCoords(ped, _, _, _, zOffset)
    local p = entity(ped).position
    return {x = p.x, y = p.y, z = p.z + zOffset}
end
function GetScreenCoordFromWorldCoord(x, _, z)
    projectedX[#projectedX + 1] = x
    return true, 0.5 + x * 0.001, 0.5 - z * 0.001
end
function HasEntityClearLosToEntity(_, ped, flags)
    check(flags == 17, 'Visibility uses documented native collider mask')
    return entity(ped).visible ~= false
end
function GetWorldCoordFromScreenCoord(x, y) return {x = x, y = y, z = 0}, {x = 0, y = 0, z = 1} end
function DrawLine(...) renderedLines = renderedLines + 1 end
function SetTextFont() end
function SetTextScale() end
function SetTextColour() end
function SetTextCentre() end
function SetTextOutline() end
function BeginTextCommandDisplayText() end
function AddTextComponentSubstringPlayerName(text) renderedLabels[#renderedLabels + 1] = text end
function EndTextCommandDisplayText() end
function GetPlayerServerId() return 22 end
function GetPlayerName() return 'Other~r~Player' end
function IsControlPressed(_, control) return control == 25 end
function GetGameplayCamCoord() return {x = 0, y = 0, z = 0} end
function GetGameplayCamRelativeHeading() return 0 end
function GetGameplayCamRelativePitch() return 0 end
function SetGameplayCamRelativeHeading(value) headingResult = value end
function SetGameplayCamRelativePitch(value, scale)
    check(scale == 1.0, 'Camera pitch uses the documented scaling factor')
    pitchResult = value
end
local visual = settings()
visual.aim.enabled, visual.aim.drawFov = true, true
visual.esp.enabled, visual.esp.skeletons = true, true
apply(visual, 26)
now = now + 500
tick()
near(headingResult, math.deg(math.atan(-20, 50)), 'Camera targeting selects a streamed player')
check(pitchResult > 0 and renderedLines == 96 + 4 + 15, 'FOV, ESP box, skeleton and camera native paths exercised')
check(#renderedLabels == 1 and not renderedLabels[1]:find('~', 1, true), 'Player-name formatting directives removed from labels')
visual.aim.onlyPlayers = false
apply(visual, 27)
now = now + 500
tick()
near(headingResult, math.deg(math.atan(-1, 50)), 'Non-player target comes from the actual streamed NPC pool')
entity(20).visible = false
visual.aim.onlyPlayers = true
apply(visual, 28)
now = now + 500
headingResult = nil
tick()
check(headingResult == nil, 'Aim native setter not called for blocked player targets')

-- Participant enumeration is throttled, but coordinates and projections must
-- follow the streamed character on every frame.
entity(20).visible = true
local liveEsp = settings()
liveEsp.esp.enabled = true
apply(liveEsp, 29)
now = now + 500
tick()
local beforeEnumerations = enumerations
entity(20).position = { x = 70, y = 80, z = 4 }
projectedX, renderedLabels = {}, {}
now = now + 16
tick()
check(enumerations == beforeEnumerations, 'ESP reuses participant IDs between enumeration intervals')
local projectsLivePosition = false
for _, value in ipairs(projectedX) do if value == 70 then projectsLivePosition = true end end
check(projectsLivePosition, 'ESP projects the current character position on the next frame')
check(#renderedLabels == 1 and renderedLabels[1]:find('m', 1, true), 'ESP distance remains available while positions update live')

liveEsp.esp.admins = true
liveEsp.player.noclipSpeed = 37
local beforeSpeedEnumeration = enumerations
apply(liveEsp, 30)
source = 0
events['zyon_admin_menu:admins']({22})
renderedLabels = {}
tick()
check(enumerations == beforeSpeedEnumeration, 'Speed and administrator-display edits preserve the ESP enumeration interval')
check(not renderedLabels[1]:find('ADM', 1, true), 'Local events cannot invent administrator markers')
source = 65535
events['zyon_admin_menu:admins']({22})
renderedLabels = {}
tick()
check(renderedLabels[1]:find('ADM', 1, true) ~= nil, 'Server-authorized administrator IDs mark the streamed player')
events['zyon_admin_menu:admins']({})
renderedLabels = {}
tick()
check(not renderedLabels[1]:find('ADM', 1, true), 'Server list cleanup removes stale administrator markers')
handlers.onResourceStop('zyon_admin_menu')

-- Visibility snapshots must include an already hidden ped, and unavailable
-- optional natives must reject their feature rather than falsely acknowledge it.
entity(11).visible = false
local hidden = settings()
hidden.player.invisible = true
apply(hidden, 31)
apply(settings(), 32)
check(not IsEntityVisible(11), 'Disabling invisibility preserves an originally hidden character')
entity(11).visible = true
local visibilityGetter = IsEntityVisible
IsEntityVisible = nil
apply(hidden, 33)
check(lastAck().arguments[2] == false and entity(11).visible, 'Missing visibility getter rejects invisibility without mutating the ped')
IsEntityVisible = visibilityGetter

local mumbleNative = MumbleSetVolumeOverride
MumbleSetVolumeOverride = nil
local muted = settings(); muted.player.muteVoice = true
apply(muted, 34)
check(lastAck().arguments[2] == false, 'Missing Mumble native explicitly rejects voice mute')
apply(settings(), 35)
check(lastAck().arguments[2] == true, 'Missing optional Mumble native does not block other settings')
local reportedUnavailable = false
for _, event in ipairs(sent) do
    if event.name == 'zyon_admin_menu:clientCapabilities' and event.arguments[1].muteVoice == false then
        reportedUnavailable = true
    end
end
check(reportedUnavailable, 'Client capability report reflects the unavailable Mumble native')
MumbleSetVolumeOverride = mumbleNative

local temporary = settings()
temporary.player.invisible, temporary.player.muteVoice = true, true
apply(temporary, 36)
check(not IsEntityVisible(11) and volumeOverrides[2] == 0.0, 'Visibility and voice effects enable together')
now = now + 15001
tick()
check(IsEntityVisible(11) and volumeOverrides[2] == -1.0, 'Lease expiry restores invisibility and local voice overrides')
apply(temporary, 36)
check(IsEntityVisible(11) and volumeOverrides[2] == -1.0, 'Expired revision cannot revive visibility or voice effects')
apply(temporary, 37)
handlers.onResourceStop('zyon_admin_menu')
check(IsEntityVisible(11) and volumeOverrides[2] == -1.0, 'Resource shutdown restores visibility and voice overrides')

-- Action tickets come from the authorized server and cannot be replayed or
-- supplied by another local resource.
apply(settings(), 38)
local beforeSuicideHealth = healthCalls
source = 0
events['zyon_admin_menu:suicide'](1, 38)
check(entity(11).alive and healthCalls == beforeSuicideHealth, 'Local suicide event cannot kill the character')
source = 65535
for _, invalid in ipairs({ 0, -1, 1.5, math.huge, 0 / 0, '1', {} }) do
    events['zyon_admin_menu:suicide'](invalid, 38)
end
check(entity(11).alive and healthCalls == beforeSuicideHealth, 'Invalid suicide tickets do not change health')
events['zyon_admin_menu:suicide'](1, 38)
runAction()
check(not entity(11).alive and entity(11).health == 0, 'Server-authorized suicide changes only the current ped health')
check(lastAck().name == 'zyon_admin_menu:suicideAck' and lastAck().arguments[1] == 1
    and lastAck().arguments[2] == true, 'Suicide acknowledges its correlated ticket after death verification')
local afterSuicideHealth = healthCalls
events['zyon_admin_menu:suicide'](1, 38)
check(healthCalls == afterSuicideHealth, 'A repeated suicide ticket cannot act twice')
events['zyon_admin_menu:suicide'](2, 38)
runAction()
check(healthCalls == afterSuicideHealth and lastAck().arguments[1] == 2, 'An already dead character acknowledges without another health write')
events['zyon_admin_menu:revive'](3, 38)
check(entity(11).alive and entity(11).health == 200, 'Revive remains usable after a ticketed suicide')

local realHealthSetter = SetEntityHealth
SetEntityHealth = function() healthCalls = healthCalls + 1 end
events['zyon_admin_menu:suicide'](3, 38)
local pendingAction = threads[#threads]
local actionOk, actionWait = coroutine.resume(pendingAction)
check(actionOk and actionWait == 0 and coroutine.status(pendingAction) == 'suspended',
    'Suicide waits for the observed death state rather than claiming a health setter succeeded')
now = now + 2000
actionOk = coroutine.resume(pendingAction)
check(actionOk and coroutine.status(pendingAction) == 'dead', 'Unconfirmed suicide finishes after its bounded timeout')
check(lastAck().name == 'zyon_admin_menu:suicideAck' and lastAck().arguments[1] == 3
    and lastAck().arguments[2] == false and entity(11).alive, 'Unconfirmed death sends an explicit failed ticket acknowledgement')
SetEntityHealth = realHealthSetter

apply(settings(), 39)
entity(11).alive = false
local beforeStaleRevive = reviveCalls
events['zyon_admin_menu:revive'](4, 38)
check(not entity(11).alive and reviveCalls == beforeStaleRevive,
    'A delayed revive for an older revision cannot resurrect a newly reset session')
entity(11).alive = true
local beforeStaleHealth = healthCalls
events['zyon_admin_menu:suicide'](4, 38)
runAction()
check(healthCalls == beforeStaleHealth and lastAck().arguments[2] == false,
    'A delayed suicide for an older revision cannot change health')
events['zyon_admin_menu:suicide'](5, 39)
apply(settings(), 40)
runAction()
check(entity(11).alive and healthCalls == beforeStaleHealth and lastAck().arguments[2] == false,
    'A queued suicide revalidates its revision before writing health')

SetEntityHealth = function() healthCalls = healthCalls + 1 end
events['zyon_admin_menu:suicide'](6, 40)
pendingAction = threads[#threads]
actionOk, actionWait = coroutine.resume(pendingAction)
check(actionOk and actionWait == 0, 'A pending suicide waits while the native death state remains alive')
apply(settings(), 41)
actionOk = coroutine.resume(pendingAction)
check(actionOk and coroutine.status(pendingAction) == 'dead' and lastAck().arguments[2] == false,
    'Revision reset interrupts an action already waiting for death confirmation')
SetEntityHealth = realHealthSetter

entity(11).alive = false
now = now + 15001
tick()
beforeStaleRevive = reviveCalls
events['zyon_admin_menu:revive'](5, 41)
check(not entity(11).alive and reviveCalls == beforeStaleRevive,
    'A revive event cannot act after its client control lease expires')
entity(11).alive = true
beforeStaleHealth = healthCalls
events['zyon_admin_menu:suicide'](7, 41)
runAction()
check(healthCalls == beforeStaleHealth and lastAck().arguments[2] == false,
    'A suicide event cannot act after its client control lease expires')
handlers.onResourceStop('zyon_admin_menu')
print(string.format('PASS: %d client targeting/lifecycle assertions (mocked natives; gameplay not verified).', assertions))
