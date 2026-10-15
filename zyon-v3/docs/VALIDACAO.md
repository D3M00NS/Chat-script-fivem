# Validação da versão 3.1.1

Publicações Windows x64 completa (runtime incluído) e compacta concluídas no Linux, sem erros ou avisos, com SDK .NET 8.0.426.

`bash scripts/test.sh` executou e concluiu:

- 8 suítes portáveis: 129 assertivas de projeção, timing, assinaturas e política da interface.
- 30 verificações C#: HTTP e pareamento reais em loopback, cancelamento, limites, schema e serialização.
- 54 contratos servidor com mocks: autenticação, ACE/licença, gerações, leases, ações e ADMs.
- 166 assertivas cliente com natives simulados: movimento por quadro, ajustes de velocidade, ACKs, projeção e restauração.
- 25 contratos médicos: seleção de standalone/ESX/QBCore, estados médico/nativo, callback legado, exports atuais, timeout e reinício.
- 51 assertivas de pareamento cliente.
- 23 testes NUI, sem falhas, cancelamentos ou skips.

Verificações específicas incluem velocidade ajustada continuamente a cada 60 ms sem atingir rate limit, ausência de passos extras de movimento em mensagens de configuração, rejeição de ações após reset/revisão substituída, roster privado por ACE, mute nativo confirmado e restauração de invisibilidade/voz/voo.

Os ZIPs são montados somente com os executáveis destas publicações e fontes atuais. `manifest.json` registra a validação e hashes dos binários; `ZyonMenu-v3.1.1-packages.json` registra hashes dos pacotes. Nenhum executável recebido no ZIP original foi usado para execução ou como binário desta versão.

A janela Windows, as leituras/escritas Win32 do jogo e uma sessão GTA/FiveM real não foram executadas. A integração médica implementada para APIs padrão ESX/QBCore, os recursos de voz e a visibilidade para outros clientes precisam ser conferidos no servidor do usuário. ESX 1.14/1.15 sem o callback legado e hospitais personalizados exigem adaptação; não são apresentados como fluxo médico validado. Silent Aim permanece indisponível.
