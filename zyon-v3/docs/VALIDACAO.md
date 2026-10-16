# Validação da versão 3.2.0

SDK .NET8.0.426 e Zig0.15.2. As publicações Windows x64 completa e compacta passaram no Linux, com warnings tratados como erros, sem erros ou avisos. A instalação do módulo no cliente Windows e uma sessão real FiveM não foram executadas aqui.

`bash scripts/test.sh` concluiu:

- 14 suítes C# portáveis, **538 assertivas**.
- Dentro dessas suítes, o provedor de módulo possui 10 cenários/71 assertivas com named pipes reais e jogo simulado: ACK pendente, velocidade/voo em espera, épocas/PID, concorrência, corrupção, timeout sem replay, encerramento e perda de autorização com nova conexão sem reaplicar efeitos.
- 20 suítes C++ / **117 assertivas**, com natives simulados e PE sintético: dependências de capacidades, hover/foco, restauração retida, readbacks, ações e epochs.
- 30 verificações C# de HTTP/pareamento reais em loopback.
- 54 contratos servidor, 166 assertivas cliente, 25 contratos médicos e 51 assertivas de pareamento com mocks da integração administrativa anterior.
- 23 testes NUI, sem falhas ou skips.

O ASI foi compilado como DLL AMD64 com entry point e recurso customizado `FX_ASI_BUILD`, tipo numérico `3258`. O verificador rejeita saída que não seja PE e imports externos inesperados. Os imports observados são componentes Windows10/11: Kernel32, Advapi32, User32 e contratos UCRT.

A inspeção de metadata da assembly comparou os **432128 bytes** do módulo embutido com o ASI compilado, sem carregar código Windows. SHA256 do ASI: `56f988d75c8eff53db5bde5b25c4b1536c314cca6bc7772586ba8019c390f082`.

Os pacotes incluem os binários dessas publicações, fontes e licenças. O manifest registra resultados e hashes dos binários; `downloads/ZyonMenu-v3.2.0-packages.json` registra os hashes dos ZIPs. Nenhum DLL privado recebido anteriormente foi executado ou redistribuído.

**Ainda não validado:** janela WinForms e DPI, instalação/loader real do ASI, exports do cliente efetivamente instalado, políticas do servidor, natives e comportamento dentro do FiveM. O usuário confirmou que poderá fazer essa validação no Windows.

Silent Aim, voz recebida, ADMs/nomes e visibilidade continuam sem implementação. O revive do módulo é nativo e não confirma limpeza médica ESX/QBCore. Os testes médicos antigos não estabelecem suporte médico no módulo cliente.
