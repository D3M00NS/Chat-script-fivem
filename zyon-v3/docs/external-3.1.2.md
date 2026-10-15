# Diagnóstico externo 3.1.2

Abra `ZyonMenu.exe`, confirme a versão no título e entre no FiveM b3258. Ative a função com falha e reproduza o problema. Para testar Aim, mantenha um alvo dentro do FOV e pressione a tecla de ativação com a janela do jogo em primeiro plano. Volte ao menu e use **Salvar diagnóstico**. Envie o arquivo JSON gerado.

O arquivo registra:

- Processo, módulo, personagem local, posição, vida e validação da matriz.
- Viewport, replay, lista de personagens e contadores dos filtros, falhas de ossos e projeção.
- Última tentativa de Aim: câmera, alvo, direção candidata, escrita e leitura posterior. A tentativa é preservada ao tirar o foco do jogo para salvar o arquivo.
- Assinaturas de No Recoil/No Spread, campos resolvidos, arma, hash e leituras antes/depois das escritas.
- Estado por função, janela do jogo, idade do quadro e contadores de atividade.

`AppliedRevision` informa que uma configuração foi processada. `FeatureStates` distingue disponível, aguardando, leitura confirmada, bloqueada e não implementada. A leitura confirmada mostra o valor observado; não comprova por si só um efeito no jogo. Observações antigas aparecem como desatualizadas na interface.

Valores NaN/Infinity são preservados como strings JSON para permitir examinar uma leitura inválida sem impedir o salvamento do diagnóstico. Nenhum novo endereço de memória foi presumido nesta versão.

Esse diagnóstico ajuda a investigar as funções já implementadas de Aim/ESP/armas. Ele não implementa Silent Aim, reviver, noclip ou as demais funções ausentes.
