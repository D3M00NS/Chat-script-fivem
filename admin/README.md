# Unban administrativo local do txAdmin

Aplicativo Windows de 64 bits para revogar **um ban local do txAdmin** usando
o arquivo de dados do servidor que você administra. Não requer instalar Go,
Python ou dependências no computador onde será executado.

[Baixar txAdmin-Unban.exe](https://github.com/D3M00NS/Chat-script-fivem/raw/refs/heads/hwid-lab-simulator/downloads/txAdmin-Unban.exe)
· [SHA-256](../downloads/txAdmin-Unban.sha256)

1. Encerre o txAdmin/FXServer responsável pelo banco de dados. O txAdmin mantém
   os registros em memória e pode sobrescrever alterações externas enquanto
   está aberto.
2. Abra o executável e selecione
   `txData/<perfil>/data/playersDB.json` usando **Procurar**.
3. Informe o Ban ID exibido na mensagem de banimento, confira o jogador e o
   motivo na confirmação e clique em **Desbanir**.

Depois, inicie novamente o txAdmin/FXServer. A aplicação preserva o registro e
o histórico, preenchendo somente a revogação do ban escolhido. Um backup com
os bytes originais é salvo ao lado do banco antes da alteração. O autor da
revogação fica registrado como `local-admin-tool`.

O aplicativo requer acesso de leitura e escrita ao arquivo do seu servidor.
Ele não se conecta a servidores pela internet, não recebe credenciais, não
modifica identificadores e não procura bancos em outros computadores.

Suporta o formato de banco **versão 5 do txAdmin**, em arquivos de até 128 MiB.
Não remove bans aplicados
separadamente por FiveGuard, PL PROTECT, EQPG, Likizão ou pela plataforma Cfx.re.
Se houver outros bans ativos para o mesmo jogador, eles continuam ativos.

O aplicativo recusa alterações se encontrar um FXServer ativo na máquina,
formato incompatível, Ban ID ausente ou ambíguo, arquivo redirecionado por link,
ou mudança no banco entre a confirmação e a gravação. Para arquivos de outra
máquina, confirme também que o txAdmin correspondente está encerrado.

Se o aplicativo encerrar abruptamente, pode restar um arquivo
`playersDB.json.local-unban.lock`. Remova esse arquivo somente depois de
confirmar que todas as instâncias da ferramenta estão fechadas.

## Desenvolvimento

O comportamento de revogação foi conferido no código oficial do txAdmin:

- [Tipos do banco](https://github.com/tabarra/txAdmin/blob/8a9a41410000fd92a527fe11bae2b8eeeb8b10e0/core/modules/Database/databaseTypes.ts)
- [Revogação das ações](https://github.com/tabarra/txAdmin/blob/8a9a41410000fd92a527fe11bae2b8eeeb8b10e0/core/modules/Database/dao/actions.ts)
- [Versão e persistência](https://github.com/tabarra/txAdmin/blob/8a9a41410000fd92a527fe11bae2b8eeeb8b10e0/core/modules/Database/instance.ts)

Com Go 1.23+ e Python 3, a partir da raiz do repositório:

```sh
cd admin
go test ./...
cd ..
python3 scripts/build_txadmin_admin.py
```

O executável e o checksum são gerados em `dist/`. Use `--go /caminho/para/go`
para escolher o compilador. O build usa apenas a biblioteca padrão do Go.

Os testes usam bancos fictícios em diretórios temporários. A interface nativa
precisa ser validada no Windows; o binário não possui assinatura digital.
