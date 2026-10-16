# Zyon 3.3.0 — módulo somente no cliente

Versão de teste para Windows x64 e FiveM b3258. O executável inclui o módulo nativo `ZyonClient.asi` e oferece sua instalação na pasta local do FiveM. Nenhum recurso é instalado no servidor, e o modo **CLIENTE LOCAL** não verifica cargo de administrador ou a permissão ACE `zyon.modmenu`.

O módulo usa o carregamento oficial de plugins do FiveM. O servidor deve **já permitir ScriptHook/plugins e usar pure level abaixo de 2**. O aplicativo não altera essa política. O funcionamento real no Windows/FiveM ainda não foi validado.

## Abrir e atualizar

1. Feche o FiveM e a versão anterior do menu. Extraia o [pacote completo](../downloads/ZyonMenu-v3.3.0-completo.zip) em uma pasta nova.
2. Abra `ZyonMenu.exe` ou `Abrir-modulo-local.cmd`. Confira **3.3.0** e **CLIENTE LOCAL** no título.
3. Na engrenagem/Conexão, clique em **Preparar módulo local** e selecione sua pasta `FiveM.app`. Repita esta etapa mesmo se já instalou a 3.2.0: o novo protocolo exige aplicativo e ASI atualizados juntos.
4. Abra o FiveM novamente, entre no servidor e aguarde o módulo conectado e suas capacidades aparecerem no menu.
5. Teste uma função de cada vez e use **Salvar diagnóstico** para registrar o resultado.

Somente `plugins/ZyonClient.asi` e seu registro de instalação são gravados no cliente; uma versão anterior recebe backup `.bak`. O pacote completo inclui o runtime. O compacto requer .NET Desktop Runtime 8 x64. O modo externo permanece acessível com `ZyonMenu.exe --external` ou `Abrir-somente-externo.cmd`; feche o modo atual antes de trocar.

Se o módulo permanecer **desconectado**, confira primeiro o modo, a instalação da versão atual e o carregamento do plugin. Exporte o diagnóstico com o FiveM aberto. Esse estado não prova falta de cargo administrativo no servidor; a integração administrativa antiga usa outro modo e outra autorização.

## Funções implementadas para teste

| Função | Caminho e limite nesta versão |
| --- | --- |
| GodMode | Módulo nativo; no modo `--external`, bit de invulnerabilidade já existente |
| Noclip / voar | Módulo nativo por quadro, personagem a pé e com controle local; velocidade 1–50 m/s |
| Invisibilidade | Módulo nativo com estado anterior preservado |
| Freeze Ammo | Módulo nativo, com leitura da arma e munição |
| Reviver | Ressurreição nativa; não limpa morte/hospital de ESX/QBCore |
| Suicídio | Módulo nativo; no modo externo, escrita de vida zero com readback e proteção contra alvo substituído |
| Bloquear / desbloquear carro | Veículo ocupado ou o mais próximo em até 8 m; exige controle de rede e confirmação das portas; não concede chaves de frameworks |
| Silent Aim experimental | Protótipo com `SET_PED_SHOOTS_AT_COORD`; sem confirmação de redirecionamento ou acerto; somente no módulo local |
| Aim, ESP, No Recoil e No Spread | Backend de memória b3258 existente, condicionado às leituras e assinaturas válidas |

Noclip usa **W/A/S/D**, **Espaço**, **Ctrl** e **Shift**. A velocidade pode mudar durante o voo. Veículo, entidade anexada ou falta de controle suspendem o movimento. A janela não desliga as opções apenas porque uma nova velocidade ainda aguarda confirmação do próximo quadro.

Na aba **Veículo**, confirme a ação e mantenha-se no mesmo carro ou próximo dele. O módulo solicita controle pela API de rede por até dois segundos, escreve o estado das portas uma vez e confirma a leitura correspondente. Perda do alvo ou de controle cancela a ação. Scripts de chaves e garagem podem alterar novamente esse estado.

## Silent Aim experimental

O protótipo consulta jogadores nos índices 0–255 e seleciona um personagem válido conforme o **FOV em pixels**, osso, tecla de ativação e linha de visão nativa configurados. A opção é fixa em somente jogadores. Não há suavização ou círculo FOV.

Ative apenas durante um teste controlado. **Solte o botão de tiro depois de ativar**; em seguida, pressione o tiro junto da tecla de ativação. Há uma tentativa por pressionamento, e é necessário soltar para armar outra. Exige jogo em foco, personagem vivo e a pé, arma e munição válidas e ausência de recarga.

Enquanto a opção está ativa, a entrada normal de tiro é suprimida **inclusive quando não há alvo elegível**. O protótipo pode, portanto, impedir disparos. Ele chama `SET_PED_SHOOTS_AT_COORD` sem mover a câmera nem criar um projétil manual; essa chamada não comprova que o tiro original foi redirecionado. Consumo de munição observável não confirma acerto ou dano. O comportamento no FiveM precisa de validação e pode não produzir efeito.

**Silenciar voz recebida, identificar ADMs/nomes e visibilidade do ESP externo continuam pendentes.** O revive nativo pode deixar a interface médica de ESX/QBCore em estado de morte; os fluxos desses frameworks não foram implementados no módulo.

## Validação e diagnóstico

Há testes portáveis de C#, comunicação por named pipes e motor nativo com APIs simuladas, além de ferramentas para compilação cruzada Windows e verificação de arquivos PE. Esses testes não comprovam comportamento dentro do jogo. Consulte [validação](docs/VALIDACAO.md) e [módulo local 3.3.0](docs/client-module-3.3.0.md).

O diagnóstico separa os estados do módulo e do leitor externo e registra opções, PID, época do personagem, revisões e observações das ações. Ações não são repetidas após timeout; perda da execução nativa revoga a conexão para impedir reaplicação de efeitos antigos. Para investigar uma falha, salve o JSON com o FiveM aberto e informe a função e o comportamento observado.

Para remover o módulo, feche o FiveM e apague somente `FiveM.app/plugins/ZyonClient.asi` e `ZyonClient-install.json`. Backups `.bak` não são carregados como plugins.

## Desenvolvimento

SDK .NET 8.0.426, Lua 5.4, Node.js, Python 3 e Zig 0.15.2. `bash scripts/test.sh` executa os testes gerenciados e os contratos da integração administrativa anterior. O motor nativo tem testes independentes em `native/zyon-client/tests`.

Compile o módulo antes do aplicativo:

```bash
ZYON_ZIG_COMMAND=/caminho/zig bash native/zyon-client/build.sh
```

Publique o aplicativo com `-p:RequireClientModuleAsset=true` para exigir o módulo embutido. A verificação `--check-embedded-client` do runner portável compara os bytes do recurso com o ASI compilado sem executar código Windows. `python3 release/pack_release.py` exige os binários, o relatório da mesma versão em `.build/validation.json` e as licenças em `ZYON_DOTNET_ROOT`, `NUGET_PACKAGES` e `ZYON_ZIG_ROOT`.

O código administrativo anterior em `fivem/` permanece para compatibilidade e consulta. Não participa da instalação local desta versão.
