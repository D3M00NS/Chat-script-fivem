# Zyon 3.1.2 — executável externo para FiveM b3258

Esta versão abre diretamente no modo externo, sem instalar recurso no servidor. O perfil é destinado a `FiveM_b3258_GTAProcess.exe` no Windows x64.

Extraia o [pacote completo](../downloads/ZyonMenu-v3.1.2-completo.zip) em uma pasta nova, feche o menu anterior e abra `ZyonMenu.exe`. Confirme **Zyon 3.1.2** no título. O runtime está incluído; o pacote compacto exige .NET Desktop Runtime 8 x64.

## Correções desta versão

- Uma cópia nova não reativa silenciosamente uma versão antiga. O aviso identifica as versões e pede que a janela anterior seja fechada; o mutex continua impedindo dois controladores simultâneos.
- O cálculo dos ossos usa os 12 coeficientes necessários da matriz do personagem. Valores inválidos nos quatro campos de padding não descartam mais todos os personagens. A validação da matriz de projeção continua rigorosa.
- **Situação das funções** mostra as pré-condições e falhas de cada função. **Salvar diagnóstico** registra cadeias de leitura, câmera, armas, projeção e resultados das escritas. Configuração recebida e leitura confirmada não são anunciadas como efeito comprovado no jogo.
- A janela se ajusta à área de trabalho e permite rolagem nas abas; a mudança ainda precisa ser conferida no Windows com escala de tela diferente.

## O que permanece pendente

O backend externo contém Aim, ESP, GodMode e modificadores de recoil/spread condicionados às assinaturas encontradas. A correção de padding pode recuperar personagens antes descartados, mas não comprova que todas as leituras do seu FiveM estão corretas.

**Silent Aim, reviver, suicídio, noclip/voar, invisibilidade, silenciar, ADMs/nomes no ESP, Freeze Ammo e teste de visibilidade não têm implementação externa funcional neste projeto.** Seus controles permanecem indisponíveis. Esta atualização não afirma resolver essas funções.

Para investigar Aim/ESP/armas, reproduza a falha com o FiveM aberto, volte ao menu e clique em **Salvar diagnóstico**. Envie o JSON resultante. Ele permite localizar a falha no perfil efetivamente carregado, sem trocar endereços por tentativa. Veja [o diagnóstico externo](docs/external-3.1.2.md).

Compilação e testes portáveis foram executados no Linux. A janela WinForms, acesso Win32 ao jogo e efeitos no FiveM real não foram executados aqui. Consulte a [validação](docs/VALIDACAO.md).

## Desenvolvimento

Use SDK .NET 8.0.426, Lua 5.4 e Node.js. No Windows:

```powershell
powershell -File scripts/build.ps1
powershell -File scripts/test.ps1
```

No Linux, `bash scripts/test.sh`. Os executáveis podem ser indicados por `ZYON_DOTNET_COMMAND`, `ZYON_LUA_COMMAND` e `ZYON_NODE_COMMAND`. Os testes cobrem geometria, política da interface, mutex entre processos, diagnósticos, HTTP e contratos da integração administrativa anterior.

`python release/pack_release.py` empacota as publicações de `dist/win-x64` e `dist/compacto`. Use `ZYON_DOTNET_ROOT` e `NUGET_PACKAGES` para localizar as licenças do runtime. Os pacotes incluem fontes e licenças; não incluem DLLs privadas recebidas para análise.

O código da integração administrativa 3.1.1 foi preservado em `fivem/` para compatibilidade e consulta. Ele não participa do funcionamento externo nem é exigido para abrir esta versão.
