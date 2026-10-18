# HWID Lab — simulador para FiveM

Recurso Lua para testar criação de perfis, bloqueio, consulta de acesso e
desbloqueio usando IDs fictícios como `SIM-HWID-000001`. Cada perfil representa
um participante imaginário no laboratório.

O registro é independente dos jogadores e dos banimentos do servidor. Nenhum
HWID, identificador de jogador ou arquivo do Windows é lido ou modificado.
O recurso não interage com anticheat.

## Unban administrativo do txAdmin

[Baixar txAdmin-Unban.exe para Windows 64 bits](https://github.com/D3M00NS/Chat-script-fivem/raw/refs/heads/hwid-lab-simulator/downloads/txAdmin-Unban.exe)

Esta ferramenta separada revoga um ban local do txAdmin com acesso ao arquivo
do servidor: encerre o txAdmin/FXServer, escolha `playersDB.json`, informe o
Ban ID e confirme. Ela preserva o histórico e cria backup antes de gravar.
Suporta apenas o formato de banco versão 5 do txAdmin.

[Instruções e limitações](admin/README.md).

## Instalador para Windows

[Baixar instalador Windows 64 bits](https://github.com/D3M00NS/Chat-script-fivem/raw/refs/heads/hwid-lab-simulator/downloads/HWID-Lab-Installer.exe)
· [SHA-256](downloads/SHA256SUMS.txt)

O executável `HWID-Lab-Installer.exe` instala **este simulador** no seu servidor
FiveM. Ele inclui os arquivos do recurso e funciona sem downloads adicionais.

1. Abra o instalador no Windows 10/11 de 64 bits.
2. Selecione o `server.cfg` e a pasta `resources` do mesmo servidor.
3. Confira os caminhos e confirme a instalação.

O recurso fica em `resources/[local]/hwid_lab`. Quando necessário, o instalador
faz backup do `server.cfg` ao lado do original e adiciona `ensure hwid_lab`.
Uma instalação existente com arquivos modificados é preservada e a operação
é interrompida. Executar novamente com os mesmos arquivos não duplica a linha.

Se o servidor já estiver aberto, execute `refresh` e `ensure hwid_lab` no
console. Depois, use `hwidlab help`. O instalador não inicia o servidor FiveM.

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

## Compilar o instalador

Com Python 3 e Go 1.23 ou superior, execute na raiz do repositório:

```sh
cd installer
go test ./...
cd ..
python3 scripts/build_installer.py
```

O resultado fica em `dist/HWID-Lab-Installer.exe`; o hash SHA-256 fica em
`dist/SHA256SUMS.txt`. O build usa apenas a biblioteca padrão do Go e incorpora
os arquivos atuais do simulador. Para escolher o compilador, use
`python3 scripts/build_installer.py --go /caminho/para/go`.

Os testes do instalador verificam extração, backup, reinstalação e preservação
da configuração em diretórios temporários. A interface gráfica usa as APIs
nativas do Windows e precisa ser validada no Windows. O binário não possui
assinatura digital.
