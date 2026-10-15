# Arquivos reais do cliente 1.0.0.37151

Análise estática dos três DLLs enviados. Nenhum foi carregado ou executado. Todos são PE32+ x64 e expõem a versão de arquivo/produto `1.0.0.37151` em `VS_FIXEDFILEINFO`.

| Arquivo | Bytes | Exports nomeados | SHA-256 |
| --- | ---: | ---: | --- |
| `rage-scripting-five.dll` | 1716320 | 66 | `2041c76b2f59882eff04bb5d3fd86297701de1bb1d716503df1cf698ac5fa77c` |
| `scripting-gta.dll` | 279136 | 14 | `e7844e83f4f256c4af43e5719460e2c8730e2e0b349d106a038dfa046e084d37` |
| `gta-game-five.dll` | 222816 | 70 | `a546575c3966e2caa4d6bc793de2f17462b545d49ef907e0ae8e47f35309228e` |

A presença de tabela de certificado não foi tratada como validação de assinatura Authenticode. A versão do cliente não substitui a identificação da imagem GTA nem valida, por si, os deslocamentos do perfil b3258.

## Scripting

No primeiro arquivo, `rage::scrEngine::GetNativeHandler` está no RVA `0x16E60`, `GetActiveThread` em `0x16D40`, `SetActiveThread` em `0x17120` e `GetThreadCollection` em `0x16F00`. As operações de thread usam TLS do processo/thread corrente, incluindo `gs:[0x58]`; não selecionam a thread de outro processo por PID.

`scripting-gta.dll` exporta `WrapNativeInvoke` em `0x25660`. A ABI Win64 confirmada é:

```text
handler: ponteiro de callback RAGE em RCX
nativeIdentifier: uint64 em RDX
context: scrNativeCallContext* RAGE em R8
errorMessage: char** em R9
```

O wrapper executa o handler imediatamente na thread chamadora. Ele não transporta argumentos entre processos nem agenda trabalho no loop do jogo. Os exports `fx::ScriptEngine::GetNativeHandler` e `GetNativeHandlerPtr` trabalham com objetos MSVC `std::function`; esses objetos não podem ser usados como callbacks RAGE crus.

Também há dois layouts de contexto diferentes: o contexto `fx` mantém argumentos em `+0`, resultados em `+8` e contagem em `+0x10`; o contexto RAGE mantém resultados em `+0`, contagem em `+8`, argumentos em `+0x10` e contagem de dados em `+0x18`. Os tipos não são intercambiáveis.

Não foi encontrada exigência explícita de recurso no caminho normal desse wrapper. Isso não prova segurança de chamada a partir de uma thread arbitrária. A ressurreição real continua sem implementação/validação; `ReviveNative` deste componente altera registros de hashes e não revive o jogador.

## Associação do jogador

| Accessor de `gta-game-five.dll` | RVA | Fato observado |
| --- | --- | --- |
| `CNetworkPlayerMgr::GetPlayer` | `0x10850` | Salta pelo ponteiro inicializado em `DLL+0x30048`, que aponta para código da imagem GTA. |
| `CNetGamePlayer::GetPlayerInfo` | `0x10860` | Retorna o endereço do campo em `this + int32(DLL+0x33B40)`; é necessário ler o ponteiro armazenado nesse campo. |
| `netPlayer::GetGamerInfoBase` | `0x10830` | Chama o getter na vtable pelo deslocamento em bytes lido de `DLL+0x33B30`. |
| `activePlayerIndex` | `0x10870` | Usa o deslocamento inicializado em `DLL+0x33B38`. |
| `physicalPlayerIndex` | `0x10890` | Usa o deslocamento inicializado em `DLL+0x33B3C`. |

Os valores dos globais são preenchidos durante a inicialização. Valores zero na seção não inicializada do arquivo não são offsets válidos para o leitor externo. O DLL não contém o corpo do getter GTA apontado por `+0x30048`, nem o getter efetivo da vtable, portanto ainda não estabelece a coleção de jogadores ou o vínculo completo com os personagens.

O código-fonte CFX estabelece que, para `PeerAddress<2824>` usado no b3258, o ID do servidor vem de `(uint32(gamerInfo+0xB0) & 0xffff) ^ 0xFEED`. É necessário resolver primeiro `gamerInfo` e associar o jogador ao `CPlayerInfo` do ped. `ped+0xB0`, `playerInfo+0xE8`, índice da lista e PID não substituem esse caminho.

## Pendências da instalação

Na consulta inicial, `gta-net-five.dll`, `citizen-resources-gta.dll` e `citizen-playernames-five.dll` não apareceram. A listagem também omitiu os três DLLs já recebidos e não representava a instalação efetivamente usada.

O caminho do processo informou uma instalação sob `%LOCALAPPDATA%\FiveM\FiveM.app\data\cache\subprocess\FiveM_b3258_GTAProcess.exe`. Uma busca explícita na raiz `%LOCALAPPDATA%\FiveM\FiveM.app`, incluindo arquivos ocultos, encontrou os três componentes, além de `citizen-game-ipc.dll`, `citizen-game-main.dll` e DLLs de recursos. Os três arquivos encontrados ainda precisam ser recebidos para inspeção estática. Também foi solicitado `rage-nutsnbolts-five.dll`, componente que o código-fonte CFX identifica como exportador dos eventos de frame. O caminho completo do perfil Windows foi omitido desta documentação distribuível.

Nomes CFX não comprovam nome ou Steam ID autenticado dos participantes. Nenhuma implementação de identificação foi adicionada com valores presumidos. A versão independente permanece em desenvolvimento. O botão **Salvar diagnóstico** registra os módulos selecionados por nome e os dados de resolução necessários, sem executar os DLLs ou incluir caminhos completos do perfil Windows.
