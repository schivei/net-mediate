# Avaliação arquitetural e técnica — evolução com Full NativeAOT + Trimming

## 1) Objetivo e proposta principal

O **NetMediate** é uma implementação de mediator para mensageria in-process em .NET (commands, notifications, requests e streams), com foco em baixo overhead, integração com DI e caminho orientado a **NativeAOT + Trimming** via geração de código em tempo de compilação.

Referências:
- `/home/runner/work/net-mediate/net-mediate/README.md:26-27`
- `/home/runner/work/net-mediate/net-mediate/README.md:61-73`
- `/home/runner/work/net-mediate/net-mediate/docs/AOT.md:3-19`

---

## 2) Estrutura do repositório e componentes principais

### Núcleo
- `src/NetMediate.Core`: contratos públicos (`IMediator`, handlers, delegates e contratos legados).
- `src/NetMediate`: runtime principal (dispatch e exceções estruturadas).

Referências:
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate.Core/NetMediate.Core.csproj:4-7`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate/Internals/Mediator.cs:8-15`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate/Internals/Notifier.cs:7-16`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate/MediatorException.cs:29-56`

### Source generation e registro
- `src/NetMediate.SourceGeneration`: incremental generator.
- Gera `AddNetMediate()`, typed extensions e classes concretas de decorators/framework behaviors.
- `buildTransitive` injeta `NetMediate` e `GenDI.SourceGenerator` no consumidor.

Referências:
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate.SourceGeneration/NetMediateRegistrationGenerator.cs:23-43`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate.SourceGeneration/NetMediateGeneratedDI.template:13-21`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate.SourceGeneration/buildTransitive/NetMediate.SourceGeneration.props:2-7`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate.SourceGeneration/buildTransitive/NetMediate.SourceGeneration.targets:2-13`

### Pacotes opcionais
- `NetMediate.Diagnostics`: telemetria OpenTelemetry por decorators.
- `NetMediate.Resilience`: retry/timeout/circuit-breaker por decorators.
- `NetMediate.Quartz`: persistência de notifications em Quartz (**não AOT-safe**).
- `NetMediate.Moq`: utilitários de teste (**não AOT-safe**).

Referências:
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate.Diagnostics/TelemetryBehaviors.cs:14-18`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate.Resilience/RetryBehaviors.cs:185-191`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate.Quartz/NetMediate.Quartz.csproj:16-22`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate.Moq/NetMediate.Moq.csproj:8-14`

---

## 3) Como NativeAOT e Trimming são preservados hoje

1. **Caminho principal é source-generated + closed types**  
   O uso recomendado é `NetMediate.SourceGeneration` + `AddNetMediate()`; sem scanning/reflection no caminho principal.

2. **Generator incremental com filtros de segurança**  
   Descobre tipos concretos, não abstratos, não genéricos e acessíveis.

3. **Modelo atual é GenDI-first**  
   O template atual de `AddNetMediate()` encadeia `AddGenDIServices()` do app + NetMediate; não há mais foco em `Register*Handler<>` explícito no template.

4. **Cross-cutting por decorators concretos**  
   Contratos legados `IPipeline*Behavior` estão obsoletos com erro de compilação, incentivando `DecoratorFor`.

5. **Dispatch runtime sem runtime codegen**  
   `Mediator`/`Notifier` usam DI tipado e cache por tipo para caminhos sem key; keyed resolve via keyed DI.

6. **Typed extensions geradas**  
   Métodos tipados chamam overloads concretos de `IMediator`, reforçando execução estática.

Referências:
- `/home/runner/work/net-mediate/net-mediate/docs/AOT.md:7-19`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate.SourceGeneration/NetMediateRegistrationGenerator.cs:135-150`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate.SourceGeneration/NetMediateGeneratedDI.template:13-21`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate.Core/IPipelineBehavior.cs:12-16`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate/Internals/Mediator.cs:17-33`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate/Internals/Mediator.cs:78-83`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate/Internals/Mediator.cs:140-145`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate/Internals/Mediator.cs:181-195`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate/Internals/Notifier.cs:50-59`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate.SourceGeneration/NetMediateTypedExtensions.template:10-12`

---

## 4) Riscos atuais para AOT/trim safety

1. **Desalinhamento parcial entre documentação e implementação atual do generator**  
   Há trechos e testes legados ainda em transição para o modelo atual.

2. **Pacotes core não explicitam todas as flags MSBuild de AOT/trim**  
   O contrato existe na arquitetura, mas pode ser fortalecido por sinalização formal.

3. **CI ainda não gateia publish NativeAOT/trimmed do caminho principal**  
   Build/test/coverage já existem, mas falta smoke publish/exec como gate.

4. **Open generics e registros manuais podem quebrar o preceito se usados fora do padrão**  
   Ponto de atenção para futuras contribuições.

5. **Ecossistema misto (pacotes AOT-safe e não AOT-safe)**  
   Quartz/Moq são explicitamente não compatíveis.

Referências:
- `/home/runner/work/net-mediate/net-mediate/ROADMAP.md:9-12`
- `/home/runner/work/net-mediate/net-mediate/tests/NetMediate.SourceGeneration.Tests/GeneratorIntegrationTests.cs:310-337`
- `/home/runner/work/net-mediate/net-mediate/tests/NetMediate.SourceGeneration.Tests/GeneratorIntegrationTests.cs:779-789`
- `/home/runner/work/net-mediate/net-mediate/tests/NetMediate.SourceGeneration.Tests/GeneratorIntegrationTests.cs:889-926`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate/NetMediate.csproj:3-18`
- `/home/runner/work/net-mediate/net-mediate/.github/workflows/ci-cd.yml:177-211`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate.Quartz/NetMediate.Quartz.csproj:16-22`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate.Moq/NetMediate.Moq.csproj:8-14`

---

## 5) Oportunidades concretas (impacto x risco)

### Alto impacto / baixo risco
- Formalizar flags AOT/trim em `NetMediate` e `NetMediate.Core`.
- Adicionar gate de publish+exec NativeAOT/trimmed em CI.
- Alinhar docs/testes ao estado real do modelo GenDI-first.

### Médio impacto / baixo-médio risco
- Melhorar cache para dispatch keyed sem reflection/dynamic IL.
- Evoluir decorators de stream para reduzir bufferização completa e preservar streaming incremental.
- Criar analyzer/checks para barrar padrões incompatíveis com AOT/trim no caminho core.

Referências:
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate/Internals/Mediator.cs:13-34`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate.Diagnostics/TelemetryBehaviors.cs:118-146`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate.Resilience/RetryBehaviors.cs:140-166`
- `/home/runner/work/net-mediate/net-mediate/src/NetMediate.Resilience/TimeoutBehaviors.cs:123-140`

---

## 6) Roteiro planejado por etapas (curtas)

## Fase 1 — Consolidação do contrato atual (curto prazo)

1. Publicar matriz de compatibilidade AOT/trim por pacote (`core`, `diagnostics`, `resilience`, `quartz`, `moq`).
2. Atualizar `docs/AOT.md`, `docs/SOURCE_GENERATION.md` e `ROADMAP.md` para o modelo GenDI-first.
3. Revisar linguagem da documentação para separar claramente “caminho principal AOT-safe” de “pacotes opcionais não AOT-safe”.
4. Fechar pendências de testes de source generation hoje marcadas como `Skip` por expectativas legadas.

## Fase 2 — Governança técnica em CI (curto-médio prazo)

1. Adicionar job de smoke app com `dotnet publish -p:PublishAot=true` + execução básica.
2. Adicionar job de smoke app com `dotnet publish -p:PublishTrimmed=true` + execução básica.
3. Tornar falha de smoke AOT/trim bloqueante para merge em áreas core.
4. Documentar no `CONTRIBUTING.md` o checklist obrigatório de compatibilidade AOT.

## Fase 3 — Hardening do core path (médio prazo)

1. Formalizar flags MSBuild de compatibilidade AOT/trim nos pacotes core.
2. Definir política de warnings AOT/trim tratados como erro no core.
3. Adicionar validações automatizadas para impedir padrões proibidos (reflection registration, runtime method invoke, scanning no core path).
4. Criar “contrato de não regressão AOT” para PRs que alterem runtime/generator.

## Fase 4 — Evolução de desempenho sem violar AOT (médio prazo)

1. Introduzir cache keyed de baixo risco no `Mediator`/`Notifier`.
2. Revisar caminhos de stream nos decorators para reduzir materialização completa em lista.
3. Medir impacto com benchmark dedicado (JIT e NativeAOT) e registrar baseline.
4. Promover melhorias aprovadas para gate de regressão de performance.

## Fase 5 — Maturidade do ecossistema (longo prazo)

1. Publicar “AOT profile” oficial com exemplos mínimos e receitas de adoção.
2. Manter suíte de compatibilidade por pacote e por feature em release cadence.
3. Evoluir extensões futuras somente sob regra: sem quebrar Full NativeAOT + Trimming no caminho core.
4. Revisar periodicamente o contrato com base em novas versões de .NET/SDK.

---

## 7) Recomendações práticas de governança

- **Testes/CI**: além de cobertura, sempre validar publish+run AOT/trim do caminho principal.
- **Analyzers**: habilitar e endurecer validação de padrões incompatíveis no core path.
- **Documentação**: manter matriz de compatibilidade por pacote visível e atualizada.
- **Contribuições**: todo PR que toque core/generator deve declarar impacto em AOT/trim.
- **Compatibilidade**: preservar princípio central do projeto: compile-time generation + closed-type dispatch + decorators concretos.

