-- Lua / fivem/zyon_admin_menu/targeting.lua / FiveM Lua 5.4 / GTA V client targeting math.
ZyonTargeting = {}

local T = ZyonTargeting

function T.finite(value)
    return type(value) == 'number' and value == value and value ~= math.huge and value ~= -math.huge
end

function T.clamp(value, minimum, maximum)
    return math.max(minimum, math.min(maximum, value))
end

function T.wrapDegrees(angle)
    return (angle + 180.0) % 360.0 - 180.0
end

function T.distance(a, b)
    local x, y, z = b.x - a.x, b.y - a.y, b.z - a.z
    return math.sqrt(x * x + y * y + z * z)
end

-- GTA camera yaw zero faces world +Y; positive yaw turns toward -X.
function T.angles(origin, target)
    local dx, dy, dz = target.x - origin.x, target.y - origin.y, target.z - origin.z
    local horizontal = math.sqrt(dx * dx + dy * dy)
    if horizontal < 0.000001 and math.abs(dz) < 0.000001 then
        return nil
    end
    return math.deg(math.atan(dz, horizontal)), math.deg(math.atan(-dx, dy))
end

function T.screenDistance(screenX, screenY, width, height)
    local dx = (screenX - 0.5) * width
    local dy = (screenY - 0.5) * height
    return math.sqrt(dx * dx + dy * dy)
end

-- Keep the same response at different frame rates. Smooth=1 selects immediately.
function T.smoothingFactor(smooth, frameSeconds)
    if smooth <= 1.0 then return 1.0 end
    local dt = T.clamp(frameSeconds, 0.001, 0.1)
    return 1.0 - (1.0 - 1.0 / smooth) ^ (dt * 60.0)
end

function T.cameraStep(currentPitch, currentYaw, targetPitch, targetYaw, smooth, frameSeconds)
    local alpha = T.smoothingFactor(smooth, frameSeconds)
    return (targetPitch - currentPitch) * alpha, T.wrapDegrees(targetYaw - currentYaw) * alpha
end

function T.eligible(candidate, options, localPed)
    if candidate.ped == localPed or candidate.dead or not candidate.exists then return false end
    if options.onlyPlayers and not candidate.isPlayer then return false end
    if options.onlyVisible and not candidate.visible then return false end
    return candidate.projected and candidate.screenX >= 0.0 and candidate.screenX <= 1.0
        and candidate.screenY >= 0.0 and candidate.screenY <= 1.0
end

function T.select(candidates, options, localPed, width, height)
    local best, bestPixels
    for _, candidate in ipairs(candidates) do
        if T.eligible(candidate, options, localPed) then
            local pixels = T.screenDistance(candidate.screenX, candidate.screenY, width, height)
            if pixels <= options.fov and (not bestPixels or pixels < bestPixels) then
                best, bestPixels = candidate, pixels
            end
        end
    end
    return best
end

-- Camera-oriented on-foot flight direction. Movement is normalised to avoid
-- diagonal speed gains. Inputs use +forward, +right, +vertical conventions.
function T.flightDirection(pitchDegrees, yawDegrees, forward, right, vertical)
    local pitch, yaw = math.rad(pitchDegrees), math.rad(yawDegrees)
    local cp = math.cos(pitch)
    local x = -math.sin(yaw) * cp * forward + math.cos(yaw) * right
    local y = math.cos(yaw) * cp * forward + math.sin(yaw) * right
    local z = math.sin(pitch) * forward + vertical
    local length = math.sqrt(x * x + y * y + z * z)
    if length > 1.0 then x, y, z = x / length, y / length, z / length end
    return x, y, z
end
