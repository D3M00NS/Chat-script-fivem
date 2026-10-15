# Validação da versão 3.1.2

Publicações Windows x64 completa (runtime incluído) e compacta concluídas no Linux com SDK .NET 8.0.426 e warnings tratados como erros. Não houve erros ou avisos.

`bash scripts/test.sh` executou e concluiu:

- 11 suítes portáveis: 300 assertivas, incluindo projeção, padding da matriz de ossos, estado da interface, leitura posterior de vetores, diagnóstico com NaN/Infinity e mutex real entre processos.
- 30 verificações C#: HTTP e pareamento reais em loopback, cancelamento, limites, schema e serialização.
- 54 contratos servidor com mocks.
- 166 assertivas cliente com natives simulados.
- 25 contratos médicos da integração administrativa anterior.
- 51 assertivas de pareamento cliente.
- 23 testes NUI, sem falhas ou skips.

Os testes de regressão reproduzem o descarte indevido de ossos por padding não finito, a política de reabertura de versões diferentes, a exclusão entre dois processos e recuperação após saída do proprietário. A identificação de cópias abertas usa o título gerado pela versão da assembly, pois o PE do apphost publicado no Linux pode manter a versão do runtime. Os testes verificam a normalização de três para quatro componentes e a identificação de versões futuras e títulos antigos. A suíte também rejeita leituras inválidas, assinaturas ambíguas e deslocamentos negativos de instruções com disp8. Não simula o efeito de disparos no FiveM.

O diagnóstico registra configuração selecionada, leituras por função e pendências de restauração. Revisão estática verificou a retenção de snapshots após falhas de restauração, retentativas condicionadas à identidade e descarte explícito por mudança de contexto. Essas operações Win32 não foram executadas neste ambiente.

Os pacotes incluem os binários das publicações locais, fontes atuais e licenças. O manifest registra resultados e hashes dos binários; `downloads/ZyonMenu-v3.1.2-packages.json` contém os hashes dos ZIPs. O pacote abre no modo externo e não requer recurso no servidor.

A janela Windows, escala DPI real, leituras/escritas Win32 e uma sessão GTA/FiveM não foram executadas. Aim, ESP, GodMode e modificadores de armas continuam dependendo da conferência no cliente do usuário. Silent Aim, reviver, suicídio, noclip, invisibilidade, voz recebida, ADMs/nomes e Freeze Ammo não foram implementados no backend externo; testes da integração administrativa anterior não estabelecem suporte externo.
