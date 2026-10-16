# Zyon 3.2.0 — módulo somente no cliente

Versão de teste para Windows x64 e FiveM b3258. O executável inclui o módulo nativo `ZyonClient.asi` e oferece sua instalação na pasta local do FiveM. Nenhum recurso é instalado no servidor.

O módulo usa o carregamento oficial de plugins do FiveM. O servidor deve **já permitir ScriptHook/plugins e usar pure level abaixo de 2**. O aplicativo não altera essa política. Windows/FiveM real ainda precisa de validação pelo usuário.

## Abrir e preparar

1. Feche o FiveM e a versão anterior do menu. Extraia o [pacote completo](../downloads/ZyonMenu-v3.2.0-completo.zip) em uma pasta nova.
2. Abra `ZyonMenu.exe`. O modo inicial é **CLIENTE LOCAL**.
3. Na engrenagem/Conexão, clique em **Preparar módulo local** e selecione a pasta `FiveM.app` da sua instalação. Somente `plugins/ZyonClient.asi` e seu registro de instalação são gravados no cliente; uma versão anterior recebe backup `.bak`.
4. Abra o FiveM novamente, entre no servidor e aguarde as capacidades do módulo aparecerem no menu.
5. Teste uma função de cada vez e use **Salvar diagnóstico** para registrar o resultado.

O pacote completo inclui o runtime. O compacto requer .NET Desktop Runtime 8 x64. O modo anterior permanece acessível com `ZyonMenu.exe --external` ou `Abrir-somente-externo.cmd`. Feche o modo atual antes de trocar.

## Funções implementadas para teste

| Função | Caminho nesta versão |
| --- | --- |
| GodMode | Módulo nativo; no modo `--external`, bit de invulnerabilidade já existente |
| Noclip / voar | Módulo nativo por quadro, em personagem a pé e com controle local; velocidade 1–50 m/s |
| Invisibilidade | Módulo nativo com estado anterior preservado |
| Freeze Ammo | Módulo nativo, com leitura da arma e munição |
| Reviver | **Ressurreição nativa**; não limpa morte/hospital de ESX/QBCore |
| Suicídio | Módulo nativo; no modo externo, escrita de vida zero com readback e proteção contra alvo substituído |
| Aim, ESP, No Recoil e No Spread | Backend de memória b3258 existente, condicionado às leituras e assinaturas válidas |

Noclip usa **W/A/S/D**, **Espaço**, **Ctrl** e **Shift**. A velocidade pode mudar durante o voo. Veículo, entidade anexada ou falta de controle suspendem o movimento. A janela não desliga as opções apenas porque uma nova velocidade ainda aguarda confirmação do próximo quadro.

**Silent Aim, silenciar voz recebida, identificar ADMs/nomes e teste de visibilidade continuam pendentes.** Não foi adicionado um segundo projétil como substituto de Silent Aim. O revive nativo pode deixar a interface médica de ESX/QBCore em estado de morte; os fluxos desses frameworks não foram implementados no módulo.

## Validação e diagnóstico

Há testes portáveis de C#, comunicação por named pipes e motor nativo com APIs simuladas, além de compilação cruzada Windows e verificação dos arquivos PE. Isso não comprova o comportamento dentro do jogo. Consulte [validação](docs/VALIDACAO.md) e [módulo local](docs/client-module-3.2.0.md).

O diagnóstico contém os estados separados do módulo e do leitor externo, opções atuais, PID, época do personagem, revisão e observação das ações. As ações não são repetidas após timeout; perda da execução nativa revoga a conexão para impedir reaplicação de efeitos antigos.

Para remover o módulo, feche o FiveM e apague somente `FiveM.app/plugins/ZyonClient.asi` e `ZyonClient-install.json`. Backups `.bak` não são carregados como plugins.

## Desenvolvimento

SDK .NET 8.0.426, Lua 5.4, Node.js, Python 3 e Zig 0.15.2. `bash scripts/test.sh` executa os testes gerenciados e os contratos da integração administrativa anterior. O motor nativo tem testes independentes em `native/zyon-client/tests`.

Compile o módulo antes do aplicativo:

```bash
ZYON_ZIG_COMMAND=/caminho/zig bash native/zyon-client/build.sh
```

Publique o aplicativo com `-p:RequireClientModuleAsset=true` para exigir o módulo embutido. A verificação `--check-embedded-client` do runner portável compara os bytes do recurso com o ASI compilado sem executar código Windows. `python3 release/pack_release.py` exige os binários, o relatório da mesma versão em `.build/validation.json` e as licenças em `ZYON_DOTNET_ROOT`, `NUGET_PACKAGES` e `ZYON_ZIG_ROOT`.

O código administrativo anterior em `fivem/` permanece para compatibilidade e consulta. Não participa da instalação local desta versão.
