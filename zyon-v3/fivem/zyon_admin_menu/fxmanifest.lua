-- Lua / fxmanifest.lua / FiveM FXServer / authorized external administrative menu.
fx_version 'cerulean'
game 'gta5'
lua54 'yes'

author 'Zyon'
description 'External administrative control using authenticated FXServer HTTP and supported natives.'
version '3.1.1'

shared_script 'medical.lua'
server_script 'server.lua'
client_scripts { 'targeting.lua', 'client.lua', 'pairing_client.lua' }

ui_page 'nui/index.html'
files { 'nui/index.html', 'nui/pairing.js' }
