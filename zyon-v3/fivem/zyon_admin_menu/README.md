# Zyon 3.1.1 — recurso administrativo FiveM

Copie esta pasta para `resources/[local]/zyon_admin_menu`, mantendo o nome. Use o aplicativo e o recurso 3.1.1 juntos; o protocolo e o schema diferem dos pacotes 2 e 3.0.

No `server.cfg`, conceda a ACE ao grupo administrativo existente e inicie o recurso:

```cfg
add_ace group.admin zyon.modmenu allow
set zyon_menu_medical_provider "auto"
ensure zyon_admin_menu
```

Entre na cidade com uma conta que pertença a esse grupo e com o menu fechado, abra `Abrir-modo-administrativo.cmd`; se a janela externa estiver aberta, use o botão **Abrir modo administrativo** da aba Noclip/Conexão. O pareamento é automático; nenhum token é digitado ou copiado. O recurso verifica a conexão, a identidade `license:` e a ACE a cada operação. Um controlador ativo é admitido por vez.

Se FXServer e aplicativo estiverem em computadores diferentes, configure `zyon_menu_public_url` com a URL HTTPS completa do recurso, por exemplo `https://admin.example.com/zyon_admin_menu/`, servida por um proxy com certificado confiável. O programa aceita HTTP somente em loopback. Preserve as restrições administrativas do seu servidor.

## Funções

- Noclip/voar: W/A/S/D seguem a câmera; Espaço sobe, Ctrl desce, Shift triplica a velocidade. O slider aceita 1–50 m/s. Somente o ciclo de quadros avança a posição: receber uma nova velocidade não reinicia o voo nem adiciona um passo extra. É necessário personagem vivo, fora de veículo, sem attachment e com controle da entidade.
- Reviver: seleciona o fluxo médico do servidor. Standalone usa ressurreição nativa; ESX atual usa o export de `esx_death`; ESX legado compatível usa o evento e callback oficiais; QBCore usa `hospital:client:Revive` e verifica `isdead`/`inlaststand` no cliente e no servidor. Consulte [os perfis suportados](../../docs/medical-integration.md). Suicídio desativa o GodMode e o noclip deste menu, altera a saúde e aguarda confirmação do estado de morte. Ambas as ações usam tickets e recusam revisões/leases antigas. Pedidos HTTP são aceitação; eventos separados informam o resultado.
- Invisibilidade: salva a visibilidade observada e restaura ao desativar, mudar de personagem, expirar ou parar o recurso. A visibilidade para outros clientes deve ser conferida no seu servidor.
- Silenciar voz recebida: usa `MumbleSetVolumeOverride` localmente nos jogadores ativos e restaura o comportamento padrão ao desativar. Não silencia o microfone nem aplica punição global. O controle aparece somente quando o cliente confirma o native. Recursos de voz que alteram os mesmos overrides podem exigir adaptação.
- ESP: lê coordenadas/projeta/desenha a cada quadro. ADMs são jogadores com ACE `zyon.modmenu`; seus IDs são enviados somente ao administrador pareado. O rótulo ADM aparece mesmo com nomes desligados. Apenas entidades presentes no streaming podem ser exibidas; o alcance respeita o slider. Outros sistemas de cargo precisam mapear suas permissões para essa ACE.
- GodMode, Aim e armas: integração nativa reaproveitada com restauração dos estados observáveis. Silent Aim continua indisponível; Aim move a câmera e não redireciona projéteis.

A opção `auto` atende standalone, ESX e QBCore em servidores separados. É possível configurar `standalone`, `esx` ou `qbcore` explicitamente. Se houver uma base carregada sem seu hospital padrão, o reviver é bloqueado com diagnóstico. Hospitais personalizados e o ESX intermediário sem callback médico exigem integração própria; não há fallback nativo nesses casos. Reviver no QBCore confirma metadados em execução; a gravação em SQL segue o ciclo normal de salvamento do framework. Valide revive/suicídio com o hospital real antes de usar fora do desenvolvimento.

Ao desconectar, o servidor solicita a restauração. A perda de heartbeat/lease encerra os efeitos em até 15 s; perda de ACE, saída do jogador e parada do recurso também revogam a sessão. O pareamento sem renovação expira em 120 s. Mutações validam geração, identidade e autorização novamente após receber o corpo HTTP. A configuração e o roster de ADMs não são eventos de broadcast.

## API e validação

A API autenticada fica em `/zyon_admin_menu/api/`: GET `status`, GET `events?since=0`, POST `settings`, POST `revive`, POST `suicide`, POST `disconnect`. Os POST de ação usam JSON `{}`; `settings` usa snapshot completo. A revisão aplicada só avança após ACK válido. Ações pendentes bloqueiam outra ação por até 10 s; um HTTP 200 não confirma execução no jogo.

Os testes automatizados usam mocks de FXServer/natives e verificam autorização, estados, revisões, restauração, movimento, roster e contratos de pareamento. Eles não executam GTA V. A validação real exige Windows/FiveM: teste controles durante o voo, ESP em movimento, reviver/suicídio com seu hospital, invisibilidade, voz, remoção de ACE e restauração após desconectar.
