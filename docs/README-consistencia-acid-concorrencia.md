# Guia — Consistência, ACID e concorrência

Passo a passo para resolver todos os itens da seção **"Consistência, ACID e concorrência"** do [README principal](../README.md).

Cada item segue o mesmo formato:

1. **Como está hoje**: o código real do projeto.
2. **O problema**: o que dá errado quando duas requisições chegam juntas.
3. **Como resolver**: passo a passo, com código.
4. **Como provar**: o teste que mostra que funcionou.

---

## Sumário

0. [Conceitos em 2 minutos](#0-conceitos-em-2-minutos)
1. [Testes de concorrência com PostgreSQL real](#1-testes-de-concorrência-com-postgresql-real) — primeiro provar o bug
2. [Transação explícita envolvendo leitura e escrita](#2-transação-explícita-envolvendo-leitura-e-escrita)
3. [Lock pessimista no estoque + ordem fixa contra deadlock](#3-lock-pessimista-no-estoque--ordem-fixa-contra-deadlock)
4. [CHECK constraint de estoque não negativo](#4-check-constraint-de-estoque-não-negativo)
5. [`ConflictException` → HTTP 409](#5-conflictexception--http-409)
6. [Lock otimista no pedido](#6-lock-otimista-no-pedido)
7. [Lock otimista no produto](#7-lock-otimista-no-produto)
8. [Violação de índice único → 400 em vez de 500](#8-violação-de-índice-único--400-em-vez-de-500)
9. [Retry de falhas transitórias do banco](#9-retry-de-falhas-transitórias-do-banco)
10. [Checklist final](#10-checklist-final)

A ordem importa: cada passo usa o anterior.

---

## 0. Conceitos em 2 minutos

**ACID** são as quatro garantias de uma transação:

| Letra | Significa | No PedeAqui hoje |
|---|---|---|
| **A**tomicidade | Tudo ou nada | ✅ O `SaveChanges` já grava pedido, itens, movimentações e estoque juntos |
| **C**onsistência | O banco só aceita estados válidos | ⚠️ Só as FKs e os índices únicos; a regra "estoque ≥ 0" só existe no C# |
| **I**solamento | Transações simultâneas não atrapalham uma à outra | ❌ A leitura e a escrita nem estão na mesma transação |
| **D**urabilidade | Commit feito não se perde | ✅ Garantido pelo PostgreSQL |

O problema central do projeto é o **I**, isolamento.

**Lost update.** Duas transações leem o mesmo valor, cada uma calcula em cima dele e a última a gravar apaga o trabalho da primeira.

**Check-then-act.** Você verifica uma condição (`estoque >= 1`) e age em seguida. Entre verificar e agir, outra requisição pode mudar o dado.

**Lock pessimista** (`SELECT ... FOR UPDATE`): "vou mexer nesta linha; quem vier depois espera". Use quando o conflito é **frequente**, como o estoque de um produto popular.

**Lock otimista** (`UPDATE ... WHERE versao = @versaoQueEuLi`): não trava nada. Na hora de gravar, confere se alguém mudou a linha; se mudou, dá erro e a requisição devolve 409. Use quando o conflito é **raro**, como dois cancelamentos do mesmo pedido.

**READ COMMITTED.** É o nível de isolamento padrão do PostgreSQL, e o que vamos usar. Ele **não** impede lost update sozinho, mas combinado com `FOR UPDATE` resolve: quando a trava é liberada, a próxima transação lê a versão já commitada da linha.

---

## Mapa: quem lê, decide e escreve hoje

Todo caso de uso abaixo segue o padrão **ler → decidir em memória → gravar**, e é aí que mora o problema.

| Caso de uso | Lê | Decide | Grava | Risco |
|---|---|---|---|---|
| `Orders/Create` | produtos (`GetByIds`) | `RemoveStock` (tem estoque?) | pedido + estoque | Vender a mesma unidade duas vezes |
| `Orders/Update` | pedido + produtos | `EnsureIsPending` + diferença de estoque | pedido + estoque | Estoque errado; alterar pedido já cancelado |
| `Orders/Cancel` | pedido + produtos | `Cancel()` (está pendente?) | pedido + estoque | **Devolver o estoque duas vezes** |
| `Orders/Confirm` | pedido | `Confirm()` (está pendente?) | pedido | Pedido confirmado **e** cancelado ao mesmo tempo |
| `Stock/Entry` e `Stock/Exit` | produto | `AddStock` / `RemoveStock` | estoque | Lost update no saldo |
| `Products/Update` | produto | AutoMapper no request | **todas** as colunas | Admin apaga uma venda que aconteceu no meio (ver item 7) |
| `Users/Register`, `Products/Register` | `Exists...` | "não existe, pode criar" | insert | Duplicado → **500** |

---

## 1. Testes de concorrência com PostgreSQL real

### Como está hoje

Os testes de integração em `tests/PedeAqui.IntegrationTests/PedeAquiWebApplicationFactory.cs` trocam o banco pelo EF InMemory:

```csharp
services.AddDbContext<PedeAquiDbContext>(options => options.UseInMemoryDatabase(_databaseName));
```

### O problema

O InMemory **não tem transação, lock, constraint nem SQL**. Ele é ótimo para testar "a rota chama o caso de uso e grava", mas não consegue reproduzir nenhum bug deste guia. Nenhuma correção daqui pode ser provada com ele.

### Como resolver

Crie uma segunda factory que sobe um **PostgreSQL de verdade num container** (Testcontainers). Ela fica no mesmo projeto de testes de integração, numa pasta nova.

**Passo 1 — pacote** (precisa do Docker rodando):

```bash
dotnet add tests/PedeAqui.IntegrationTests package Testcontainers.PostgreSql
```

**Passo 2 — factory** em `tests/PedeAqui.IntegrationTests/Concurrency/PostgresApiFactory.cs`:

```csharp
public class PostgresApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string ADMIN_EMAIL = "admin@pedeaqui.test";
    public const string ADMIN_PASSWORD = "Admin@123";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Connection", _postgres.GetConnectionString());
        builder.UseSetting("Settings:Admin:Email", ADMIN_EMAIL);
        builder.UseSetting("Settings:Admin:Password", ADMIN_PASSWORD);
    }
}
```

- **`UseEnvironment("Development")`** faz o próprio `Program.cs` aplicar as migrations no container e criar o admin, como acontece quando você roda a API.
- **`DisposeAsync` explícito**: o `WebApplicationFactory` já tem um `DisposeAsync` próprio (com retorno `ValueTask`), então a implementação do `IAsyncLifetime` precisa ser explícita.
- **Container por classe de teste**: cada classe que usa `IClassFixture<PostgresApiFactory>` ganha o seu.

**Passo 3 — helpers HTTP** em `Concurrency/ConcurrencyApi.cs`:

```csharp
public static class ConcurrencyApi
{
    public static async Task<HttpClient> Admin(this PostgresApiFactory factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/users/login",
            new LoginRequest { Email = PostgresApiFactory.ADMIN_EMAIL, Password = PostgresApiFactory.ADMIN_PASSWORD });
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    public static async Task<HttpClient> NewCustomer(this PostgresApiFactory factory)
    {
        var client = factory.CreateClient();
        var register = await client.PostAsJsonAsync("/api/users", new RegisterUserRequest
        {
            Name = "Cliente",
            Email = $"{Guid.NewGuid():N}@pedeaqui.test",
            Password = "Senha@123"
        });
        var body = await register.Content.ReadFromJsonAsync<RegisterUserResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    public static async Task<long> CreateProductWithStock(this HttpClient admin, int stock)
    {
        var created = await admin.PostAsJsonAsync("/api/products", new ProductRequest
        {
            Name = "Produto disputado",
            Sku = Guid.NewGuid().ToString("N")[..20],
            Price = 10m
        });
        var product = await created.Content.ReadFromJsonAsync<ProductResponse>();

        await admin.PostAsJsonAsync($"/api/products/{product!.Id}/stock/entries",
            new StockMovementRequest { Quantity = stock, Reason = "Carga inicial" });

        return product.Id;
    }

    public static async Task<int> StockOf(this HttpClient client, long productId)
    {
        var product = await client.GetFromJsonAsync<ProductResponse>($"/api/products/{productId}");
        return product!.StockQuantity;
    }

    public static Task<HttpResponseMessage> Buy(this HttpClient customer, long productId, int quantity = 1) =>
        customer.PostAsJsonAsync("/api/orders", new OrderRequest
        {
            Items = [new OrderItemRequest { ProductId = productId, Quantity = quantity }]
        });
}
```

**Passo 4 — o teste que tem que FALHAR hoje** em `Concurrency/StockConcurrencyTests.cs`:

```csharp
[Trait("Category", "Concurrency")]
public class StockConcurrencyTests : IClassFixture<PostgresApiFactory>
{
    private readonly PostgresApiFactory _factory;

    public StockConcurrencyTests(PostgresApiFactory factory) => _factory = factory;

    [Fact]
    public async Task CreateOrder_ManyCustomersBuyingTheLastUnits_NeverSellsMoreThanStock()
    {
        const int stock = 10, buyers = 50;
        var admin = await _factory.Admin();
        var productId = await admin.CreateProductWithStock(stock);
        var customers = await Task.WhenAll(Enumerable.Range(0, buyers).Select(_ => _factory.NewCustomer()));

        var responses = await Task.WhenAll(customers.Select(customer => customer.Buy(productId)));

        Assert.Equal(stock, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(buyers - stock, responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity));
        Assert.Equal(0, await admin.StockOf(productId));
    }
}
```

Dicas:
- **Crie os clientes antes do `Task.WhenAll` das compras.** Assim os 50 pedidos disparam juntos, e não espalhados entre cadastros.
- **Rode várias vezes.** Bug de concorrência é probabilístico. Hoje o esperado é ver **mais de 10** pedidos criados.
- **Separe pelo `Trait`:**
  ```bash
  dotnet test --filter "Category=Concurrency"     # só concorrência (precisa de Docker)
  dotnet test --filter "Category!=Concurrency"    # o resto, sem Docker
  ```

**Só siga para o item 2 depois de ver esse teste falhar.** Teste de correção que nunca falhou não prova nada.

---

## 2. Transação explícita envolvendo leitura e escrita

### Como está hoje

`PedeAqui.Infrastructure/Database/UnitOfWork.cs`:

```csharp
public async Task Commit()
{
    await _dbContext.SaveChangesAsync();
}
```

`PedeAqui.Application/UseCases/Orders/Create/CreateOrderUseCase.cs`, resumido:

```csharp
var products = await _productRepository.GetByIds(...);   // SELECT  → transação implícita 1 (termina na hora)
product.RemoveStock(item.Quantity, ...);                  // decide em memória
await _unitOfWork.Commit();                               // UPDATE  → transação implícita 2
```

### O problema

O `SaveChanges` **é atômico**: pedido, itens, movimentações e estoque entram juntos. Mas a **leitura** aconteceu numa transação anterior, que já acabou:

| t | Requisição A | Requisição B | Estoque no banco |
|---|---|---|---|
| 1 | `SELECT` → lê **1** | | 1 |
| 2 | | `SELECT` → lê **1** | 1 |
| 3 | `RemoveStock(1)` ✔ → memória 0 | | 1 |
| 4 | | `RemoveStock(1)` ✔ → memória 0 | 1 |
| 5 | `UPDATE ... SET "StockQuantity" = 0` | | 0 |
| 6 | | `UPDATE ... SET "StockQuantity" = 0` | **0** |

Resultado: dois pedidos criados para uma unidade física. A regra `RemoveStock` está certa; ela só decidiu com um dado velho. Repare que o EF grava o **valor absoluto** (`= 0`), não `= StockQuantity - 1`, por isso o segundo `UPDATE` não percebe nada.

### Como resolver

A ideia é abrir **uma** transação antes da leitura e fechá-la só depois da escrita. Sozinha, a transação ainda não resolve (em `READ COMMITTED` os dois `SELECT` continuam lendo 1), mas ela é o **pré-requisito** para o lock do item 3 funcionar: o lock só dura enquanto a transação estiver aberta.

**Passo 1 — contrato** em `PedeAqui.Domain/Repositories/IUnitOfWork.cs`:

```csharp
public interface IUnitOfWork
{
    Task Commit();
    Task<T> ExecuteInTransaction<T>(Func<Task<T>> operation);
    Task ExecuteInTransaction(Func<Task> operation);
}
```

A Application continua sem conhecer o EF. Ela só pede "rode isto numa transação".

**Passo 2 — implementação** em `PedeAqui.Infrastructure/Database/UnitOfWork.cs`:

```csharp
public async Task<T> ExecuteInTransaction<T>(Func<Task<T>> operation)
{
    await using var transaction = await _dbContext.Database.BeginTransactionAsync();

    var result = await operation();

    await _dbContext.SaveChangesAsync();
    await transaction.CommitAsync();

    return result;
}

public Task ExecuteInTransaction(Func<Task> operation) =>
    ExecuteInTransaction(async () =>
    {
        await operation();
        return true;
    });
```

- **`BeginTransactionAsync`** manda um `BEGIN` ao PostgreSQL. Tudo que o `DbContext` fizer até o commit roda nessa transação.
- **O `SaveChangesAsync`** percebe a transação aberta e **não cria outra**.
- **Se `operation()` lançar exceção** (404, 422...), o `CommitAsync` nunca roda. O `await using` descarta a transação e o banco faz **ROLLBACK**.
- **Regra para quem usa:** tudo que **escreve** precisa estar dentro do lambda.

**Passo 3 — fakes dos testes unitários** em `tests/PedeAqui.UnitTests/Fakes/FakeServices.cs`:

```csharp
public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int CommitCount { get; private set; }

    public Task Commit()
    {
        CommitCount++;
        return Task.CompletedTask;
    }

    public async Task<T> ExecuteInTransaction<T>(Func<Task<T>> operation)
    {
        var result = await operation();
        CommitCount++;
        return result;
    }

    public async Task ExecuteInTransaction(Func<Task> operation)
    {
        await operation();
        CommitCount++;
    }
}
```

Os testes que verificam `CommitCount == 0` depois de um erro continuam válidos: se o lambda lança exceção, o `CommitCount++` não executa.

**Passo 4 — testes de integração (InMemory).** O InMemory lança exceção ao abrir transação. Mande-o ignorar, em `PedeAquiWebApplicationFactory.cs`:

```csharp
services.AddDbContext<PedeAquiDbContext>(options => options
    .UseInMemoryDatabase(_databaseName)
    .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
```

Os casos de uso só passam a usar o `ExecuteInTransaction` no item 3, junto com o lock.

---

## 3. Lock pessimista no estoque + ordem fixa contra deadlock

### Como está hoje

`ProductRepository.GetByIds` é um `SELECT` comum, sem trava:

```csharp
public async Task<List<Product>> GetByIds(IEnumerable<long> ids)
{
    var distinctIds = ids.Distinct().ToList();
    return await _dbContext.Products
        .Where(product => distinctIds.Contains(product.Id))
        .ToListAsync();
}
```

### O problema

É a tabela do item 2: as duas requisições leem o mesmo valor. A transação explícita garante que leitura e escrita estão juntas, mas não impede que **outra transação** leia a mesma linha ao mesmo tempo.

### Como resolver

Trocar a leitura por `SELECT ... FOR UPDATE` **dentro** da transação. A linha fica travada até o `COMMIT`:

| t | Requisição A | Requisição B | Estoque |
|---|---|---|---|
| 1 | `BEGIN` + `SELECT ... FOR UPDATE` → lê 1 e **trava** | | 1 |
| 2 | | `BEGIN` + `SELECT ... FOR UPDATE` → **espera** ⏳ | 1 |
| 3 | `RemoveStock(1)`, `UPDATE = 0`, `COMMIT` → solta a trava | | 0 |
| 4 | | recebe a linha **já atualizada**: lê **0** | 0 |
| 5 | | `RemoveStock(1)` → 422, `ROLLBACK` | 0 |

**Passo 1 — contrato** em `PedeAqui.Domain/Repositories/Products/IProductUpdateOnlyRepository.cs`:

```csharp
public interface IProductUpdateOnlyRepository
{
    Task<Product?> GetById(long id);
    Task<List<Product>> GetByIds(IEnumerable<long> ids);
    Task<List<Product>> GetByIdsForUpdate(IEnumerable<long> ids);
    void Update(Product product);
}
```

**Passo 2 — implementação** em `ProductRepository.cs`:

```csharp
public async Task<List<Product>> GetByIdsForUpdate(IEnumerable<long> ids)
{
    if (!_dbContext.Database.IsRelational())
        return await GetByIds(ids);

    var sortedIds = ids.Distinct().Order().ToArray();

    return await _dbContext.Products
        .FromSqlInterpolated($@"SELECT * FROM products WHERE ""Id"" = ANY({sortedIds}) ORDER BY ""Id"" FOR UPDATE")
        .ToListAsync();
}
```

Linha por linha:
- **`IsRelational()`**: o InMemory dos testes de integração não executa SQL, então ali caímos no `GetByIds` normal. É uma concessão consciente: esses testes **não** provam a trava. Quem prova é o teste do item 1. Quando todos os testes de integração migrarem para Testcontainers, essa linha sai.
- **`Order()` + `ORDER BY "Id"`** previne **deadlock**. Sem isso:
  - O pedido A (produtos 1 e 2) trava o 1 e espera o 2.
  - O pedido B (produtos 2 e 1) trava o 2 e espera o 1.
  - Os dois esperam para sempre. O PostgreSQL detecta o ciclo e mata um deles com erro `40P01`.
  
  Travando **sempre em ordem crescente de Id**, o ciclo não tem como acontecer: quem chega depois espera na primeira linha em comum.
- **`FromSqlInterpolated`** transforma `{sortedIds}` em parâmetro `bigint[]`. Não é concatenação de texto, então não há SQL injection.
- **`"Id"` com aspas**: a `ProductConfiguration` só renomeia a tabela (`products`). As colunas continuam em PascalCase, e o PostgreSQL só respeita maiúsculas entre aspas.
- **As entidades voltam rastreadas pelo EF**, então o `SaveChanges` detecta o `RemoveStock` normalmente.

> **Detalhe avançado.** O PostgreSQL também tem o `FOR NO KEY UPDATE`, uma trava mais leve que não bloqueia inserts de outras tabelas que apontam (FK) para o produto, como `order_items` e `stock_movements`. Como você não altera o `Id` do produto, ele também serviria. Comece com `FOR UPDATE` (mais conhecido) e troque depois, se quiser comparar no teste de carga.

**Passo 3 — fake** em `tests/PedeAqui.UnitTests/Fakes/InMemoryProductRepository.cs`:

```csharp
public Task<List<Product>> GetByIdsForUpdate(IEnumerable<long> ids) => GetByIds(ids);
```

**Passo 4 — `CreateOrderUseCase`:**

```csharp
public async Task<OrderResponse> Execute(OrderRequest request)
{
    new OrderValidator().Validate(request).ThrowIfInvalid();

    var user = await _loggedUser.Get();

    var order = await _unitOfWork.ExecuteInTransaction(async () =>
    {
        var products = await _productRepository.GetByIdsForUpdate(request.Items.Select(item => item.ProductId));

        var newOrder = new Order { UserId = user.Id };
        await _orderRepository.Add(newOrder);

        var movements = new List<StockMovement>();

        foreach (var item in request.Items)
        {
            var product = products.FirstOrDefault(p => p.Id == item.ProductId)
                ?? throw new NotFoundException($"{ResourceErrorMessages.PRODUCT_NOT_FOUND} Id: {item.ProductId}.");

            product.EnsureCanBeSold();

            movements.Add(product.RemoveStock(item.Quantity, STOCK_REASON, newOrder));
            newOrder.SetItem(product, item.Quantity);
        }

        await _movementRepository.AddRange(movements);

        return newOrder;
    });

    return _mapper.Map<OrderResponse>(order);
}
```

O que mudou:

| Mudança | Por quê |
|---|---|
| `GetByIds` → `GetByIdsForUpdate` | Trava os produtos |
| O miolo foi para dentro do `ExecuteInTransaction` | A trava dura até o `COMMIT` |
| Saiu o `_unitOfWork.Commit()` | O `ExecuteInTransaction` já faz `SaveChanges` + commit |
| Saiu o `_productRepository.Update(product)` | A entidade já está rastreada. O `Update()` marca **todas** as colunas como alteradas; sem ele o EF grava só o que mudou (ver item 7) |
| O lambda devolve a **entidade**, e o `Map` fica fora | O `order.Id` só existe depois do `SaveChanges`, que roda no fim do `ExecuteInTransaction`. Mapear dentro do lambda devolveria `Id = 0` |
| Validação e `_loggedUser.Get()` ficaram fora da transação | Não mexem no estoque. **Transação curta significa trava segurada por menos tempo.** Nunca coloque chamada HTTP, e-mail ou processamento pesado dentro do lambda |

**Passo 5 — os outros casos de uso que mexem em estoque**

**`Orders/Cancel`:**

```csharp
public async Task Execute(long id)
{
    var user = await _loggedUser.Get();

    await _unitOfWork.ExecuteInTransaction(async () =>
    {
        var order = await _orderRepository.GetById(user, id)
            ?? throw new NotFoundException(ResourceErrorMessages.ORDER_NOT_FOUND);

        order.Cancel();

        var products = await _productRepository.GetByIdsForUpdate(order.Items.Select(item => item.ProductId));
        var movements = order.Items
            .Select(item => products.First(p => p.Id == item.ProductId)
                .AddStock(item.Quantity, $"Cancelamento do pedido #{order.Id}", order))
            .ToList();

        await _movementRepository.AddRange(movements);
    });
}
```

**`Orders/Update`:** o mesmo padrão. Trave a **união** dos produtos antigos e novos, que o código já calcula em `productIds`:

```csharp
var products = await _productRepository.GetByIdsForUpdate(productIds);
```

**`Stock/Entry` e `Stock/Exit`:** troque o `GetById(productId)` por:

```csharp
var product = (await _productRepository.GetByIdsForUpdate([productId])).FirstOrDefault()
    ?? throw new NotFoundException(ResourceErrorMessages.PRODUCT_NOT_FOUND);
```

> Nesses dois, o `GetById` atual filtra `Active`, e o `FOR UPDATE` acima não filtra. Se quiser manter "não movimentar produto inativo", acrescente `AND "Active"` no SQL ou faça a checagem no caso de uso.

> No `Cancel` e no `Update` isso protege o **estoque**, mas ainda não o **pedido**: dois cancelamentos simultâneos continuam lendo `Status = Pending` ao mesmo tempo. Isso é resolvido no item 6.

### Alternativa: update atômico condicional

Em vez de ler, travar e gravar, dá para deixar o banco decidir numa única instrução:

```csharp
var affected = await _dbContext.Products
    .Where(p => p.Id == productId && p.Active && p.StockQuantity >= quantity)
    .ExecuteUpdateAsync(set => set.SetProperty(p => p.StockQuantity, p => p.StockQuantity - quantity));

if (affected == 0)
    throw new BusinessRuleException(ResourceErrorMessages.INSUFFICIENT_STOCK);
```

| | `FOR UPDATE` | Update atômico |
|---|---|---|
| Performance | Boa | Melhor (uma ida ao banco, sem espera explícita) |
| Onde fica a regra "estoque ≥ quantidade" | Na entidade `Product` (DDD) | No SQL / repositório |
| Mensagem de erro | Detalhada ("disponível 1, solicitado 3") | Genérica, a não ser que você consulte de novo |
| `StockMovement` | Gerada pela entidade | Você monta à parte |

Para estudar, implemente o `FOR UPDATE` primeiro (mantém o domínio intacto). Depois implemente o atômico num branch e compare os dois no teste de carga.

### Como provar

Rode o teste do item 1: agora ele passa. Rode umas 10 vezes seguidas.

---

## 4. CHECK constraint de estoque não negativo

### Como está hoje

A regra só existe em `Product.RemoveStock`:

```csharp
if (quantity > StockQuantity)
    throw new BusinessRuleException(...);
```

### O problema

Qualquer caminho que não passe pela entidade (um script SQL, um `ExecuteUpdate` futuro, um bug, alguém esquecendo o lock) grava estoque negativo, e o banco aceita sem reclamar. O **C** do ACID (consistência) depende de o banco conhecer as regras invioláveis.

### Como resolver

**Passo 1** — em `ProductConfiguration.cs`:

```csharp
builder.ToTable("products", table =>
    table.HasCheckConstraint("ck_products_stock_non_negative", "\"StockQuantity\" >= 0"));
```

**Passo 2** — migration:

```bash
dotnet ef migrations add StockNonNegative --project PedeAqui.Infrastructure --startup-project PedeAqui --output-dir Database/Migrations
```

**Passo 3** — confira o SQL gerado:

```bash
dotnet ef migrations script --project PedeAqui.Infrastructure --startup-project PedeAqui
# deve aparecer: ALTER TABLE products ADD CONSTRAINT ck_products_stock_non_negative CHECK ("StockQuantity" >= 0);
```

Se a constraint for violada, o PostgreSQL devolve o erro `23514`. Isso **não deveria acontecer** se o lock estiver certo, então deixe virar 500 e aparecer no log: é sinal de bug. A constraint é a **rede de segurança**, não o mecanismo principal.

### Como provar

Teste de integração com Testcontainers: rode `UPDATE products SET "StockQuantity" = -1` direto pelo `DbContext` e verifique que lança `DbUpdateException` / `PostgresException` com `SqlState == "23514"`.

---

## 5. `ConflictException` → HTTP 409

### Como está hoje

As exceções em `PedeAqui.Exception/ExceptionsBase` cobrem 400, 401, 404 e 422. Não existe **409 Conflict**. Uma `DbUpdateConcurrencyException` do EF não é uma `PedeAquiException`, então o `PedeAquiExceptionHandler` não a trata e ela sai como **500**.

### Por que precisamos

Os itens 6 e 7 vão gerar conflitos de concorrência. Para o cliente, isso **não é erro do servidor**: significa "alguém mudou isso enquanto você mexia; recarregue e tente de novo". O código HTTP certo para isso é o **409**.

### Como resolver

**Passo 1** — `PedeAqui.Exception/ExceptionsBase/ConflictException.cs`:

```csharp
public class ConflictException : PedeAquiException
{
    public ConflictException(string message) : base(message)
    {
    }

    public override HttpStatusCode GetStatusCode() => HttpStatusCode.Conflict;

    public override IList<string> GetErrorMessages() => [Message];
}
```

**Passo 2** — mensagem em `ResourceErrorMessages.cs`:

```csharp
public const string CONCURRENCY_CONFLICT = "O registro foi alterado por outra requisição. Recarregue e tente novamente.";
```

**Passo 3** — traduzir no `UnitOfWork`. A Application não conhece o EF, então o lugar da tradução é a Infrastructure. Centralize num método privado, usado pelo `Commit` e pelo `ExecuteInTransaction`:

```csharp
public async Task Commit()
{
    await SaveChanges();
}

public async Task<T> ExecuteInTransaction<T>(Func<Task<T>> operation)
{
    await using var transaction = await _dbContext.Database.BeginTransactionAsync();

    var result = await operation();

    await SaveChanges();
    await transaction.CommitAsync();

    return result;
}

private async Task SaveChanges()
{
    try
    {
        await _dbContext.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        throw new ConflictException(ResourceErrorMessages.CONCURRENCY_CONFLICT);
    }
}
```

O `PedeAquiExceptionHandler` não muda: ele já trata qualquer `PedeAquiException` pelo `GetStatusCode()`.

---

## 6. Lock otimista no pedido

### Como está hoje

`Order` não tem nenhuma coluna de versão. `CancelOrderUseCase` e `ConfirmOrderUseCase` leem o pedido, chamam `order.Cancel()` / `order.Confirm()` (que verificam `EnsureIsPending`) e gravam.

### O problema

**Dois cancelamentos simultâneos** (o cliente clicou duas vezes, ou o app repetiu a chamada):

| t | Cancelamento A | Cancelamento B |
|---|---|---|
| 1 | lê o pedido: `Pending` | |
| 2 | | lê o pedido: `Pending` |
| 3 | `Cancel()` ✔, devolve 3 unidades | |
| 4 | | `Cancel()` ✔, devolve **mais** 3 unidades |
| 5 | grava | grava |

O estoque subiu 6, mas o pedido tinha 3 unidades. Com o item 3 aplicado, os **produtos** ficam travados, mas o **pedido** não. B espera a trava do produto, só que já tinha lido `Pending` antes de esperar.

O mesmo acontece com **cancelar e confirmar ao mesmo tempo** (o pedido termina confirmado **e** com o estoque devolvido) e com dois `PUT` simultâneos.

### Como resolver

**Por que otimista aqui?** Conflito no mesmo pedido é **raro**. Travar todo pedido sempre seria desperdício; basta **detectar** o conflito na hora de gravar e recusar.

O PostgreSQL tem uma coluna de sistema chamada **`xmin`**, que muda a cada `UPDATE` da linha. O Npgsql a usa como versão sem precisar criar coluna nova.

**Passo 1** — na entidade `Order`:

```csharp
public uint Version { get; private set; }
```

**Passo 2** — em `OrderConfiguration.cs`:

```csharp
builder.Property(order => order.Version).IsRowVersion();
```

**Passo 3** — migration:

```bash
dotnet ef migrations add OrderVersion --project PedeAqui.Infrastructure --startup-project PedeAqui --output-dir Database/Migrations
```

A migration pode aparecer com um `AddColumn` para `xmin`, mas o Npgsql **não gera SQL** para ela, porque é coluna de sistema. Confira com `dotnet ef migrations script`: não deve haver `ALTER TABLE orders ADD xmin`.

**O que muda no SQL.** O `UPDATE` gerado pelo EF passa a ser:

```sql
UPDATE orders SET "Status" = 'Cancelled', ... WHERE "Id" = 42 AND xmin = 1234;
```

Se outra transação alterou o pedido depois da sua leitura, o `xmin` mudou, **0 linhas** são afetadas e o EF lança `DbUpdateConcurrencyException`. O item 5 transforma isso em **409**.

**O detalhe que faz tudo funcionar.** O `UPDATE` do pedido e os `UPDATE`s de estoque estão **na mesma transação** (item 2). Quando o pedido dá conflito, **toda a transação faz rollback**, inclusive o estoque que o cancelamento B tinha devolvido. Sem a transação explícita, o lock otimista não resolveria o problema.

Como fica a linha do tempo:

| t | Cancelamento A | Cancelamento B |
|---|---|---|
| 1 | lê o pedido (`xmin = 100`) | |
| 2 | | lê o pedido (`xmin = 100`) |
| 3 | trava produtos, devolve estoque | espera a trava dos produtos ⏳ |
| 4 | `UPDATE orders ... WHERE xmin = 100` ✔ → `COMMIT` | |
| 5 | | trava produtos, devolve estoque (em memória) |
| 6 | | `UPDATE orders ... WHERE xmin = 100` → **0 linhas** → **ROLLBACK** → 409 |

**Passo 4** — os casos de uso `Cancel`, `Confirm` e `Update` devem rodar dentro de `ExecuteInTransaction` (o `Cancel` e o `Update` já foram mudados no item 3). No `Confirm`:

```csharp
await _unitOfWork.ExecuteInTransaction(async () =>
{
    var order = await _orderRepository.GetById(user, id)
        ?? throw new NotFoundException(ResourceErrorMessages.ORDER_NOT_FOUND);

    order.Confirm();
});
```

O `_orderRepository.Update(order)` pode sair: o pedido já vem rastreado do `IOrderUpdateOnlyRepository.GetById`.

**Passo 5** — itens do pedido. O `xmin` é da linha de `orders`. Mudar só `order_items` não alteraria o `xmin`, mas no PedeAqui toda mudança de item passa por `SetItem`/`RemoveItem`, que chamam `RecalculateTotal()`. Esse método altera `Total` e `UpdatedAt`, então a linha do pedido sempre é atualizada. Mantenha isso: **toda alteração no agregado tem que tocar a raiz.**

### Como provar

Com Testcontainers:

```csharp
[Fact]
public async Task CancelOrder_TwoSimultaneousCancels_ReturnsStockOnlyOnce()
{
    var admin = await _factory.Admin();
    var productId = await admin.CreateProductWithStock(10);
    var customer = await _factory.NewCustomer();
    var created = await customer.Buy(productId, quantity: 3);
    var order = await created.Content.ReadFromJsonAsync<OrderResponse>();

    var responses = await Task.WhenAll(
        customer.PatchAsync($"/api/orders/{order!.Id}/cancel", null),
        customer.PatchAsync($"/api/orders/{order.Id}/cancel", null));

    Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
    Assert.Single(responses, r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity);
    Assert.Equal(10, await admin.StockOf(productId));
}
```

Por que **409 ou 422**? Depende de quando B lê o pedido:
- **Antes de A fazer commit:** B ainda vê `Pending`, e o conflito é detectado no `UPDATE` → **409**.
- **Depois do commit de A:** B já vê `Cancelled`, e o `EnsureIsPending` recusa → **422**.

Os dois são corretos. O que **nunca** pode acontecer é dois `204` ou o estoque terminar em 13.

---

## 7. Lock otimista no produto

### Como está hoje

`UpdateProductUseCase`:

```csharp
var product = await _updateOnlyRepository.GetById(id);
_mapper.Map(request, product);
_updateOnlyRepository.Update(product);        // _dbContext.Products.Update(product)
await _unitOfWork.Commit();
```

### O problema (e é pior do que parece)

`DbSet.Update(entity)` marca **todas** as colunas como alteradas. O SQL gerado é:

```sql
UPDATE products SET "Name" = ..., "Price" = ..., "Sku" = ..., "StockQuantity" = 10, "Active" = ... WHERE "Id" = 42;
```

Repare no `StockQuantity = 10`. O PUT do produto **nem mexe em estoque**, mas grava o estoque que leu:

| t | Admin (PUT preço) | Cliente (POST pedido, 3 un.) | Estoque |
|---|---|---|---|
| 1 | lê o produto: estoque 10 | | 10 |
| 2 | | trava, baixa 3, commit | 7 |
| 3 | grava **todas** as colunas: `StockQuantity = 10` | | **10** |

A venda "desapareceu" do saldo, mas a `StockMovement` de saída continua lá. Saldo e histórico ficam inconsistentes.

O mesmo vale para dois admins editando o mesmo produto: o último a salvar apaga a mudança do outro sem saber.

### Como resolver

São duas correções complementares.

**Correção 1 — gravar só o que mudou.** Remova a chamada `_updateOnlyRepository.Update(product)` do `UpdateProductUseCase`. O produto já vem **rastreado** do `IProductUpdateOnlyRepository.GetById`, então o EF compara com o valor original e gera:

```sql
UPDATE products SET "Price" = 12.90 WHERE "Id" = 42;
```

Faça o mesmo em todos os casos de uso que chamam `Update()` em entidade rastreada. Se depois disso o método `Update` dos repositórios ficar sem uso, apague-o da interface.

**Correção 2 — versão no produto**, para os casos em que o conflito é no **mesmo campo** (dois admins mudando o preço):

```csharp
public uint Version { get; private set; }
```

```csharp
builder.Property(product => product.Version).IsRowVersion();
```

Mais uma migration (`ProductVersion`).

**Como isso convive com o `FOR UPDATE` do item 3?** Muito bem:
- **Pedidos e movimentações de estoque** pegam a trava antes de ler. O `xmin` que eles leem é o atual, então nunca dão conflito.
- **O PUT do admin** não trava. Se uma venda acontecer no meio, o `xmin` muda e o PUT recebe **409**. O admin recarrega a tela, vê o estoque novo e salva de novo.

> Quer o comportamento oposto (o PUT do admin espera em vez de falhar)? Use `GetByIdsForUpdate([id])` dentro de um `ExecuteInTransaction` no `UpdateProductUseCase`. Para tela de admin, o 409 costuma ser melhor: ele avisa que o dado mudou.

### Como provar

Com Testcontainers:
1. Crie um produto com estoque 10.
2. Leia-o pelo `DbContext` num escopo A.
3. Faça uma compra de 3 pela API.
4. Altere o preço no escopo A e chame `SaveChanges`.
5. O esperado é `DbUpdateConcurrencyException` (ou 409 via API) e estoque final **7**.

Um teste mais simples, que prova só a correção 1: compra e PUT de preço em paralelo, repetidos 20 vezes. O estoque final sempre bate com `inicial - vendidos`.

---

## 8. Violação de índice único → 400 em vez de 500

### Como está hoje

Os índices únicos já existem (veja a migration `Initial`):

```csharp
builder.HasIndex(user => user.Email).IsUnique();       // IX_users_Email
builder.HasIndex(product => product.Sku).IsUnique();   // IX_products_Sku
```

E os casos de uso consultam antes de inserir:

```csharp
if (await _readOnlyRepository.ExistsUserWithEmail(request.Email))
    result.Errors.Add(new ValidationFailure(nameof(request.Email), ResourceErrorMessages.EMAIL_ALREADY_REGISTERED));
```

### O problema

É o mesmo *check-then-act*. Dois cadastros com o mesmo e-mail ao mesmo tempo:

| t | Cadastro A | Cadastro B |
|---|---|---|
| 1 | `Exists` → não existe | |
| 2 | | `Exists` → não existe |
| 3 | `INSERT` ✔ | |
| 4 | | `INSERT` → **violação do índice único** (`23505`) |

O índice protege os dados, **mas B recebe 500** (`DbUpdateException` não tratada), em vez do 400 "e-mail já cadastrado".

### Como resolver

O índice é a **garantia real**; o `Exists` é só o caminho rápido, com a mensagem bonita no caso comum. Os dois ficam. Falta traduzir a violação do índice.

**Passo 1 — nomes explícitos nos índices**, para não depender da convenção do EF:

```csharp
builder.HasIndex(user => user.Email).IsUnique().HasDatabaseName("ux_users_email");
builder.HasIndex(product => product.Sku).IsUnique().HasDatabaseName("ux_products_sku");
```

Gere uma migration (`UniqueIndexNames`): ela renomeia os índices.

**Passo 2 — traduzir no `UnitOfWork.SaveChanges`** (o mesmo método do item 5):

```csharp
private async Task SaveChanges()
{
    try
    {
        await _dbContext.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        throw new ConflictException(ResourceErrorMessages.CONCURRENCY_CONFLICT);
    }
    catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres)
    {
        throw TranslateUniqueViolation(postgres.ConstraintName);
    }
}

private static PedeAquiException TranslateUniqueViolation(string? constraintName) => constraintName switch
{
    "ux_users_email" => new ErrorOnValidationException([ResourceErrorMessages.EMAIL_ALREADY_REGISTERED]),
    "ux_products_sku" => new ErrorOnValidationException([ResourceErrorMessages.PRODUCT_SKU_ALREADY_EXISTS]),
    _ => new ConflictException(ResourceErrorMessages.CONCURRENCY_CONFLICT)
};
```

- **A ordem dos `catch` importa**: `DbUpdateConcurrencyException` herda de `DbUpdateException`, então tem que vir antes.
- **`PostgresException` e `PostgresErrorCodes`** vêm do Npgsql (`using Npgsql;`), que a Infrastructure já referencia.
- **Por que 400 e não 409?** Para o cliente, a resposta fica **idêntica** à do caminho normal, em que o `Exists` encontrou o e-mail. Ele não precisa saber que houve corrida.

### Como provar

Com Testcontainers, dispare 10 `POST /api/users` simultâneos com o **mesmo e-mail**. O esperado é exatamente um `201`, nove `400` e nenhum `500`.

---

## 9. Retry de falhas transitórias do banco

### Como está hoje

```csharp
services.AddDbContext<PedeAquiDbContext>(options => options.UseNpgsql(connectionString));
```

Sem retry nenhum.

### O problema

Alguns erros **não são bugs** e resolvem tentando de novo:
- **Conexão caiu** no meio da requisição (rede, failover do RDS na AWS).
- **Deadlock detectado** (`40P01`). O item 3 previne o caso comum, mas o PostgreSQL pode matar uma transação por outros motivos.
- **Falha de serialização** (`40001`), se um dia você usar `SERIALIZABLE`.

Hoje todos esses casos viram 500 para o cliente.

### Como resolver

**Passo 1** — ligar o retry do Npgsql em `PedeAqui.Infrastructure/DependencyInjectionExtension.cs`:

```csharp
services.AddDbContext<PedeAquiDbContext>(options => options.UseNpgsql(connectionString, npgsql =>
    npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(2), errorCodesToAdd: null)));
```

Para `SaveChanges` simples (o `Commit()`), isso basta: o EF repete sozinho.

**Passo 2 — a pegadinha.** Com o retry ligado, o EF **recusa** transações abertas manualmente (`BeginTransactionAsync`) e lança `InvalidOperationException`. O motivo: se ele repetisse só o último comando, as instruções anteriores da transação estariam perdidas. A transação inteira precisa estar dentro de uma *execution strategy*, que repete **o bloco todo**.

A versão final do `UnitOfWork`:

```csharp
internal class UnitOfWork : IUnitOfWork
{
    private readonly PedeAquiDbContext _dbContext;

    public UnitOfWork(PedeAquiDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task Commit()
    {
        await SaveChanges();
    }

    public Task<T> ExecuteInTransaction<T>(Func<Task<T>> operation)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();

            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            var result = await operation();

            await SaveChanges();
            await transaction.CommitAsync();

            return result;
        });
    }

    public Task ExecuteInTransaction(Func<Task> operation) =>
        ExecuteInTransaction(async () =>
        {
            await operation();
            return true;
        });

    private async Task SaveChanges()
    {
        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(ResourceErrorMessages.CONCURRENCY_CONFLICT);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres)
        {
            throw TranslateUniqueViolation(postgres.ConstraintName);
        }
    }

    private static PedeAquiException TranslateUniqueViolation(string? constraintName) => constraintName switch
    {
        "ux_users_email" => new ErrorOnValidationException([ResourceErrorMessages.EMAIL_ALREADY_REGISTERED]),
        "ux_products_sku" => new ErrorOnValidationException([ResourceErrorMessages.PRODUCT_SKU_ALREADY_EXISTS]),
        _ => new ConflictException(ResourceErrorMessages.CONCURRENCY_CONFLICT)
    };
}
```

Pontos importantes:
- **`ChangeTracker.Clear()` no início de cada tentativa.** Se a primeira tentativa falhou depois do `RemoveStock`, a entidade em memória já está com o estoque baixado. Sem limpar, a segunda tentativa baixaria de novo. Limpando, o lambda relê tudo do banco.
- **O lambda precisa poder rodar mais de uma vez.** Por isso tudo que lê e escreve fica **dentro** dele. Nada de `new Order()` fora do lambda que dependa de estado já alterado, e nada de efeito externo (e-mail, fila) dentro dele.
- **`ConflictException` e as exceções de negócio não são repetidas.** O retry só atua em erros transitórios do Npgsql. 409, 422 e 404 sobem direto para o cliente, como deve ser.
- **O InMemory dos testes funciona normalmente.** Ele usa uma estratégia que não repete nada.

### Como provar

Este é o item mais difícil de testar automaticamente, porque exige derrubar a conexão no meio da operação. Duas opções:
- **Manual:** coloque um breakpoint dentro do lambda, rode `docker restart pedeaqui-postgres` e solte o breakpoint. A requisição deve completar depois de reconectar.
- **Automatizado (opcional):** use o **Toxiproxy** (container que simula falhas de rede) entre a API e o PostgreSQL no Testcontainers.

---

## 10. Checklist final

Ordem de implementação, cada linha com o seu "pronto quando":

- [ ] **1. Testcontainers.** `StockConcurrencyTests` roda e **falha** com o código atual.
- [ ] **2. Transação explícita.** `IUnitOfWork.ExecuteInTransaction` existe; fakes e factory InMemory foram ajustados; `dotnet test` continua verde.
- [ ] **3. `FOR UPDATE` ordenado.** `GetByIdsForUpdate` é usado em `Create`, `Update`, `Cancel`, `Entry` e `Exit`. O teste de 50 compradores × 10 unidades passa **10 vezes seguidas**.
- [ ] **4. CHECK constraint.** A migration `StockNonNegative` foi aplicada; um `UPDATE` manual com estoque negativo falha com `23514`.
- [ ] **5. `ConflictException`.** 409 em ProblemDetails, traduzido no `UnitOfWork`.
- [ ] **6. Versão no pedido.** Dois cancelamentos simultâneos resultam em um `204`, um `409`/`422` e o estoque devolvido **uma** vez.
- [ ] **7. Produto.** Sem `Update()` em entidade rastreada, com `Version` no produto. PUT de preço e venda simultâneos não perdem estoque.
- [ ] **8. Índice único.** 10 cadastros simultâneos com o mesmo e-mail resultam em um `201`, nove `400` e zero `500`.
- [ ] **9. Retry.** `EnableRetryOnFailure` + `ExecutionStrategy` + `ChangeTracker.Clear()`. A API sobrevive a um `docker restart` do PostgreSQL durante uma requisição.
- [ ] **README principal**: marcar os itens da seção "Consistência, ACID e concorrência".

### Resumo de onde cada peça mora

| Peça | Camada | Arquivo |
|---|---|---|
| `ExecuteInTransaction` (contrato) | Domain | `Repositories/IUnitOfWork.cs` |
| `ExecuteInTransaction` + tradução de erros + retry | Infrastructure | `Database/UnitOfWork.cs` |
| `GetByIdsForUpdate` (contrato) | Domain | `Repositories/Products/IProductUpdateOnlyRepository.cs` |
| `GetByIdsForUpdate` (SQL `FOR UPDATE`) | Infrastructure | `Database/Repositories/ProductRepository.cs` |
| `Version` (`xmin`) | Domain + Infrastructure | `Entities/Order.cs`, `Entities/Product.cs`, `Configurations/*` |
| CHECK constraint e nomes dos índices | Infrastructure | `Configurations/ProductConfiguration.cs`, `UserConfiguration.cs` + migrations |
| `ConflictException` | Exception | `ExceptionsBase/ConflictException.cs` |
| Casos de uso dentro da transação | Application | `UseCases/Orders/*`, `UseCases/Stock/*`, `UseCases/Products/Update` |
| Retry | Infrastructure | `DependencyInjectionExtension.cs` |
| Provas | Testes | `tests/PedeAqui.IntegrationTests/Concurrency/*` |

Repare que **a regra de negócio não saiu do domínio**. `RemoveStock`, `Cancel` e `EnsureIsPending` continuam iguais. O que mudou foi **como** a Application e a Infrastructure garantem que o domínio decide com dados atuais e que ninguém grava por cima de ninguém.
