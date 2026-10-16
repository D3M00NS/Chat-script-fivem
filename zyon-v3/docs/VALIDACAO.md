# Validação da versão 3.3.0

As publicações Windows x64 completa e compacta passaram com warnings tratados como erros. A inspeção dos recursos .NET confirmou, em ambas as publicações, que os **452.096 bytes** do ASI embutido correspondem ao módulo nativo compilado. O PE foi validado como DLL AMD64 com recurso `FX_ASI_BUILD` de tipo `3258` e dependências de sistema.

| Verificação | Resultado |
| --- | --- |
| C# portável, incluindo pipes reais com jogo simulado | 14 suítes, 587 assertivas |
| Provider local, incluído nas suítes C# | 15 cenários, 112 assertivas |
| Motor nativo com natives simuladas e PE sintético | 28 suítes, 196 assertivas |
| Disparo direcionado experimental com natives simuladas | 7 suítes, 150 assertivas |
| HTTP/pareamento local | 30 verificações |
| Contratos Lua do servidor simulado | 54 verificações |
| Lua cliente com natives simuladas | 166 assertivas |
| Integração médica simulada | 25 testes |
| Pareamento Lua cliente | 51 assertivas |
| NUI JavaScript | 23 testes, sem falhas ou skips |

As regressões incluem a recusa de uma ação sem capacidade seguida de outra ação válida, cancelamento fora da trava da sessão, controle e readback de portas, ausência de replay, preservação de GodMode/noclip após falha experimental e leitura de consumo antes da reposição de munição. O resultado experimental permanece disponível ao alternar para o menu para salvar o diagnóstico.

O ambiente de desenvolvimento utiliza SDK .NET 8.0.426, Zig 0.15.2 e compilação cruzada no Linux. Os testes portáveis usam comunicação local real quando indicado e simulam o jogo e as natives; não substituem uma sessão Windows/FiveM.

As verificações cobrem protocolo 2 e configurações de 48 bytes, capacidades do módulo, perda de conexão, ações sem replay, preservação de estados, controle/readback das portas do veículo e condições do protótipo experimental de Silent Aim. A inspeção do PE e do recurso embutido não carrega código Windows.

**Ainda não validado em Windows/FiveM real:** janela WinForms e DPI, instalação e loader do ASI, exports do cliente instalado, política do servidor, controle de rede, chamadas nativas e efeitos dentro do jogo. Não se afirma que todas as funções estejam funcionando nessa sessão real.

O Silent Aim usa `SET_PED_SHOOTS_AT_COORD` como protótipo experimental. A presença do handler e o consumo observável de munição não comprovam redirecionamento do tiro, hit ou dano. Enquanto a opção estiver ativa, o tiro normal pode ser impedido, inclusive sem alvo no FOV. A validação exige teste no jogo e diagnóstico do resultado.

Bloquear/desbloquear veículo observa apenas o estado nativo das portas após obter controle oficial. Não concede chaves de ESX/QBCore nem confirma persistência diante de scripts de garagem/chaves. O revive é nativo e não confirma limpeza médica desses frameworks.

Voz recebida, ADMs/nomes e visibilidade do ESP externo continuam sem implementação. Testes médicos da integração administrativa anterior não estabelecem suporte médico no módulo cliente.

Os pacotes 3.3.0 devem conter aplicativo e ASI atualizados juntos. O protocolo 2 usa `ZyonClientBridge-v2-<GTA_PID>` e é incompatível com o módulo 3.2.0. Feche o FiveM e execute **Preparar módulo local** novamente antes do teste. Exporte **Salvar diagnóstico** com o jogo aberto se o módulo continuar desconectado.
