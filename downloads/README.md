# Zyon 3.3.1 — conexão e preparação do cliente

[Pacote completo com runtime](ZyonMenu-v3.3.1-completo.zip) · [Pacote compacto](ZyonMenu-v3.3.1-compacto.zip) · [Hashes SHA256](ZyonMenu-v3.3.1-packages.json)

Corrige a perda do erro de inicialização do plugin, que podia virar uma mensagem genérica de permissão. Verifica a pasta da instalação realmente aberta e o hash do ASI instalado. O banner oferece **Usar cliente local**, **Preparar módulo**, **Verificar novamente** e **Salvar diagnóstico**, com o motivo de bloqueio nas abas de personagem e veículo.

Feche o FiveM, extraia o pacote em pasta nova, abra `ZyonMenu.exe` e prepare o módulo novamente antes de abrir o jogo. O modo local continua exigindo plugins/ScriptHook permitidos pelo servidor, sem cargo de ADM ou recurso de servidor. Um ASI no disco não confirma sua execução. As funções e limites da 3.3.0 permanecem, incluindo Silent Aim experimental. [Instruções](../zyon-v3/docs/client-module-3.3.1.md).

## Versões anteriores

# Zyon 3.3.0 — módulo local para teste no Windows

[Pacote completo com runtime](ZyonMenu-v3.3.0-completo.zip) · [Pacote compacto](ZyonMenu-v3.3.0-compacto.zip) · [Hashes SHA256](ZyonMenu-v3.3.0-packages.json)

Feche o FiveM e o menu anterior. Extraia o ZIP em uma pasta nova, abra `ZyonMenu.exe` e use **Conexão → Preparar módulo local**, selecionando sua pasta `FiveM.app`. Faça isso também se já instalou a 3.2.0: aplicativo e ASI desta versão usam o novo protocolo 2. Depois abra o FiveM novamente.

O modo **CLIENTE LOCAL** não pede cargo de administrador no servidor. O módulo depende de o servidor já permitir plugins/ScriptHook e pure level abaixo de 2. Estado **desconectado** pede diagnóstico do carregamento e da conexão, não a permissão ACE `zyon.modmenu` da integração administrativa antiga.

Esta versão preserva GodMode, noclip/voar, invisibilidade, Freeze Ammo, suicídio e revive nativo; acrescenta **Bloquear carro / Desbloquear carro** para o veículo ocupado ou o mais próximo em até 8 m, sujeito ao controle de rede e à confirmação das portas. Não concede chaves de ESX/QBCore.

O **Silent Aim é experimental**: tenta `SET_PED_SHOOTS_AT_COORD`, sem mover a câmera nem criar um projétil manual. Exige soltar o tiro após ativar e faz uma tentativa por novo pressionamento, com jogador válido no FOV. Enquanto ativo, suprime o tiro normal mesmo sem alvo; pode impedir tiros e não garante redirecionamento ou acerto. O diagnóstico pode confirmar consumo de munição, não um hit.

**Windows/FiveM real ainda não foi validado.** Voz recebida, ADMs/nomes, visibilidade do ESP externo e recuperação médica ESX/QBCore continuam pendentes. Consulte [instruções e limites](../zyon-v3/README.md), [protocolo e teste](../zyon-v3/docs/client-module-3.3.0.md) e [validação](../zyon-v3/docs/VALIDACAO.md). Use **Salvar diagnóstico** após testar uma função por vez.

## Histórico

Os pacotes abaixo pertencem a versões anteriores.

# Zyon 3.2.0 — módulo local para teste no Windows

[Pacote completo com runtime](ZyonMenu-v3.2.0-completo.zip) · [Pacote compacto](ZyonMenu-v3.2.0-compacto.zip) · [Hashes SHA256](ZyonMenu-v3.2.0-packages.json)

Com o FiveM fechado, extraia o ZIP, abra `ZyonMenu.exe` e use **Conexão → Preparar módulo local**. Selecione `FiveM.app`; depois abra o FiveM novamente. O programa instala somente no cliente e mantém backup de uma versão anterior.

O módulo depende de o servidor já permitir plugins/ScriptHook e pure<2. Implementa GodMode, noclip/voar, invisibilidade, Freeze Ammo, suicídio e revive nativo para testes. Aim/ESP/recoil/spread continuam pelo leitor externo. **Silent Aim, voz recebida, ADMs/nomes e revive médico ESX/QBCore continuam pendentes.**

Compilação e testes automatizados são distintos de validação no jogo. Consulte [instruções e limites](../zyon-v3/README.md) e [validação](../zyon-v3/docs/VALIDACAO.md). Para o modo externo anterior: `ZyonMenu.exe --external`.

## Histórico

Os pacotes abaixo pertencem a versões anteriores.

# Zyon 3.1.2 — executável externo

[Pacote completo com runtime](ZyonMenu-v3.1.2-completo.zip) · [Pacote compacto](ZyonMenu-v3.1.2-compacto.zip) · [Hashes SHA256](ZyonMenu-v3.1.2-packages.json)

Extraia para uma pasta nova, feche a cópia anterior e abra `ZyonMenu.exe`. Confirme **Zyon 3.1.2** no título. Esta versão não exige instalar nada no servidor.

Corrige a abertura que reativava uma versão antiga e o cálculo de ossos que rejeitava padding inválido. Acrescenta estado e diagnóstico por função. **Silent Aim, reviver, noclip/voar, suicídio, invisibilidade, silenciar, ADMs/nomes e Freeze Ammo externos continuam indisponíveis.**

Para investigar Aim/ESP/armas, reproduza a falha com o FiveM aberto e envie o JSON de **Salvar diagnóstico**. Compilação e testes automatizados passaram; comportamento real no Windows/FiveM não foi executado aqui. Consulte [validação](../zyon-v3/docs/VALIDACAO.md) e [limites](../zyon-v3/README.md).

## Histórico de pacotes

As versões abaixo foram publicadas anteriormente e possuem requisitos diferentes.

# Zyon 3.1.1 — reviver em standalone, ESX e QBCore

[Pacote completo com runtime](ZyonMenu-v3.1.1-completo.zip) · [Pacote compacto](ZyonMenu-v3.1.1-compacto.zip) · [Hashes SHA256](ZyonMenu-v3.1.1-packages.json) · [Integração médica](../zyon-v3/docs/medical-integration.md)

Esta atualização mantém as correções de noclip e ESP e adiciona reviver com seleção automática por servidor. Instale aplicativo e recurso 3.1.1 juntos. O exemplo de `server.cfg` usa `set zyon_menu_medical_provider "auto"`, adequado para standalone, ESX e QBCore em servidores separados. Pode-se escolher cada perfil explicitamente.

ESX atual usa `esx_death`; ESX legado compatível exige callback médico oficial; QBCore usa `qb-ambulancejob` e confirma flags no cliente e servidor. Recursos parados, APIs ausentes e prazos expirados bloqueiam a ação, sem fallback nativo. ESX intermediário sem essa API e hospitais personalizados precisam de adaptação.

Compilação completa e compacta concluídas; suíte geral e 25 novos testes médicos passaram. Os testes de natives e frameworks usam simulações. Windows/FiveM real ainda precisa de validação. Silent Aim permanece indisponível.

## Pacotes anteriores

# Zyon v3 — prévia independente para FiveM b3258

[Baixar o pacote completo](ZyonMenu-v3-standalone-completo.zip) · [Pacote compacto](ZyonMenu-v3-standalone-compacto.zip)

O pacote completo inclui o executável Windows x64, runtime e código-fonte. Extraia o ZIP para uma pasta nova, feche a versão antiga e abra `ZyonMenu.exe`. Confirme o título **Zyon v3 — FiveM b3258 / standalone**.

Esta prévia usa detecção direta de `FiveM_b3258_GTAProcess.exe`, sem recurso de servidor, grupo admin, chave manual ou porta TCP local. Aim, ESP e GodMode podem ser preparados antes da validação do personagem. A aba da engrenagem inclui **Salvar diagnóstico** para investigar o cliente efetivamente carregado.

**Reviver, nomes/IDs da cidade, identidade Steam, Silent Aim, Noclip, Freeze Ammo e oclusão permanecem indisponíveis.** O código compilou e passou sete suítes portáveis com 101 assertivas, cinco verificações de mutex entre processos e três comparações de bytes com os accessors recebidos. A janela Windows, o acesso ao processo e os efeitos em uma sessão real de FiveM não foram executados neste ambiente Linux.

O pacote compacto exige **.NET Desktop Runtime 8 x64**. Consulte `LEIA-ME.txt` e `codigo-fonte/README.md` dentro dos pacotes para a implementação atual e seus limites. Nenhum dos DLLs ou executáveis privados enviados para análise está incluído.

## Pacotes anteriores

Os pacotes abaixo são da versão administrativa 2 e exigem um recurso instalado no servidor.

# ZyonMenu v2 — arquivos para download

Aplicativo externo Windows 10 x64 com pareamento automático. A janela não pede chave de acesso nem URL.

- `ZyonMenu-completo.zip`: executável com runtime embutido, recurso FiveM e fontes.
- `ZyonMenu-compacto.zip`: executável menor, recurso e fontes; requer .NET Desktop Runtime 8 x64.

Abrir apenas o executável não ativa funções. Instale a pasta `fivem/zyon_admin_menu` no servidor e siga o README do pacote para dar a permissão ACE `zyon.modmenu` ao administrador. Use aplicativo e recurso v2 juntos. Servidor remoto requer a URL HTTPS do recurso configurada no servidor; o programa a recebe automaticamente.

Compilação e testes automatizados passaram. A janela desta versão não foi executada no Windows e o comportamento real no FiveM não foi validado neste ambiente Linux. Silent Aim permanece desativado. Os pacotes não incluem executáveis enviados para análise nem credenciais reais.

## SHA256

`downloads/ZyonMenu-completo.zip`: `727a235cd080f472fc101459dba59385dad824215b3442d4f1fa7a4f86a432cb`

`downloads/ZyonMenu-compacto.zip`: `84da53336d618298dcb5c33cf46bde6146838b964d6f36e4f115ed8c24dee4e0`
