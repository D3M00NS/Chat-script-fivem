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
