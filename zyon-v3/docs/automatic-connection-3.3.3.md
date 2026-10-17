# Conexão automática 3.3.3

A 3.3.2 não resolveu o problema no jogo. O novo print confirma que o ASI carregou e respondeu, mas seu callback nunca entrou. Também mostra a rejeição do cabeçalho PE pelo scanner. Esses são problemas separados: corrigir a leitura do executável não faz o scheduler passar a executar plugins.

## Alterações

O menu abre diretamente com detecção automática, sem escolher um modo. O título é Zyon/FiveM. O cabeçalho mostra conexão parcial quando há funções de memória disponíveis e a integração nativa não executa. A opção de configurar a integração sai do destaque quando o arquivo já está atualizado ou o módulo já comunicou; permanece na aba Conexão para manutenção.

GodMode e suicídio já tinham implementação de memória, mas o provider combinado removia suas capacidades e exigia execução nativa. O caminho automático preserva essas operações quando seu processo/personagem é validado. GodMode mantém um único proprietário por processo e não muda de caminho apenas porque outro provedor ficou disponível. Ações de saúde usam a identidade e o epoch de seu provedor, com cancelamento e sem repetição automática. Capacidades de memória não habilitam noclip, revive, invisibilidade, munição, portas ou Silent Aim.

A igualdade obrigatória entre `ProcessModule.ModuleMemorySize` e `OptionalHeader.SizeOfImage` foi removida. O [loader oficial do FiveM](https://github.com/citizenfx/fivem/blob/master/code/client/launcher/ExecutableLoader.cpp) copia o cabeçalho NT e as seções do GTA para o módulo hospedeiro, mantendo o DOS do launcher. Tamanhos e posição da tabela podem diferir legitimamente. O scanner mantém limites independentes de Windows e PE, protege a tabela real e exige que cada seção caiba inteira nos dois intervalos. Não expande nem trunca seções para fazer uma assinatura passar.

O diagnóstico registra os dois tamanhos, o magic, a máquina, o endereço PE do DOS, os tamanhos de cabeçalho e os limites da tabela. Falhas antes agrupadas na mesma mensagem agora identificam o campo e seus valores. Assinaturas ausentes, ambíguas, operandos inválidos e cobertura incompleta continuam bloqueados.

## Atualização

Feche o aplicativo antigo, extraia o ZIP e abra `ZyonMenu.exe`. O ASI é idêntico ao da 3.3.2, com protocolo v2. Se o módulo já está carregado, não é preciso preparar a integração outra vez para esta atualização. O FiveM pode continuar aberto durante a troca apenas do aplicativo.

## Limite confirmado pelo print

A comunicação com o módulo não comprova execução de natives. O [scheduler oficial](https://github.com/citizenfx/fivem/blob/master/code/components/scripthookv/src/VishCompat.cpp) não entra no callback sem ScriptHook permitido e rede inicializada. O print não confirma qual condição impede sua sessão. Esta atualização não contorna a política do servidor nem comprova que essas funções passaram a executar.

Os [resultados dos testes](VALIDACAO.md) distinguem simulações de validação Windows/FiveM. Silent Aim continua experimental, revive não limpa estado médico ESX/QBCore e portas não concedem chaves de frameworks. Voz recebida, ADMs/nomes e oclusão ESP permanecem sem implementação.
