# Conexão do cliente 3.3.1

A tela agora apresenta o modo e o bloqueio atual em todas as abas. **Usar cliente local** encerra o provider externo e abre a interface do módulo no mesmo processo, preservando a exclusão de instâncias. As opções antigas não são reaplicadas nessa troca. **Preparar módulo** e **Verificar novamente** permanecem disponíveis quando as funções estão bloqueadas.

O diagnóstico procura primeiro a pasta `FiveM.app` do processo GTA do FiveM em execução. Depois de fechar o jogo, mantém essa pasta em memória; uma instalação concluída registra a escolha nas preferências locais. A inspeção compara o SHA256 do arquivo instalado com a imagem embutida no executável e limita o tamanho dos arquivos e registros lidos. Arquivo correspondente significa apenas **ReadyOnDisk**, sem comprovar carregamento, autorização ou execução nativa.

O instalador exige FiveM fechado, consulta launchers e outras builds e rejeita uma pasta vazia apenas chamada `FiveM.app`. Ele preserva backup do ASI anterior e não modifica recursos ou permissões do servidor. A escolha permanece local.

A inicialização do ASI mantém seu erro separado dos epochs de conexão. Uma DLL ou export ausente não é mais descartado após Hello nem substituído por um aviso genérico de permissão. A ausência de quadro nativo continua sem comprovar qual política impediu a execução. Nenhuma nova tentativa de registro em outra thread, invocação de handler diretamente ou alteração de autorização foi adicionada.

O estado do módulo (`NativeClient`) é separado do leitor de memória. Os botões nativos exigem quadro recente e capacidades reais; um leitor de memória pronto não libera portas, noclip ou revive. O diagnóstico JSON também inclui fatos da instalação e o erro nativo.

Para atualizar, feche o FiveM e o menu antigo, extraia a versão nova, abra `ZyonMenu.exe` e execute **Preparar módulo** novamente. Abra o FiveM depois. O protocolo continua 2, mas o novo ASI é necessário para preservar os erros de inicialização.

As funções e os limites documentados na [3.3.0](client-module-3.3.0.md) continuam: build 3258, servidor permitindo plugins/ScriptHook e pure abaixo de 2; Silent Aim é somente um protótipo experimental. Testes automatizados e compilação não confirmam o comportamento real no Windows/FiveM. Os resultados desta versão estão em [Validação](VALIDACAO.md).
