# Zyon 3.1.1 — standalone, ESX e QBCore

Fonte extraída do pacote v3 da branch `download-zyon-menu-v1`, com correções de atualização do ESP e integração administrativa opcional reaproveitada da v2. O modo externo continua direcionado a `FiveM_b3258_GTAProcess.exe`; o modo administrativo usa o recurso do seu próprio servidor.

## Abrir

No pacote completo, extraia tudo para uma pasta nova, feche a versão anterior e abra `ZyonMenu.exe`. O runtime está incluído. O pacote compacto requer .NET Desktop Runtime 8 x64.

Para reviver, suicídio, noclip/voar, invisibilidade, voz recebida e ADMs no ESP, instale `fivem/zyon_admin_menu` conforme o [README do recurso](fivem/zyon_admin_menu/README.md). Com o menu fechado, abra `Abrir-modo-administrativo.cmd` ou execute `ZyonMenu.exe --admin`. Se a janela externa estiver aberta, use **Abrir modo administrativo** na aba Noclip/Conexão. Não é necessário inserir chave. Os efeitos externos são desligados antes de abrir a janela administrativa, evitando dois controladores do mesmo estado.

## Correções

- Aba Noclip dedicada com velocidade de 1–50 m/s e controles de voo.
- Movimento nativo atualizado por quadro e sem passos extras ao receber um ajuste de velocidade.
- ESP externo em thread STA própria, independente da interface; coleta e desenho usam intervalos de 16 ms, sujeitos à carga e ao agendamento do Windows. Quadros antigos são descartados e ossos parcialmente projetados deixam de ocultar todo o personagem.
- A suavização da mira usa o tempo decorrido, preservando sua resposta ao mudar a frequência de coleta. Configurações não são serializadas por quadro; sliders enviam atualizações agrupadas em 60 ms, sem esperar que o usuário solte o controle.
- ESP administrativo desenhado por quadro; ADMs identificados pela ACE do servidor, com rótulo próprio mesmo com nomes desligados.
- Reviver e suicídio com confirmação separada; invisibilidade e voz recebida com restauração ao encerrar os efeitos.
- Reviver com seleção automática standalone/ESX/QBCore por servidor: utiliza o fluxo médico oficial, confirma os estados disponíveis e informa integração ausente em vez de anunciar sucesso apenas pela vida nativa.
- `Directory.Build.props` incluído: sua ausência no ZIP original impedia compilar Core/testes sem parâmetros externos.

## Limites

Silent Aim continua indisponível. Não há integração validada de redirecionamento de projéteis, e alterar vida não foi usado como substituto de ressurreição. O modo externo sozinho também não executa os natives de noclip/revive, identifica ADMs ou lê nomes/IDs da cidade. Use o recurso administrativo para essas funções.

Reviver seleciona o fluxo nativo no standalone, `esx_death` no ESX atual, o evento/callback oficial no ESX legado compatível ou `qb-ambulancejob` no QBCore. Hospitais personalizados e versões ESX sem essa API exigem adaptação; consulte [integração médica](docs/medical-integration.md). A identificação ADM usa `zyon.modmenu`, não tenta adivinhar cargos. Silenciar refere-se ao áudio de voz recebido localmente. Apenas entidades em streaming entram no ESP.

Este ambiente Linux compila o aplicativo Windows e executa testes portáveis/mocks. A janela WinForms, acesso ao processo e comportamento real no FiveM não foram executados aqui. Não interprete compilação ou testes com mocks como validação dentro do jogo.

## Desenvolvimento

Use o SDK .NET 8.0.426, Lua 5.4 e Node.js. No Windows:

```powershell
powershell -File scripts/build.ps1
powershell -File scripts/test.ps1
```

No Linux, `bash scripts/test.sh`. É possível indicar executáveis por `ZYON_DOTNET_COMMAND`, `ZYON_LUA_COMMAND` e `ZYON_NODE_COMMAND`. Os testes executam geometria/timing/política de UI, HTTP e pareamento reais em loopback, contratos Lua e NUI.

Para empacotar as duas publicações, execute `python release/pack_release.py`. Configure `ZYON_DOTNET_ROOT` para o SDK usado e `NUGET_PACKAGES` para o cache das dependências. O ZIP inclui executáveis produzidos localmente, fontes, recurso administrativo e licenças do runtime; não inclui DLLs privadas recebidas para análise.
