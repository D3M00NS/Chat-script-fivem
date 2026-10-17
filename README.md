# Chat-script-fivem

[Zyon 3.4.0 — baixar pacote completo](downloads/ZyonMenu-v3.4.0-completo.zip) · [Instruções](zyon-v3/README.md) · [Validação](zyon-v3/docs/VALIDACAO.md)

Aplicativo externo para Windows x64/FiveM b3258. A 3.4.0 remove a dependência de ScriptHook e do cliente ASI: inicia diretamente com o leitor de memória, sem instalação ou conexão a plugins. O executável não contém o ASI, o instalador nem o provider nativo das versões anteriores.

Mantém as implementações externas de Aim, ESP, GodMode, No Recoil/No Spread e escrita de vida zero após confirmação. Disponibilidade depende da leitura e validação do processo; cobertura incompleta continua bloqueando campos de arma. **Noclip, revive, invisibilidade, Freeze Ammo, Silent Aim e portas não têm implementação externa nesta versão.**

Para desativar o módulo Zyon instalado anteriormente, feche o FiveM e use **Conexão → Remover integração antiga**. O aplicativo renomeia somente o `ZyonClient.asi` de releases conhecidas para uma cópia `.disabled`. Arquivos do FiveM e outros plugins são preservados.
