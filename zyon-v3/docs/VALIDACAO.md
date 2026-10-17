# Validação da versão 3.3.3

As publicações Windows x64 completa e compacta passaram com warnings tratados como erros. A inspeção dos recursos .NET confirmou, em ambas as publicações, que os **455.680 bytes** do ASI embutido correspondem ao módulo nativo compilado. O PE foi validado como DLL AMD64 com recurso `FX_ASI_BUILD` de tipo `3258` e dependências de sistema.

| Verificação | Resultado |
| --- | --- |
| C# portável, incluindo pipes reais com jogo simulado | 16 suítes, 711 assertivas |
| Varredura PE, incluída nas suítes C# | 58 verificações |
| Provider local, incluído nas suítes C# | 20 cenários, 145 assertivas |
| Inspeção de instalação, incluída nas suítes C# | 29 verificações |
| Motor nativo com natives simuladas e PE sintético | 28 suítes, 199 assertivas |
| Disparo direcionado experimental com natives simuladas | 7 suítes, 150 assertivas |
| Preservação de erro de inicialização e estados nativos | 8 suítes, 61 assertivas |
| HTTP/pareamento local | 30 verificações |
| Contratos Lua do servidor simulado | 54 verificações |
| Lua cliente com natives simuladas | 166 assertivas |
| Integração médica simulada | 25 testes |
| Pareamento Lua cliente | 51 assertivas |
| NUI JavaScript | 23 testes, sem falhas ou skips |

As regressões novas reproduzem o módulo autenticado que nunca executa e preservam GodMode/suicídio por memória. Verificam um único dono por processo, ausência de migração automática, cancelamento/epoch do personagem, rejeição inicial sem fixar um dono inválido e ausência de replay quando o módulo passa a executar. Falhas repetidas da integração não apagam uma sessão exclusivamente de memória.

A limpeza da última opção nativa exige a revisão aplicada correspondente, além de ACK de recebimento. Enquanto essa observação não chega, a revisão não é anunciada como aplicada. Depois da limpeza confirmada, revisões só de memória não fazem I/O desnecessário no módulo opcional. A política da interface mantém GodMode/suicídio por capacidade validada, sem liberar noclip/revive pela prontidão da memória.

A regressão de varredura usa seção de 33 MiB com assinaturas depois do limite antigo, correspondências em fronteiras de blocos/páginas, fallback recuperável, página inacessível, assinaturas ambíguas e cabeçalhos inválidos. A cobertura incompleta não autoriza campos. Os casos novos cobrem tamanhos PE/Windows diferentes e cabeçalho NT transplantado para outro endereço DOS, mantendo rejeição de seções truncadas ou fora dos dois limites. Nenhuma leitura de processo Windows real foi executada nesta validação.

Os testes nativos novos distinguem callback nunca iniciado, interrompido e de outra conexão, preservando erros específicos recentes de timer/personagem. Nenhum heartbeat concede prontidão nativa ou confirmação de configurações.

As verificações de instalação distinguem módulo ausente, diferente, ilegível e correspondente no disco; limitam os arquivos lidos e não tratam hash correspondente como execução nativa. A prontidão do módulo é exposta separadamente da prontidão do leitor de memória. As falhas de inicialização são preservadas após Hello e reconexões, em vez de virarem um aviso genérico de permissão.

O ambiente de desenvolvimento utiliza SDK .NET 8.0.426, Zig 0.15.2 e compilação cruzada no Linux. Os testes portáveis usam comunicação local real quando indicado e simulam o jogo e as natives; não substituem uma sessão Windows/FiveM.

As verificações cobrem protocolo 2 e configurações de 48 bytes, capacidades do módulo, perda de conexão, ações sem replay, preservação de estados, controle/readback das portas do veículo e condições do protótipo experimental de Silent Aim. A inspeção do PE e do recurso embutido não carrega código Windows.

**Ainda não validado em Windows/FiveM real:** janela WinForms e DPI, instalação e loader do ASI, exports do cliente instalado, política do servidor, controle de rede, chamadas nativas e efeitos dentro do jogo. Não se afirma que todas as funções estejam funcionando nessa sessão real.

O Silent Aim usa `SET_PED_SHOOTS_AT_COORD` como protótipo experimental. A presença do handler e o consumo observável de munição não comprovam redirecionamento do tiro, hit ou dano. Enquanto a opção estiver ativa, o tiro normal pode ser impedido, inclusive sem alvo no FOV. A validação exige teste no jogo e diagnóstico do resultado.

Bloquear/desbloquear veículo observa apenas o estado nativo das portas após obter controle oficial. Não concede chaves de ESX/QBCore nem confirma persistência diante de scripts de garagem/chaves. O revive é nativo e não confirma limpeza médica desses frameworks.

Voz recebida, ADMs/nomes e visibilidade do ESP externo continuam sem implementação. Testes médicos da integração administrativa anterior não estabelecem suporte médico no módulo cliente.

Os pacotes 3.3.3 incluem o ASI idêntico ao da 3.3.2, com protocolo 2 em `ZyonClientBridge-v2-<GTA_PID>`. O SHA256 foi comparado ao manifesto anterior. No caso mostrado, com ASI já carregado, esta atualização exige trocar somente o aplicativo. O pacote apresenta um único inicializador `Abrir-Zyon.cmd`, sem seleção de modo.

O print da 3.3.2 confirma que o callback nativo nunca entrou no servidor usado pelo usuário. Esta versão corrige o roteamento de funções de memória e a validação PE; não modifica o scheduler nem comprova solução para a execução das funções exclusivamente nativas nessa cidade.
