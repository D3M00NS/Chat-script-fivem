# Chat-script-fivem

[Zyon 3.3.3 — baixar pacote completo](downloads/ZyonMenu-v3.3.3-completo.zip) · [Fontes e instruções](zyon-v3/README.md) · [Validação](zyon-v3/docs/VALIDACAO.md)

Versão de teste Windows x64/FiveM b3258 com módulo instalado somente no cliente pelo próprio executável. Não exige cargo de administrador ou recurso no servidor; o servidor precisa já permitir plugins/ScriptHook e usar pure level abaixo de 2.

Mantém GodMode, noclip/voar, invisibilidade, munição, suicídio e revive nativo. Acrescenta ações de portas do veículo e um protótipo experimental de Silent Aim, que pode não produzir tiro ou acerto. Não identifica ADMs nem limpa o estado médico ESX/QBCore. O comportamento real no Windows/FiveM ainda não foi validado.

Abra `ZyonMenu.exe`: a conexão é automática, sem escolher entre modos. A versão 3.3.3 recupera o caminho de memória para GodMode e suicídio quando disponível, sem depender da execução do ASI, e corrige a validação de cabeçalhos transplantados pelo loader do FiveM. O módulo da 3.3.2 permanece igual; se já está carregado, basta atualizar o executável. **Isso não confirma que noclip, revive, portas ou Silent Aim passaram a executar no servidor do print.** [Detalhes](zyon-v3/docs/automatic-connection-3.3.3.md).
