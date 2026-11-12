# PedeAqui — o que falta para ser uma API robusta

Hoje a API é um CRUD funcional de produtos, estoque e pedidos em .NET 8 com Clean Architecture, JWT, PostgreSQL e 126 testes. Ela está correta com **uma requisição por vez**, mas ainda não está preparada para concorrência, falhas ou produção.

Este checklist lista tudo o que ainda falta. Cada item traz o **problema real que ele causa hoje**.

> **Premissa: sem microsserviços.** O PedeAqui continua sendo um monólito modular. O trabalho assíncrono roda num *worker* (`BackgroundService`) do mesmo repositório, consumindo uma fila. Os conceitos de sistema distribuído (retry, DLQ, consumidor idempotente, outbox) continuam valendo, porque uma fila já é um sistema distribuído.

---

## Consistência, ACID e concorrência

> 📘 Passo a passo de implementação: [docs/README-consistencia-acid-concorrencia.md](docs/README-consistencia-acid-concorrencia.md)

- [ ] **Transação explícita envolvendo leitura e escrita.** Hoje o `Commit()` é atômico, mas a leitura do estoque acontece fora da transação que grava. Falta isolamento entre ler, decidir e escrever.
- [ ] **Lock pessimista no estoque** (`SELECT ... FOR UPDATE` ordenado por `Id`), ou um `UPDATE` atômico condicional (`... SET qtd = qtd - n WHERE qtd >= n`). Hoje dois pedidos simultâneos vendem a mesma última unidade, porque o segundo `UPDATE` sobrescreve o primeiro (*lost update*).
- [ ] **Ordem fixa de lock para evitar deadlock** quando um pedido tem vários produtos.
- [ ] **Lock otimista no pedido** (`xmin` como *concurrency token*). Hoje dois `PATCH /cancel` simultâneos devolvem o estoque **duas vezes**, e dois `PUT` simultâneos geram itens inconsistentes.
- [ ] **Lock otimista no produto.** Hoje o `PUT` do admin e uma venda concorrente podem se sobrescrever.
- [ ] **`ConflictException` → HTTP 409**, traduzindo a `DbUpdateConcurrencyException` no `UnitOfWork`.
- [ ] **CHECK constraint `StockQuantity >= 0` no banco.** Hoje só a entidade garante essa regra, e qualquer bug ou SQL manual grava estoque negativo.
- [ ] **Tratar a violação de índice único** (`23505`). E-mail e SKU já têm índice único, mas dois cadastros simultâneos passam pelo `Exists...` e o segundo estoura com **500** em vez de 409/400.
- [ ] **Retry de falhas transitórias do banco** (`EnableRetryOnFailure` + `ExecutionStrategy` envolvendo a transação).
- [ ] **Testes de concorrência em PostgreSQL real** (Testcontainers). Hoje os testes de integração usam EF InMemory, **sem transação nem lock**, e não conseguem provar nenhum item acima.

## Idempotência

- [ ] **`Idempotency-Key` no `POST /api/orders`.** Hoje um timeout seguido de reenvio pelo app cria **pedido duplicado** e baixa o estoque duas vezes.
- [ ] **Chave gravada na mesma transação do pedido, com índice único em `(UserId, Key)`.** Essa é a garantia real; consultar antes de gravar é só o caminho rápido.
- [ ] **Mesma chave com corpo diferente → 422**, comparando o hash do request.
- [ ] **Idempotência nas entradas e saídas de estoque** (`POST .../entries` e `.../exits`). Hoje um reenvio duplica a movimentação.
- [ ] **Expiração e limpeza das chaves antigas**, por exemplo um job que apaga as chaves com mais de 24 h.
- [ ] **Consumidores de fila idempotentes**, porque a mensagem pode chegar mais de uma vez (veja *Assíncrono*).

## Cache

- [ ] **Redis com cache-aside no catálogo** (`GET /api/products` e `/{id}`). Hoje a rota mais chamada sempre vai ao banco.
- [ ] **Decorator `CachedProductReadOnlyRepository`**, para que os casos de uso não mudem.
- [ ] **Invalidação depois do commit** quando um produto é criado, alterado, excluído ou tem o estoque movimentado (inclusive por pedido).
- [ ] **TTL como rede de segurança**, caso uma invalidação falhe.
- [ ] **Fallback quando o Redis cair.** Hoje o Redis nem existe, mas quando entrar, ficar fora do ar não pode derrubar a API.
- [ ] **Regra: nunca vender com base no cache.** O pedido sempre valida o estoque no banco, com lock.

## Assíncrono e mensageria (dentro do monólito)

- [ ] **Transactional Outbox.** O evento `PedidoCriado` é gravado na mesma transação do pedido, para não haver "pedido sem evento" nem "evento sem pedido".
- [ ] **Worker (`BackgroundService`)** que publica o outbox e consome a fila.
- [ ] **Fila com DLQ** (SQS; local via LocalStack).
- [ ] **Retry com backoff exponencial** e envio para a DLQ após N falhas.
- [ ] **Fluxo de pagamento simulado como máquina de estados** (saga orquestrada interna): `Pending → PaymentApproved/PaymentFailed → Confirmed/Cancelled`. Hoje não existe pagamento, e o admin confirma o pedido na mão.
- [ ] **Compensação em caso de falha**: pagamento recusado → cancelar o pedido e devolver o estoque.
- [ ] **E-mail de confirmação do pedido**, fora da requisição HTTP.
- [ ] **Expirar pedidos pendentes** que nunca foram pagos e liberar o estoque reservado.

## Resiliência

- [ ] **Polly** com timeout, retry, circuit breaker e bulkhead nas dependências externas (Redis, fila, e-mail, pagamento).
- [ ] **Rate limiting** (`AddRateLimiter`). Hoje não há limite nenhum, nem no login.
- [ ] **Timeout de requisição** (`AddRequestTimeouts`) e **`CancellationToken`** propagado do controller até o EF. Hoje uma requisição abandonada pelo cliente continua consumindo o banco.
- [ ] **Health checks** (`/health/live` e `/health/ready`, verificando banco e Redis), exigidos pelo load balancer e pelo ECS.
- [ ] **Graceful shutdown**: terminar as requisições e mensagens em andamento antes de o container morrer.
- [ ] **Limites de payload**: máximo de itens por pedido, quantidade máxima por item e tamanho do corpo.

## Observabilidade

- [ ] **Log estruturado** em JSON (Serilog), com contexto de usuário e pedido.
- [ ] **Correlation ID / `traceId`** em todo log e em toda resposta de erro (ProblemDetails).
- [ ] **OpenTelemetry** para traces e métricas: latência, erros e pedidos por minuto.
- [ ] **Métricas de negócio**: pedidos criados, cancelados, estoque zerado, conflitos 409.
- [ ] **Alarmes** para taxa de 5xx, latência p95, mensagens na DLQ e banco com CPU alta.
- [ ] **Trilha de auditoria** de quem alterou preço e produto. Hoje só o estoque tem histórico.

## Segurança

- [ ] **Segredos fora do `appsettings`**: chave JWT, senha do admin e connection string no Secrets Manager. Hoje estão em texto no repositório (valores de dev).
- [ ] **Refresh token e revogação.** Hoje o token vale até expirar, sem logout real nem forma de bloquear um usuário.
- [ ] **Proteção contra força bruta no login**, com rate limit por IP/e-mail ou bloqueio temporário.
- [ ] **CORS configurado** para o front-end.
- [ ] **Headers de segurança** e HSTS em produção.
- [ ] **Scan de dependências vulneráveis** no CI (`dotnet list package --vulnerable`).
- [ ] **Swagger desligado ou protegido** em produção.

## Design da API

- [ ] **Paginação** em `GET /api/products`, `GET /api/orders` e no histórico de estoque. Hoje cada um devolve a tabela inteira.
- [ ] **Filtros e ordenação** (por status, data e nome).
- [ ] **Versionamento** (`/api/v1`).
- [ ] **`ETag` / `If-Match`** no `PUT`, expondo o lock otimista para o cliente.
- [ ] **Erro 500 padronizado** em ProblemDetails, sem vazar stack trace.

## Banco de dados e dados

- [ ] **Migrations fora do startup.** Hoje elas só rodam em Development. Em produção, devem rodar num passo do pipeline (*migration bundle*), não em cada instância da API.
- [ ] **Seeder do admin seguro com várias instâncias.** Duas instâncias subindo juntas disputam o mesmo `INSERT`.
- [ ] **Backup e restauração testados** (RDS com *point-in-time recovery*).
- [ ] **Réplica de leitura** para relatórios (CQRS simples), sem competir com o checkout.
- [ ] **Datas de atualização** (`UpdatedAt`) nas entidades.

## Infraestrutura e AWS

- [ ] **Dockerfile da API.** Hoje só o PostgreSQL roda em container.
- [ ] **Docker Compose completo**: API, worker, PostgreSQL, Redis e LocalStack.
- [ ] **CI/CD** (GitHub Actions): build, testes, scan, publicação da imagem e deploy.
- [ ] **Infraestrutura como código** (Terraform ou AWS CDK). Hoje não existe nada na AWS.
- [ ] **ECR**, para o registro das imagens.
- [ ] **ECS Fargate** (ou App Runner), para rodar a API e o worker.
- [ ] **RDS PostgreSQL**, o banco gerenciado.
- [ ] **ElastiCache Redis**, o cache gerenciado.
- [ ] **SQS + DLQ** e **SNS**, para filas e *fan-out* de eventos.
- [ ] **S3**, para as imagens de produto.
- [ ] **SES**, para os e-mails.
- [ ] **Secrets Manager**, para os segredos.
- [ ] **CloudWatch** (logs, métricas e alarmes) e **X-Ray** (traces).
- [ ] **ALB + Auto Scaling** com várias instâncias da API. Isso só é seguro depois dos itens de concorrência e cache distribuído.
- [ ] **Ambientes separados** (dev, staging e prod).
- [ ] **AWS Budgets com alerta de custo**, configurado antes de criar qualquer recurso.

## Testes e qualidade

- [ ] **Testes de concorrência**, como 50 compradores disputando 10 unidades, que deve resultar em exatamente 10 pedidos.
- [ ] **Testes com Testcontainers** (PostgreSQL e Redis reais).
- [ ] **Testes de carga** (k6), comparando lock pessimista com update atômico.
- [ ] **Testes de falha**: Redis fora, banco lento, fila indisponível.
- [ ] **Cobertura de código** no CI.

## Deploy e escala

- [ ] **Deploy blue/green ou canary** no ECS.
- [ ] **Workers concorrentes** consumindo a mesma fila sem processar a mesma mensagem em dobro.
- [ ] **Rollback automático** quando os alarmes disparam depois do deploy.

---

## Como rodar hoje

```bash
docker compose up -d                                   # PostgreSQL na porta 5433
dotnet run --project PedeAqui --launch-profile http    # aplica as migrations e cria o admin
dotnet test PedeAqui.slnx                              # 96 unitários + 30 de integração
```

Swagger: <http://localhost:5238/swagger>. Admin de desenvolvimento: `admin@pedeaqui.dev` / `Admin@123`.
