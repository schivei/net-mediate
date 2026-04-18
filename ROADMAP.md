# Roadmap

Este roadmap consolida sugestões de melhorias e novas features para evolução do ecossistema NetMediate.

## Curto prazo

- [ ] Publicar guias de migração completos de MediatR para `NetMediate.Compat` (cenários básicos, avançados e troubleshooting).
- [ ] Expandir a documentação do `NetMediate.Moq` com receitas de testes unitários e de integração.
- [ ] Adicionar exemplos de aplicação (API, Worker e Minimal API) consumindo `NetMediate`, `NetMediate.Compat` e `NetMediate.Moq`.
- [ ] Cobrir cenários de diagnóstico com logs estruturados e métricas por tipo de mensagem.

## Médio prazo

- [ ] Adicionar pipeline behaviors/interceptors compatíveis com o fluxo MediatR (pré/pós processamento).
- [ ] Incluir estratégias de retry, timeout e circuit-breaker para handlers de notificação/request.
- [ ] Disponibilizar suporte opcional a source generators para reduzir reflection no startup.
- [ ] Evoluir suporte a observabilidade (OpenTelemetry traces/metrics para Send/Request/Notify/Stream).

## Longo prazo

- [ ] Criar pacote de integração com principais validadores (ex.: FluentValidation) sem acoplamento obrigatório.
- [ ] Disponibilizar benchmark suite pública comparando NetMediate, MediatR e cenários de alto throughput.
- [ ] Explorar modo AOT-friendly com otimizações para trimming e NativeAOT.
- [ ] Definir trilha de extensões oficiais do ecossistema (testing, diagnostics, resilience e adapters).
