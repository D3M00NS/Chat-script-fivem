# Integração médica do reviver

Em servidores separados, deixe `set zyon_menu_medical_provider "auto"` no `server.cfg`. O menu mostra a integração selecionada e mantém as ações limitadas ao administrador pareado. A mesma distribuição pode ser instalada nas três bases.

| Perfil | Fluxo | Confirmação |
| --- | --- | --- |
| Standalone | Native `NetworkResurrectLocalPlayer` | Estado de vida do GTA; sem confirmação de hospital. |
| ESX atual | Export server `esx_death:Revive` | Getter do cliente, ped vivo e `GetDeathState` carregado no servidor; persistência pertence ao export oficial. |
| ESX legado compatível | Evento `esx_ambulancejob:revive` | Callback oficial `esx_ambulancejob:getDeathStatus` consultado antes e depois; falta de callback bloqueia a operação. |
| QBCore | Evento `hospital:client:Revive` | Ped vivo e `isdead=false`/`inlaststand=false` no cliente e no servidor; SQL segue o ciclo de save do QBCore. |

Pode-se fixar `standalone`, `esx` ou `qbcore`. Se as duas bases forem iniciadas juntas, `auto` não escolhe por prioridade: é necessário selecionar uma. Core carregado com hospital ausente, recurso médico interrompido, API ausente, timeout ou revisão substituída gera erro explícito; não há ressurreição nativa como fallback de um hospital.

ESX 1.14/1.15 retirou o callback antigo antes de adotar `esx_death`. Não há API pública equivalente suficientemente identificável para integrar essas versões sem adaptar o recurso médico. O menu consulta o callback antes de tentar a rota legada e recusa quando ele não existe. Não executa um comando global `revive`, pois o registro nativo não comprova o callback médico efetivamente instalado.

Os testes executados aqui simulam as APIs. Valide no seu servidor o fluxo completo de morte, revive e reconexão. Nomes de recursos renomeados, hospitais próprios e forks podem exigir adaptação específica.

# Contratos médicos oficiais verificados em 14/10/2026

Inspeção independente por clones Git HTTPS somente leitura. Nenhum binário do menu foi executado. As cópias abaixo estão fora do checkout do usuário.

## ESX atual, lifecycle `esx_death`

Repo `esx-framework/esx_addons`, branch `main`, commit `646ad82fdf96a374c3efdd09c9a6080db2605cfd`.

- [`esx_ambulancejob/fxmanifest.lua` L30-36](https://github.com/esx-framework/esx_addons/blob/646ad82fdf96a374c3efdd09c9a6080db2605cfd/%5Besx_addons%5D/esx_ambulancejob/fxmanifest.lua#L30) declara dependência em `esx_death`; ambulance `legacyversion` é `1.16.0`.
- [`esx_death/server/main.lua` L407-436](https://github.com/esx-framework/esx_addons/blob/646ad82fdf96a374c3efdd09c9a6080db2605cfd/%5Besx_addons%5D/esx_death/server/main.lua#L407): export `Revive(src, reason)` retorna `ok, wasDead` booleanos. Já vivo retorna `false, false`. Recurso ainda não pronto, jogador ausente ou lock por jogador retornam falha em `withPlayer` L137-185.
- A chamada server aguarda SQL em `save` L219-222: `users.is_dead=0`, `death_time=NULL`. Limpa metadado `deathTime` L238 e publica state bag `isDead=false` L80-93 antes de `TriggerClientEvent('esx_death:recover', src, recovery)` L429.
- Export server `GetDeathState(src)` L450-455 retorna `snapshot` com `ok=true, dead=boolean` L188-205 ou `nil` quando não há estado em cache. `IsDead(src)` L441-447 retorna false quando não há cache, portanto isoladamente NÃO prova que o jogador está carregado/vivo.
- Export client `GetDeathState()` L875-883 retorna tabela com `dead=boolean`. `IsDead()` L871-872 retorna variável local. Recuperação L451-521 desfaz UI, ressuscita e chama os eventos oficiais spawn; evento `esx_death:recover` L736-743 ignora chamadas locais (`source ~= 65535`). Adaptador deve chamar export no server, não `TriggerEvent` no client.
- Export server `Revive` registrado L492. Ele é API entre recursos, não autoriza usuários; menu precisa checar ACE, sessão/jogador e revisão antes de invocar.

## ESX intermediário 1.14/1.15

Tags `v1.14.0` = `a5dace2fb72bf541d3a2ee6ad96a3bd12bca7c59`, `v1.15.0` = `ccda737f7d4f73d224fab2097bee5083afd4dd4e` no mesmo repo.

- Ambos ainda registram evento client `esx_ambulancejob:revive`. No v1.15 `client/main.lua` L401-421 ele ressuscita via `RespawnPed` L373-386 e termina câmeras, mas NÃO altera SQL.
- [`v1.15 server/main.lua` L24-35](https://github.com/esx-framework/esx_addons/blob/ccda737f7d4f73d224fab2097bee5083afd4dd4e/%5Besx_addons%5D/esx_ambulancejob/server/main.lua#L24) contém `persistDeathStatus` privado: UPDATE SQL assíncrono, state bag e deathTime. Comando administrativo `revive` L475-481 usa isso e limpa `deadPlayers` antes de evento client. Evento server revive L133+ exige profissão EMS, não adequado para admin genérico.
- `getDeathStatus` e `setDeathStatus` NÃO existem nesses releases. Não usar callback antigo apenas porque `esx_ambulancejob` está started.
- `v1.14.0` mantém `legacyversion '1.13.4'`, portanto versão declarada não identifica confiavelmente presença do callback antigo.
- Sem export público para lifecycle/persistência ou callback verificado, informar integração necessária. Não disparar revive client seguido de native fallback e declarar sucesso.

## ESX anterior com callback SQL

Commit oficial `a94ede6f0965947d5fe2cb145fd894e32220a1ff` (antes da mudança de julho/2026), mesmo repo, disponível por `git show` no checkout esx_addons.

- [`server/main.lua` L357-361](https://github.com/esx-framework/esx_addons/blob/a94ede6f0965947d5fe2cb145fd894e32220a1ff/%5Besx_addons%5D/esx_ambulancejob/server/main.lua#L357): `ESX.RegisterServerCallback('esx_ambulancejob:getDeathStatus', cb)` consulta `users.is_dead` e retorna boolean/valor SQL.
- Server L364-370 registra `esx_ambulancejob:setDeathStatus` e atualiza SQL/state bag.
- [`client/main.lua` L370-374](https://github.com/esx-framework/esx_addons/blob/a94ede6f0965947d5fe2cb145fd894e32220a1ff/%5Besx_addons%5D/esx_ambulancejob/client/main.lua#L370): evento client revive chama `setDeathStatus(false)` antes da animação/ressurreição. Getter depois da operação pode confirmar persistência.
- Essa edição declara `legacyversion '1.13.4'` igual a tag v1.14.0 sem o callback. Configuração explícita de adaptador e callback confirmado antes de quaisquer efeitos é necessária para esta rota; ausência/timeout não deve ativar nativo.

## QBCore + qb-ambulancejob

Repo `qbcore-framework/qb-ambulancejob`, main `19bb190d7c41e6b79d836a45d7498bd3762d0bd4`; repo core `qbcore-framework/qb-core`, main `9b3cddcce93e5e12cbcf6b47b866b687d32ac7bf`.

- [`ambulance client/main.lua` L539-569](https://github.com/qbcore-framework/qb-ambulancejob/blob/19bb190d7c41e6b79d836a45d7498bd3762d0bd4/client/main.lua#L539): `hospital:client:Revive` ressuscita, altera flags locais, `SetLaststand(false)`, `ResetAll` ferimentos e emite server status false L565-566.
- [`ambulance server/main.lua` L129-143](https://github.com/qbcore-framework/qb-ambulancejob/blob/19bb190d7c41e6b79d836a45d7498bd3762d0bd4/server/main.lua#L129): eventos `hospital:server:SetDeathStatus` e `SetLaststandStatus` alteram metadata `isdead`, `inlaststand` no jogador.
- [`core server/player.lua` L675-677](https://github.com/qbcore-framework/qb-core/blob/9b3cddcce93e5e12cbcf6b47b866b687d32ac7bf/server/player.lua#L675) export `GetPlayer(source)` retorna interface com `PlayerData` e `Functions`, e `GetPlayerData` L50-51 retorna metadata. API tradicional `QBCore.Functions.GetPlayer` também existe.
- `SetMetaData` L150-156 atualiza metadata e client, NÃO executa SQL imediatamente. `Save` L488-505 salva SQL assíncrono; confirmação metadata server não deve ser descrita como confirmação SQL síncrona. O ciclo normal do framework preserva as flags no próximo save.
- Client core `QBCore.Functions.GetPlayerData()` em `client/functions.lua` L38-40 retorna `QBCore.PlayerData`.

## FiveM `GetRegisteredCommands` não comprova dono do callback

Repo `citizenfx/fivem`, main `f5c46b3f1a6941982bdbe303185b13cbccb74e5d`, checkout `fivem`.

- [`ext/native-decls/GetRegisteredCommands.md` L11-25](https://github.com/citizenfx/fivem/blob/f5c46b3f1a6941982bdbe303185b13cbccb74e5d/ext/native-decls/GetRegisteredCommands.md#L11) confirma `name`, `resource`, `arity`.
- [`ResourceScriptFunctions.cpp` L131-159](https://github.com/citizenfx/fivem/blob/f5c46b3f1a6941982bdbe303185b13cbccb74e5d/code/components/citizen-scripting-core/src/ResourceScriptFunctions.cpp#L131) registra resource do runtime atual. ESX `RegisterCommand` é função do objeto exportado por `es_extended`, não de ambulance.
- Mesmo native troca `outerRefs[commandName]` L141 antes de early return para comando já existente L143-145, então metadata resource pode permanecer original com handler substituído.
- ESX atual `server/modules/commands/register.lua` L49-60 troca tabela privada `Core.RegisteredCommands[name].cb` e retorna em override sem alterar a metadata nativa. Não há getter público desta tabela encontrado.
- `ExecuteCommand` também usa principal `resource.<caller>` em native L103-117, então exigiria ACE específica de comando além da ACE dos usuários. Não executar automaticamente com base em `resource` publicado: não é prova suficiente do handler médico.

## Restrições para revisão

- Standalone só quando configurado/realmente standalone; ESX/QBCore carregado com hospital ausente/customizado deve produzir erro explícito.
- Não tratar nil de getter, export ausente ou callback timeout como personagem vivo.
- Exigir confirmação ped vivo + flags client + flags server apropriadas e revisão/sessão ainda válida. SQL síncrono só é garantido pelo export moderno esx_death.
- A chamada de framework pode yield; checar sessão/revisão/ACE de novo após retornos e não enviar ack para sessão substituída.
- Não resolver ambas bases iniciadas por prioridade silenciosa; exigir seleção de modo.
