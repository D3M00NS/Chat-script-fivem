# Módulo local 3.2.0

O módulo é uma biblioteca nativa x64 carregada da pasta oficial `FiveM.app/plugins`. O executável inclui uma cópia compilada a partir das fontes deste repositório e a extrai quando o usuário seleciona **Preparar módulo local**. FiveM deve estar fechado durante a instalação e ser aberto novamente depois.

O ASI contém o recurso de compatibilidade `FX_ASI_BUILD` com tipo numérico `3258`, conforme o loader CFX. Não é uma DLL .NET renomeada. O runtime CFX fornece `scriptRegister` e `scriptWait(0)`; as operações de jogo ocorrem somente no fiber registrado, com o contexto de script do FiveM. A thread do pipe não executa natives.

O invoker resolve os exports públicos de ScriptHookV e consulta a disponibilidade de handlers pelo export público RAGE. A execução continua passando pelo invoker oficial, que aplica `ShAllowed` e `networkInited`. O handshake exige temporizador de jogo avançando, build 3258 e personagem válido; resultado de export ou chamada isolada não é anunciado como efeito no jogo.

O servidor deve já autorizar plugins/ScriptHook e pure<2. Se o loader ou a política bloquear o módulo, as funções ficam indisponíveis. Nenhum recurso ou configuração de servidor é instalado ou alterado.

## Comunicação e estado

O pipe local `ZyonClientBridge-v1-<GTA_PID>` possui DACL do usuário atual/SYSTEM e rejeita acesso remoto. O desktop verifica o PID proprietário. Mensagens têm tamanho fixo, token por conexão, revisões e nonces limitados; não transportam hashes arbitrários, ponteiros ou código.

Configurações são copiadas para o quadro de jogo. A sessão permanece válida enquanto aguarda ACK ou condições de voo; a revisão aplicada indica a confirmação separadamente. A lease expira em três segundos. A perda de execução nativa após uma sessão válida revoga a conexão e impede repetição automática dos efeitos anteriores.

Reviver e suicídio vinculam-se ao personagem confirmado na interface. Troca de personagem, cancelamento, timeout ou conexão substituída invalidam o pedido. Suicídio desliga GodMode e noclip do próprio menu. O sucesso depende da observação nativa correspondente, não apenas do recebimento do pacote.

GodMode, invisibilidade e colisão/congelamento têm restauração dos estados pertencentes ao módulo. Mudança de entidade/modelo descarta o snapshot anterior; falhas de restauração não devem apagar sua pendência. O leitor de memória e o módulo são separados por função para evitar dois controladores escrevendo o mesmo estado.

## Limites e teste no Windows

O revive é nativo. ESX/QBCore e hospitais possuem estados adicionais que esta ponte não limpa. Apenas estar nativamente vivo não comprova recuperação médica. O módulo não identifica cargos administrativos nem implementa Silent Aim, voz recebida, nomes/IDs ou oclusão.

Depois de preparar e abrir o FiveM, teste uma função por vez e salve o JSON. Confira primeiro o módulo conectado e as capacidades anunciadas. O diagnóstico mantém o estado nativo e o externo separados, incluindo ações/revisões e motivo de bloqueio. A execução real em Windows/FiveM ainda não foi realizada neste ambiente Linux.

## Referências primárias

CFX revisado em `f5c46b3f1a6941982bdbe303185b13cbccb74e5d`:

- [Loader ASI](https://github.com/citizenfx/fivem/blob/f5c46b3f1a6941982bdbe303185b13cbccb74e5d/code/components/asi-five/src/Component.cpp): pasta plugins, pure level e recurso de build.
- [Compatibilidade ScriptHookV](https://github.com/citizenfx/fivem/blob/f5c46b3f1a6941982bdbe303185b13cbccb74e5d/code/components/scripthookv/src/VishCompat.cpp): scheduler, natives e permissões.
- [RAGE native lookup](https://github.com/citizenfx/fivem/blob/f5c46b3f1a6941982bdbe303185b13cbccb74e5d/code/components/rage-scripting-five/src/scrEngine.cpp): consulta e mapeamento de handlers.

A escrita de saúde externa em `ped+0x280` é corroborada pelo setter [CFX Entity.HealthFloat](https://github.com/citizenfx/fivem/blob/1bc76290982a77829f95b4267be7a493412cdc17/code/client/clrcore/External/Entity.cs#L82). Esse setter não estabelece um fluxo de ressurreição.
