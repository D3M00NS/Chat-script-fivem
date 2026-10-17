# Varredura e execução do módulo 3.3.2

O print da 3.3.1 mostra comunicação com o módulo ASI no processo FiveM b3258, sem quadro nativo validado. O personagem já estava andando na cidade. Isso impede atribuir a falha simplesmente ao carregamento, mas não confirma que o servidor negou ScriptHook nem identifica sozinho a causa.

## Correções

A busca de No Recoil e No Spread descartava qualquer seção executável acima de 32 MB. Uma falha de leitura também invalidava a seção inteira sem registrar sua extensão. O scanner agora lê blocos de 64 KiB, tenta páginas de 4 KiB quando um bloco falha e preserva assinaturas entre blocos contíguos. Não junta bytes separados por uma área ilegível. Os limites vêm da imagem PE validada; assinatura ausente, ambígua, fora do intervalo ou análise incompleta mantém o campo desativado. O diagnóstico registra a cobertura e os motivos por seção. **Verificar novamente** refaz a análise com o personagem validado, desde que No Recoil e No Spread estejam desativados e não haja campos aguardando restauração. Isso permite recuperar uma leitura incompleta durante o carregamento sem reiniciar o menu.

O motor registra separadamente a entrada no callback do scheduler e a conclusão da validação nativa. Antes, o aviso genérico de quadro ausente podia substituir uma falha específica de timer ou personagem mesmo com callback em execução. Agora essa falha é preservada quando pertence à conexão atual e está recente. Callback nunca iniciado, callback parado e espera por uma nova conexão têm mensagens distintas. Heartbeat sozinho não habilita funções nem confirma configurações aplicadas.

Essas alterações corrigem a varredura e a informação exibida. Não fornecem evidência de que o callback passará a executar no servidor usado no print. O agendador oficial do FiveM exige ScriptHook permitido e sessão de rede inicializada antes de executar plugins. Não há contorno dessa política nesta versão.

## Atualizar

1. Feche o FiveM e o menu antigo.
2. Extraia o pacote 3.3.2 em pasta nova e abra `ZyonMenu.exe`.
3. Use **Preparar módulo local** para atualizar o ASI na instalação do FiveM.
4. Abra o FiveM, entre na cidade e confira a mensagem de conexão e os resultados por função.

O protocolo continua v2. É preciso atualizar aplicativo e ASI juntos para obter o diagnóstico novo. Os [limites das funções](../README.md) permanecem, incluindo Silent Aim experimental, revive sem recuperação médica ESX/QBCore e portas sem concessão de chaves. Os [testes](VALIDACAO.md) usam natives simuladas e não validam gameplay no Windows/FiveM.

A condição de execução pode ser conferida na [implementação oficial do ScriptHook no FiveM](https://github.com/citizenfx/fivem/blob/master/code/components/scripthookv/src/VishCompat.cpp). Quando o endereço do servidor é conhecido, `vars.sv_scriptHookAllowed` no `/info.json` público fornece a configuração publicada no momento da consulta; campo ausente ou consulta recusada mantém o resultado desconhecido. Isso não substitui uma observação do handshake da sessão atual.
