# Zyon 3.4.0 — externo, sem ScriptHook

Aplicativo para Windows x64/FiveM b3258 com detecção automática. Esta versão usa diretamente `LocalGameSession`. O provider nativo, a inspeção/instalação do ASI e seu recurso embutido foram excluídos da compilação Windows. O menu não faz conexão com o módulo antigo nem verifica permissão de plugins do servidor.

## Abrir e remover a integração anterior

1. Feche a versão anterior do menu e extraia o [pacote completo](../downloads/ZyonMenu-v3.4.0-completo.zip) em pasta nova.
2. Abra `ZyonMenu.exe` ou `Abrir-Zyon.cmd`. O aplicativo detecta o FiveM b3258 diretamente.
3. Para desativar o ASI instalado pelas versões anteriores, feche o FiveM e clique em **Conexão → Remover integração antiga**.

A remoção verifica a pasta `FiveM.app`, o hash do plugin e se o FiveM está fechado. Somente `plugins/ZyonClient.asi` de releases publicadas pode ser renomeado para uma cópia única com extensão `.disabled`. O registro antigo é preservado. Arquivos desconhecidos, links, DLLs do FiveM e outros plugins não são alterados. Não há encerramento forçado do jogo nem elevação automática.

O pacote completo inclui o runtime. O compacto requer .NET Desktop Runtime 8 x64. O parâmetro antigo `--client-module` é recusado com a orientação de abrir o aplicativo normalmente. O fluxo administrativo histórico permanece acessível apenas com `--admin`; ele é separado do aplicativo externo e não usa o ASI.

## Funções

| Função | Situação na 3.4.0 |
| --- | --- |
| Aim e ESP | Implementação externa b3258, condicionada às cadeias e leituras validadas |
| GodMode | Bit de invulnerabilidade do personagem, com snapshot e restauração |
| No Recoil / No Spread | Campos de arma somente após varredura completa e assinatura única |
| Suicídio | Confirmação, restauração do GodMode, uma escrita de vida zero e readback no personagem capturado |
| Noclip / voar, revive, invisibilidade | Sem implementação externa; indisponíveis |
| Freeze Ammo, Silent Aim, bloquear/desbloquear carro | Sem implementação externa; indisponíveis |
| Voz recebida, ADMs/nomes, linha de visão | Sem implementação validada |

Retirar ScriptHook não implementa os recursos que antes existiam somente no módulo. A interface mantém esses controles indisponíveis, inclusive diante de capacidades nativas antigas. Ler um valor após uma escrita não comprova o efeito ou o estado médico do servidor. Não há validação desta versão em Windows/FiveM real.

O print da 3.3.3 passou a mostrar **16.384 bytes inacessíveis** na análise do executável. A causa não foi identificada pelo print: pode haver páginas relevantes sem leitura. A exigência de cobertura completa foi preservada. **Verificar novamente** pode refazer a leitura com No Recoil/No Spread desligados e restauração concluída; não ignora as páginas que falharem.

## Desenvolvimento

SDK .NET 8.0.426 para o aplicativo e testes C#; Lua 5.4, Node.js e g++ para os contratos históricos. Execute `bash scripts/test.sh`. Publique o Windows com `dotnet publish src/ZyonMenu.Windows/ZyonMenu.Windows.csproj -c Release -r win-x64`. Não é necessário compilar `native/zyon-client` ou fornecer Zig para produzir o executável atual.

O runner portável oferece `--check-external-build <assembly>`: inspeciona o PE gerenciado sem executar Windows e rejeita payload ASI, tipos de implementação `ClientModule*` e imports de plugins. O empacotador também rejeita qualquer arquivo `.asi`.

Os diretórios e testes de integração/natives anteriores permanecem no repositório como histórico e regressão, sem serem compilados no aplicativo Windows. Consulte [mudanças](docs/external-3.4.0.md) e [resultados](docs/VALIDACAO.md).
