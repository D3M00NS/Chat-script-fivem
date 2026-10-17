# Chat-script-fivem

[Zyon 3.3.2 — baixar pacote completo](downloads/ZyonMenu-v3.3.2-completo.zip) · [Fontes e instruções](zyon-v3/README.md) · [Validação](zyon-v3/docs/VALIDACAO.md)

Versão de teste Windows x64/FiveM b3258 com módulo instalado somente no cliente pelo próprio executável. Não exige cargo de administrador ou recurso no servidor; o servidor precisa já permitir plugins/ScriptHook e usar pure level abaixo de 2.

Mantém GodMode, noclip/voar, invisibilidade, munição, suicídio e revive nativo. Acrescenta ações de portas do veículo e um protótipo experimental de Silent Aim, que pode não produzir tiro ou acerto. Não identifica ADMs nem limpa o estado médico ESX/QBCore. O comportamento real no Windows/FiveM ainda não foi validado.

Na atualização, feche o FiveM e use **Preparar módulo** novamente antes de abrir o jogo. A versão 3.3.2 lê seções executáveis em blocos, removendo o descarte acima de 32 MB, e preserva falhas específicas de execução do callback nativo. Uma varredura incompleta mantém os campos de arma bloqueados. O print da versão anterior confirma comunicação com o ASI, mas ainda não identifica a causa da ausência de execução nativa. [Detalhes](zyon-v3/docs/client-module-3.3.2.md).
