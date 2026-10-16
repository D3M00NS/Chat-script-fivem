# Módulo local 3.3.0

Biblioteca nativa x64 carregada da pasta oficial `FiveM.app/plugins`. O executável inclui uma cópia compilada das fontes deste repositório e a extrai em **Conexão → Preparar módulo local**. Feche o FiveM durante a instalação e abra-o novamente depois. Na atualização de 3.2.0, prepare o módulo outra vez: aplicativo e ASI devem usar a mesma versão do protocolo.

O modo **CLIENTE LOCAL** não verifica cargo de administrador nem a permissão ACE `zyon.modmenu`. O servidor deve já permitir plugins/ScriptHook e usar pure level abaixo de 2. O programa não instala recursos, não altera essa política e não concede permissões no servidor. Estado desconectado requer diagnóstico de carregamento e comunicação; não é uma resposta da verificação ACE da integração antiga.

## Execução e comunicação

O ASI contém o recurso de compatibilidade `FX_ASI_BUILD`, tipo numérico `3258`. O runtime CFX fornece `scriptRegister` e `scriptWait(0)`; as operações de jogo ocorrem no fiber registrado, no contexto de script do FiveM. A thread do pipe não executa natives.

O invoker resolve exports públicos de ScriptHookV e consulta a disponibilidade dos handlers pelo export público RAGE. As chamadas continuam no invoker oficial, sujeito a `ShAllowed` e `networkInited`. O handshake exige temporizador de jogo avançando, build 3258 e personagem válido. Uma chamada isolada ou um export presente não confirmam efeito no jogo.

O **protocolo 2** usa o pipe `ZyonClientBridge-v2-<GTA_PID>` e payload de configurações de **48 bytes**. O pipe tem DACL do usuário atual/SYSTEM, rejeita acesso remoto e permite ao desktop conferir o PID proprietário. Mensagens têm tamanho fixo, token por conexão, revisões e nonces; não transportam hashes arbitrários, ponteiros ou código. O pipe v1 da 3.2.0 não atende este cliente.

Configurações são copiadas para o quadro de jogo. A sessão permanece válida enquanto espera ACK ou condições de voo; a revisão aplicada indica confirmação separadamente. A lease expira em três segundos. Perda de execução nativa após uma sessão válida revoga a conexão e impede repetição automática de efeitos antigos.

## Funções e ações

GodMode, invisibilidade e colisão/congelamento mantêm snapshots dos estados pertencentes ao módulo para restauração. Mudança de entidade/modelo descarta o snapshot anterior; falha de restauração mantém sua pendência. Noclip/voar usa atualização por quadro e velocidade alterável em tempo real, apenas a pé e com controle da entidade. Freeze Ammo lê a arma, o total e o pente. O leitor externo e o módulo continuam separados por função.

Reviver e suicídio vinculam-se ao personagem confirmado na interface. Troca do personagem, cancelamento, timeout ou conexão substituída invalidam o pedido. Suicídio desliga GodMode e noclip do próprio menu. O sucesso exige observação nativa correspondente. O revive não limpa o estado médico de ESX/QBCore ou de hospitais personalizados.

**Bloquear carro / Desbloquear carro** captura o veículo ocupado; a pé, procura o mais próximo em até **8 metros**. O alvo e seu modelo permanecem vinculados ao pedido. O módulo solicita controle pela API oficial de rede durante até **dois segundos**, confirma o controle e escreve uma única vez o estado de portas **2** para bloquear ou **1** para desbloquear. O resultado exige readback do estado solicitado no mesmo veículo. Troca do alvo, afastamento ou perda de controle cancelam a ação; não há replay ao reconectar.

Essa ação só modifica o estado nativo das portas. Não concede chaves ESX/QBCore, não altera propriedade e não confirma que um script de garagem/chaves manterá o resultado.

## Silent Aim experimental

O protótipo utiliza `SET_PED_SHOOTS_AT_COORD`. Não move a câmera e não usa `SHOOT_SINGLE_BULLET_BETWEEN_COORDS` para criar um projétil manual. Isso não significa que redirecione o projétil original: a chamada pode não produzir o efeito esperado.

Consulta somente jogadores nos índices **0–255**, com personagem válido e vivo. A interface permite configurar **FOV em pixels**, osso, tecla de ativação e linha de visão nativa; somente jogadores é fixo. Não há suavização ou círculo FOV.

Antes de uma tentativa, exige janela do FiveM em foco, personagem local vivo e a pé, arma/munição/pente válidos e ausência de recarga. É necessário soltar o tiro após ativar para armar o primeiro disparo. Depois, cada novo pressionamento junto da tecla de ativação permite **uma tentativa**; manter pressionado não repete a chamada.

**Enquanto ativo, o protótipo suprime a entrada normal de tiro, mesmo sem alvo elegível no FOV.** Pode impedir disparos. Uma queda de munição observada confirma apenas consumo, não redirecionamento, hit ou dano. Não há promessa de Silent Aim funcional antes de validar no FiveM real. Se interferir nos disparos, desative a opção e registre o diagnóstico.

## Teste no Windows e limites

Depois de atualizar o módulo com o FiveM fechado, abra o jogo e confira primeiro **CLIENTE LOCAL**, conexão e capacidades anunciadas. Teste uma função por vez e salve o JSON em **Salvar diagnóstico**, informando a ação e o resultado visto no jogo. O diagnóstico separa o estado nativo do externo e inclui revisões, observações das ações e motivos de bloqueio.

Windows/FiveM real ainda não foi validado neste ambiente Linux. Voz recebida, nomes/IDs de cidade, identificação de ADMs e visibilidade do ESP externo continuam pendentes. Natives de saúde não confirmam recuperação médica de frameworks.

## Referências primárias

- [Catálogo oficial de natives](https://docs.fivem.net/natives/) e [fontes citizenfx/natives](https://github.com/citizenfx/natives).
- [`SET_PED_SHOOTS_AT_COORD`](https://github.com/citizenfx/natives/blob/master/PED/SetPedShootsAtCoord.md): chamada usada no protótipo; a documentação não estabelece redirecionamento garantido do tiro original.
- [`SET_VEHICLE_DOORS_LOCKED`](https://github.com/citizenfx/natives/blob/master/VEHICLE/SetVehicleDoorsLocked.md), [`GET_VEHICLE_DOOR_LOCK_STATUS`](https://github.com/citizenfx/natives/blob/master/VEHICLE/GetVehicleDoorLockStatus.md) e [`NETWORK_REQUEST_CONTROL_OF_ENTITY`](https://github.com/citizenfx/natives/blob/master/NETWORK/NetworkRequestControlOfEntity.md).
- [`NETWORK_RESURRECT_LOCAL_PLAYER`](https://github.com/citizenfx/natives/blob/master/NETWORK/NetworkResurrectLocalPlayer.md) e [`SET_ENTITY_HEALTH`](https://github.com/citizenfx/natives/blob/master/ENTITY/SetEntityHealth.md): ressuscitar/saúde nativos, sem estado médico de frameworks.

Implementação CFX revisada em `f5c46b3f1a6941982bdbe303185b13cbccb74e5d`:

- [Loader ASI](https://github.com/citizenfx/fivem/blob/f5c46b3f1a6941982bdbe303185b13cbccb74e5d/code/components/asi-five/src/Component.cpp): pasta plugins, pure level e recurso de build.
- [Compatibilidade ScriptHookV](https://github.com/citizenfx/fivem/blob/f5c46b3f1a6941982bdbe303185b13cbccb74e5d/code/components/scripthookv/src/VishCompat.cpp): scheduler, natives e permissões.
- [RAGE native lookup](https://github.com/citizenfx/fivem/blob/f5c46b3f1a6941982bdbe303185b13cbccb74e5d/code/components/rage-scripting-five/src/scrEngine.cpp): consulta e mapeamento dos handlers.
