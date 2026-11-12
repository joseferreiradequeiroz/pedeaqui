# PedeAqui — Design de robustez

## Intenção

O PedeAqui é um projeto de estudo: uma API .NET 8 em Clean Architecture (produtos, estoque, pedidos, JWT, PostgreSQL). Hoje ela funciona com uma requisição por vez, mas falha com concorrência, reenvios e quedas de dependências.

O objetivo é deixar o código robusto para que o dono **leia o código depois, com o apoio de um README, e entenda cada conceito**. Cada conceito só entra se resolver um problema real que o PedeAqui tem. O código é o material de estudo, então ele precisa ser legível, direto e com nomes claros.

## Objetivos

1. **Concorrência no estoque**: dois pedidos simultâneos nunca vendem a mesma unidade.
2. **Lock otimista** em pedido e produto, com `409 Conflict`.
3. **Idempotência** no `POST /api/orders` e nas movimentações de estoque, guardando o contexto da requisição no banco.
4. **Redis**: cache-aside do catálogo e rate limiting distribuído, ambos com fallback quando o Redis cai.
5. **Transactional Outbox + worker + RabbitMQ**, com consumidor idempotente, retry com backoff e DLQ.
6. **Gateway de pagamento fake** (serviço externo que falha e fica lento de propósito), protegido por **timeout, retry e circuit breaker**.
7. **Saga de pagamento com compensação**: pagamento recusado → pedido cancelado e estoque devolvido.
8. **Expiração de pedidos pendentes** não pagos, liberando o estoque.
9. Resiliência de borda: `CancellationToken` propagado, timeout de requisição, health checks, graceful shutdown, limites de payload.
10. Observabilidade mínima: log estruturado (Serilog) e Correlation ID atravessando API → outbox → fila → worker → gateway.
11. **Testes que provam** cada garantia em infraestrutura real (Testcontainers: PostgreSQL, Redis, RabbitMQ).

## Não-objetivos

- Microsserviços, Kafka, Kubernetes, CQRS com banco separado, event sourcing.
- Qualquer coisa de AWS, IaC, CI/CD ou deploy. Ignorar essas seções do README atual.
- Lock distribuído no Redis para o estoque. O Postgres resolve melhor; o README final vai explicar essa escolha.
- Refresh token, OpenTelemetry, paginação, versionamento de API.
- Escrever o README final. O dono vai dizer como quer depois.

## Anti-objetivos (falha mesmo se "funcionar")

- Tecnologia sem problema real que a justifique.
- **Comentários no código.** Regra do dono: nenhum comentário em código de produção ou de teste. O código precisa se explicar pelos nomes.
- Abstrações genéricas demais (frameworks internos, MediatR, MassTransit) que escondem o mecanismo que se quer estudar. O RabbitMQ é usado pelo `RabbitMQ.Client` puro, para que exchange, fila, ack/nack e DLQ fiquem visíveis.
- Quebrar a Clean Architecture existente: Domain sem dependências de infraestrutura; casos de uso não sabem de Redis, RabbitMQ nem HTTP.
- Testes de concorrência que passam no EF InMemory, que não tem lock nem transação.

## Restrições

- .NET 8 (SDK 8.0.300 instalado), C#, solution `PedeAqui.slnx`. Seguir os padrões do projeto: casos de uso com interface, repositórios separados em ReadOnly/WriteOnly/UpdateOnly, FluentValidation, AutoMapper, exceções em `PedeAqui.Exception`, mensagens em `ResourceErrorMessages`.
- Tudo roda localmente com `docker compose up`: PostgreSQL, Redis (já existem), RabbitMQ (com management) e o gateway fake.
- Os 126 testes atuais continuam passando, ou são migrados para infraestrutura real quando precisarem dela.
- **Git:** não fazer commits. O dono faz commits atômicos, fora do horário de trabalho, quando pedir.
- Implementação feita por subagentes, uma tarefa por vez, com TDD.

## Decisões do dono

- Gateway de pagamento fake como dependência externa, para justificar circuit breaker, timeout e retry.
- Transactional Outbox.
- Fora: microsserviços, Kafka, Kubernetes, CQRS.
- A idempotência guarda no banco o contexto da requisição: corpo, status e o resto.

## Desenho

### Novos projetos

| Projeto | O que é |
|---|---|
| `PedeAqui.Worker` | Host com `BackgroundService`s: publicador do outbox, consumidor de pagamentos, expirador de pedidos, limpeza de chaves de idempotência. Referencia Application e Infrastructure. |
| `PedeAqui.PaymentGateway` | Minimal API independente, que finge ser um provedor de pagamento. Não referencia nenhum projeto do PedeAqui. |
| `tests/PedeAqui.IntegrationTests` (existente) | Passa a usar Testcontainers. |

Dockerfiles para API, Worker e Gateway; tudo no `docker-compose.yaml`.

### 1. Concorrência e consistência

- **Baixa de estoque** por `UPDATE` atômico condicional (`SET stock = stock - @n WHERE id = @id AND stock >= @n`), dentro de uma transação explícita, processando os produtos em ordem crescente de `Id` para evitar deadlock. Linhas afetadas = 0 → estoque insuficiente. *Escolha do builder:* se preferir `SELECT ... FOR UPDATE`, pode, desde que mantenha a ordem por `Id`; o README vai comparar os dois.
- **CHECK constraint** `stock_quantity >= 0` no banco.
- **Lock otimista** com `xmin` como concurrency token em `Order` e `Product`. `DbUpdateConcurrencyException` vira `ConflictException` → HTTP 409.
- **Violação de índice único** (`23505`) vira 409 em vez de 500.
- **Retry de falhas transitórias** do Npgsql (`EnableRetryOnFailure`) com `CreateExecutionStrategy` envolvendo as transações explícitas.
- O `IUnitOfWork` ganha a capacidade de executar um bloco dentro de uma transação com a execution strategy.
- O guia já existente em `docs/README-consistencia-acid-concorrencia.md` serve de referência para essa parte.

### 2. Idempotência

Tabela `idempotency_requests` no PostgreSQL:

| Coluna | Para quê |
|---|---|
| `Id` | PK |
| `UserId` | dono da chave |
| `Key` | valor do header `Idempotency-Key` |
| `Method`, `Path` | qual operação |
| `RequestBody` (jsonb) | corpo original |
| `RequestHash` | SHA-256 do corpo normalizado |
| `Status` | `InProgress` / `Completed` |
| `ResponseStatusCode`, `ResponseBody` (jsonb) | resposta guardada para devolver no replay |
| `CreatedAt`, `CompletedAt`, `ExpiresAt` | ciclo de vida |

Índice único em `(UserId, Key)`.

Fluxo:
1. Header obrigatório em `POST /api/orders`, `POST /api/stock/.../entries` e `.../exits`. Sem header → 400.
2. **Duas fases** (decisão tomada): a chave é inserida como `InProgress` e commitada sozinha antes da operação. O índice único é a garantia real; a consulta antes de gravar é só o caminho rápido. Depois da operação, a linha vira `Completed` com status e corpo da resposta.
3. Mesma chave + mesmo hash + `Completed` → devolve o status e o corpo guardados, com o header `Idempotent-Replayed: true`.
4. Mesma chave + hash diferente → 422.
5. Mesma chave ainda `InProgress` (requisição concorrente) → 409.
6. Respostas 2xx e 4xx são guardadas (reenviar devolve a mesma resposta). 5xx ou exceção inesperada → a linha é apagada para permitir nova tentativa.
7. Chave presa em `InProgress` há mais de X minutos (processo morreu no meio) pode ser retomada por uma nova requisição.
8. O worker apaga as chaves vencidas (24 h).
9. **Redis como primeira barreira (rápida, não durável):** antes de ir ao Postgres, `SET idem:{userId}:{key} NX PX <ttl>` barra duplicatas concorrentes sem tocar no banco (→ 409). O Postgres continua sendo a garantia durável; Redis fora do ar → pula essa etapa. Isso existe para o curso mostrar SET NX, TTL e por que o Redis sozinho não basta.

*Escolha do builder:* implementar como filtro/atributo na API (`[Idempotent]`) + serviço na Application, desde que o caso de uso fique sem saber de HTTP.

### 3. Redis

- **Cache-aside do catálogo** por decorator `CachedProductReadOnlyRepository` (Scrutor ou registro manual), com TTL como rede de segurança.
- **Invalidação depois do commit** quando um produto é criado, alterado, excluído ou tem o estoque movimentado (inclusive por pedido, cancelamento e compensação).
- **Nunca vender com base no cache**: a criação de pedido lê do banco.
- **Rate limiting distribuído** no Redis (janela fixa ou sliding window com script Lua), por IP no login e por usuário nas rotas autenticadas. Excedeu → 429 com `Retry-After`.
- **Fallback**: Redis fora do ar não derruba a API. Cache cai direto no banco; rate limit libera (fail-open) e registra log de aviso. Chamadas ao Redis com timeout curto e circuit breaker, para não pagar o timeout em toda requisição enquanto ele está fora.

### 4. Outbox, fila e worker

- Tabela `outbox_messages` (`Id` Guid, `Type`, `Payload` jsonb, `CorrelationId`, `OccurredAt`, `ProcessedAt`, `Attempts`, `LastError`). O evento `OrderCreated` é gravado na mesma transação do pedido.
- **Publicador** no worker: lê lotes não processados com `FOR UPDATE SKIP LOCKED` (vários workers não pegam a mesma linha), publica no RabbitMQ com publisher confirms, marca `ProcessedAt`. Garantia: *at-least-once*.
- **RabbitMQ**: exchange `pedeaqui.events`, fila `payments.order-created`, filas de retry com TTL (backoff exponencial) e DLQ `payments.order-created.dlq`. Após N tentativas, a mensagem vai para a DLQ.
- **Consumidor idempotente (Inbox Pattern)**: tabela `inbox_messages` (`MessageId`, `Consumer`, `ProcessedAt`) gravada na mesma transação do efeito. Mensagem repetida → ack sem efeito. (No resto deste documento, "processed_messages" = `inbox_messages`.)
- **Prefetch** (`BasicQos`) configurável no consumidor.
- **Graceful shutdown**: o worker termina a mensagem em andamento antes de parar; ack manual só depois do commit.

### 5. Gateway de pagamento fake

- `POST /payments` com `Idempotency-Key` (o gateway também é idempotente: mesma chave devolve o mesmo resultado). Responde `Approved` ou `Declined`.
- `GET /payments/{idempotencyKey}` para a reconciliação (404 se nunca recebeu).
- Estado em memória é aceitável (é um fake), mas *escolha do builder* usar SQLite/arquivo se ajudar os testes.
- Comportamento configurável por variáveis de ambiente e por endpoint admin (`POST /chaos`): % de erros 500, % de latência alta, modo "fora do ar". Isso permite ver o circuito abrir durante o estudo.
- Regra determinística para testes: por exemplo, um valor terminado em `.13` é sempre recusado.

### 6. Resiliência do cliente HTTP

- `HttpClient` tipado `IPaymentGateway` (interface no Domain, implementação na Infrastructure).
- Pipeline com `Microsoft.Extensions.Http.Resilience`/Polly v8: **bulkhead (limitador de concorrência) → timeout total → retry (backoff exponencial com jitter, só para erros transitórios e 5xx) → circuit breaker → timeout por tentativa**. A ordem é proposital e o README vai explicá-la.
- Respostas 4xx do gateway são falhas **permanentes**: sem retry, a mensagem vai direto para a DLQ.
- Circuito aberto → o consumidor não chama o gateway; a mensagem volta para a fila de retry.
- A mesma `Idempotency-Key` (o Id do pedido) é enviada em todos os retries, para o retry não cobrar duas vezes.

### 7. Saga de pagamento (orquestrada, dentro do monólito)

Máquina de estados do pedido:

```
Pending → AwaitingPayment → Confirmed
   │                     ↘ PaymentFailed → Cancelled (estoque devolvido)
   ├→ Cancelled (pelo cliente, estoque devolvido)
   └→ Expired (estoque devolvido)
```

- `POST /api/orders` cria o pedido com o estoque já baixado (reserva) e grava `OrderCreated` no outbox. Responde 201 com status `Pending`.
- O **consumidor** move o pedido de `Pending` para `AwaitingPayment` (commit, com lock otimista) **antes** de chamar o gateway, e cobra o total atual lido do banco, não um snapshot do evento.
- **Decisão do dono (opção a): `AwaitingPayment` é travado.** A partir daí só o resultado do pagamento move o pedido. Cancelar, editar (`PUT`) ou expirar um pedido `AwaitingPayment` → 409.
- `PUT /api/orders/{id}` continua existindo, só para pedidos `Pending`, endurecido com lock otimista e baixa/devolução atômica de estoque.
- Aprovado → `Confirmed`. Recusado → **compensação**: `PaymentFailed` → `Cancelled`, devolve o estoque, grava as movimentações, invalida o cache. Tudo em uma transação, junto com o `processed_messages`.
- Transição inválida (incluindo a partir de estado final) → `ConflictException` → **409**.
- A confirmação manual do admin (`PATCH /confirm`) sai; quem confirma é o pagamento.
- **Expirador**: `BackgroundService` que, a cada minuto, expira pedidos `Pending` mais velhos que X minutos (configurável) e devolve o estoque.
- **Reconciliador**: `BackgroundService` que busca pedidos `AwaitingPayment` há mais de Y minutos (por exemplo, mensagem na DLQ ou worker que morreu no meio) e consulta o gateway (`GET /payments/{idempotencyKey}`). Aprovado → `Confirmed`; recusado ou inexistente → compensação. Gateway fora → tenta no próximo ciclo. Uma mensagem na DLQ deixa o pedido em `AwaitingPayment` até o reconciliador resolver.

### 8. Borda da API

- `CancellationToken` do controller até o EF e o Redis.
- `AddRequestTimeouts` (padrão de 10 s).
- Health checks: `/health/live` (processo de pé) e `/health/ready` (PostgreSQL, Redis, RabbitMQ). Redis fora deixa o ready como *Degraded*, não *Unhealthy*.
- Limites: máximo de itens por pedido, quantidade máxima por item, tamanho máximo do corpo.
- Erros em ProblemDetails com `traceId` e `correlationId`, sem stack trace.

### 9. Observabilidade

- Serilog com saída JSON no console.
- Middleware de Correlation ID (`X-Correlation-Id`: usa o que veio ou gera um), propagado para os logs, para o outbox, para os headers da mensagem no RabbitMQ, para o log do worker e para o header da chamada ao gateway.

### 10. Testes

- Testes de integração em PostgreSQL, Redis e RabbitMQ reais com Testcontainers. O EF InMemory sai dos testes de integração.
- Testes obrigatórios que provam as garantias:
  - 50 compradores disputando 10 unidades → exatamente 10 pedidos e estoque 0.
  - Dois cancelamentos simultâneos → um 200 e um 409; estoque devolvido uma vez.
  - Mesmo `Idempotency-Key` enviado 2× (sequencial e concorrente) → um pedido; replay com o mesmo corpo; corpo diferente → 422.
  - Redis parado → catálogo ainda responde.
  - Rate limit → 429 após o limite.
  - Outbox: pedido criado → mensagem publicada; mensagem duplicada → efeito único.
  - Gateway lento → timeout; gateway com 500 → retry; falhas seguidas → circuito abre e para de chamar.
  - Pagamento recusado → pedido cancelado e estoque devolvido.
  - Pedido `Pending` não processado → expira e devolve o estoque.
  - Cancelar pedido `AwaitingPayment` → 409.
  - Mensagem que sempre falha → termina na DLQ, pedido fica `AwaitingPayment`, reconciliador resolve.
  - Idempotência: duplicata concorrente → 409; 4xx guardado e repetido; 5xx libera a chave.
- Testes unitários para a máquina de estados do pedido e regras novas do domínio.

## O que fica com o builder

- Ordem das tarefas, divisão em subagentes, nomes exatos de classes e pastas (seguindo os padrões do projeto).
- Bibliotecas de apoio pequenas (Scrutor, Testcontainers, Serilog, Polly via `Microsoft.Extensions.Http.Resilience`), dentro do .NET 8.
- Os valores numéricos (TTL, tentativas, janelas de rate limit, timeouts), todos em `appsettings`.
- Migrations do EF para as mudanças de schema.
