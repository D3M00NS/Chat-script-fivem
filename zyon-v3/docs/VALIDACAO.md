# Validação da versão 3.4.0

O aplicativo Windows usa somente o backend externo. A publicação completa e a compacta passam por inspeção de metadados sem executar Windows: não podem conter recurso ASI, tipos de implementação `ClientModule*` ou imports de plugins. A inspeção tem controle negativo: a assembly de testes com a implementação histórica foi rejeitada como esperado.

| Verificação | Resultado |
| --- | --- |
| C# portável | 17 suítes, 755 assertivas |
| Remoção reversível do plugin legado, incluída no C# | 28 verificações |
| Política externa da interface, incluída no C# | 43 verificações |
| Varredura PE, incluída no C# | 58 verificações |
| Publicações Windows x64 completa e compacta | Sem avisos ou erros, com warnings tratados como erros |
| Metadados das duas publicações | Sem payload ASI, implementação do cliente ou imports de plugin |
| Provider antigo, somente fonte de regressão | 20 cenários, 145 assertivas |
| Inspeção antiga, somente fonte de regressão | 29 verificações |
| Fontes C++ históricas, sem ASI distribuído | 43 suítes, 410 assertivas com natives simuladas |
| HTTP/pareamento administrativo histórico | 30 verificações |
| Contratos Lua do servidor simulado | 54 verificações |
| Lua cliente com natives simuladas | 166 assertivas |
| Integração médica simulada | 25 testes |
| Pareamento Lua cliente | 51 assertivas |
| NUI JavaScript | 23 testes, sem falhas ou skips |

A remoção usa diretórios temporários e imagens de teste com hash conhecido injetado. Verifica arquivo ausente, jogo aberto, início do jogo antes da renomeação, arquivo desconhecido mesmo com metadata local correspondente, backup existente, preservação de DLLs e de outros plugins, arquivo grande/truncado, preferência malformada/grande e links. Os hashes de produção foram comparados aos manifestos das releases publicadas. O helper não confia em um hash fornecido somente por metadata local.

A política externa bloqueia controles e despacho de funções retiradas mesmo diante de capacidades nativas antigas. O título neutro da versão 3.4.0 continua identificável pelo controle de instâncias, sem reativar uma sessão 3.3.x com plugin.

Os testes PE continuam cobrindo seções maiores que 32 MiB, fronteiras, recuperação por páginas, tamanhos Windows/PE diferentes e cabeçalhos transplantados pelo loader. A leitura incompleta de 16 KiB vista no print permanece bloqueada; não foi tratada como padding sem evidência.

Os contratos históricos são executados para regressão do repositório. Não estabelecem suporte nativo, médico ou administrativo no aplicativo externo. O código do provider/instalador ASI foi excluído do projeto Windows; nenhum ASI é exigido pelo empacotador ou incluído nos pacotes 3.4.0.

**Não executado neste ambiente:** janela Windows, detecção de processos Windows, remoção da instalação real do usuário, leitura de memória real e efeitos no FiveM. Testes de arquivo no Linux e compilação cruzada não comprovam esses comportamentos.

Noclip, revive, invisibilidade, Freeze Ammo, Silent Aim, portas, voz recebida, ADMs/nomes e linha de visão não têm implementação externa validada nesta versão. A remoção da dependência não implementa essas funções.
