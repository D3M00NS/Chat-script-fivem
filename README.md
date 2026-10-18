# HWID Lab — simulador para FiveM

Recurso Lua para testar criação de perfis, bloqueio, consulta de acesso e
desbloqueio usando IDs fictícios como `SIM-HWID-000001`. Cada perfil representa
um participante imaginário no laboratório.

O registro é independente dos jogadores e dos banimentos do servidor. Nenhum
HWID, identificador de jogador ou arquivo do Windows é lido ou modificado.
O recurso não interage com anticheat.

## Instalar no FiveM

1. Copie este repositório para `resources/[local]/hwid_lab` no seu servidor.
2. Adicione `ensure hwid_lab` ao `server.cfg` e inicie o servidor. Para um
   servidor já iniciado, execute `refresh` e `ensure hwid_lab` no console.
3. Execute os comandos abaixo no console do servidor. Para executar dentro do
   jogo, use `/hwidlab ...`; a resposta usa o recurso padrão `chat`, e o jogador
   precisa da permissão ACE `command.hwidlab`.

Para um grupo administrativo **já configurado** no servidor, a permissão é:

```cfg
add_ace group.admin command.hwidlab allow
```

O console funciona sem `chat` e sem configurar permissões de jogadores.
Não há dependências de framework, banco de dados ou pacotes externos.

## Cenário de teste

Em uma sessão nova do recurso, execute:

```text
hwidlab create Jogador de teste
hwidlab check SIM-HWID-000001
hwidlab ban SIM-HWID-000001 Bloqueio de teste
hwidlab check SIM-HWID-000001
hwidlab unban SIM-HWID-000001
hwidlab check SIM-HWID-000001
```

As três consultas devem retornar `PERMITIDO`, `NEGADO` e `PERMITIDO`, nessa
ordem. Em outras sessões, use o ID retornado por `create`.

Outros comandos:

| Comando | Resultado |
| --- | --- |
| `hwidlab help` | Exibe os comandos. |
| `hwidlab list` | Lista os perfis de teste. |
| `hwidlab show <id>` | Exibe o perfil e seu estado. |
| `hwidlab reset CONFIRMAR` | Apaga todos os perfis de teste. |

Nomes aceitam de 1 a 64 bytes; motivos aceitam de 1 a 200 bytes.
Caracteres de controle e identificadores fora do formato fictício são rejeitados.
Todos os dados ficam em memória e desaparecem ao reiniciar o recurso.
`reset` preserva a sequência de IDs durante a sessão para evitar reutilizar
um ID antigo. Ao reiniciar o recurso, a sequência começa novamente em `000001`.

## Executar os testes

Com Lua 5.4 instalado, a partir da raiz do repositório:

```sh
lua5.4 tests/run.lua
lua5.4 tests/fivem_smoke.lua
```

Os testes verificam o ciclo de ban/unban, validação de entradas, isolamento dos
perfis e os comandos com APIs FiveM simuladas. Não precisam de GTA V nem de um
servidor. A validação dentro do jogo exige um servidor FiveM e um cliente.
