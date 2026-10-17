# Remoção de ScriptHook — 3.4.0

O usuário pediu a retirada de ScriptHook. A versão 3.4.0 remove o caminho ASI do produto Windows: `Program` cria somente `MainForm` com seu `LocalGameSession` direto; os arquivos `ClientModule*.cs` e o recurso ASI não participam da compilação. A interface não oferece instalação, conexão ou troca para o plugin.

A checagem pós-build lê os metadados .NET e comprova ausência do payload ASI, tipos de implementação do cliente e imports de plugins. O empacotador não exige módulo nativo e rejeita arquivos `.asi`. As fontes históricas continuam disponíveis apenas para regressões; sua presença no ZIP não representa código ativo no executável.

## Desativar a instalação anterior

O menu não consegue descarregar uma DLL já carregada em outro processo. Para impedir que o módulo antigo volte a carregar, feche o FiveM e use **Conexão → Remover integração antiga**. O helper encontra a pasta registrada localmente ou a instalação padrão, oferecendo seleção de pasta quando necessário.

A operação verifica marcadores de `FiveM.app`, recusa links/UNC e arquivos grandes, e aceita somente hashes dos ASIs publicados nas versões 3.2.0–3.3.3. Verifica novamente se o jogo está fechado antes de renomear. A cópia `.disabled` é única e não sobrescreve backups. Um registro JSON local não pode autorizar um arquivo desconhecido. DLLs e demais plugins são preservados.

## Funções e leitura incompleta

Aim, ESP, GodMode, No Recoil/No Spread e a ação de vida zero mantêm a implementação de memória já existente. Os recursos anteriormente exclusivos do módulo, incluindo noclip, revive, invisibilidade, Freeze Ammo, Silent Aim e portas, continuam sem implementação externa e ficam indisponíveis. Não houve substituição por chamadas nativas em outra thread ou alteração da política do FiveM.

O print mais recente confirma cobertura incompleta de 16 KiB, mas não identifica a página, proteção ou erro de leitura. A fonte oficial tem rotas de carga diferentes para tamanhos virtuais e brutos; o total sozinho não prova padding descartável. O scanner não foi alterado para ignorar esses bytes ou autorizar offsets incompletos. Esse bloqueio permanece.

A compilação e os testes portáveis não validam a execução real do aplicativo, a remoção no Windows ou efeitos no jogo. Consulte [Validação](VALIDACAO.md).
