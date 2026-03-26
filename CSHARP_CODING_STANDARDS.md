# 🏆 C# Elite Coding Standards
### Synthesized from Google, NASA, SEI CERT, Microsoft, MISRA & Clean Code Principles

> **Purpose:** This document defines what a *great* C# project looks like — from naming conventions  
> to zero-cyclomatic-complexity design. Every rule is sourced from a recognized industry standard.

---

## 📚 Table of Contents

1. [Standards Reference Map](#1-standards-reference-map)
2. [Project Structure — The Gold Template](#2-project-structure--the-gold-template)
3. [Naming Conventions](#3-naming-conventions)
4. [Cyclomatic Complexity — The Core Enemy](#4-cyclomatic-complexity--the-core-enemy)
5. [Methods & Functions](#5-methods--functions)
6. [Classes & SOLID Principles](#6-classes--solid-principles)
7. [Error Handling](#7-error-handling)
8. [Memory & Resource Safety (NASA-Inspired)](#8-memory--resource-safety-nasa-inspired)
9. [Concurrency & Thread Safety](#9-concurrency--thread-safety)
10. [Security Coding (SEI CERT)](#10-security-coding-sei-cert)
11. [Comments & Documentation](#11-comments--documentation)
12. [Testing Standards](#12-testing-standards)
13. [Code Metrics Thresholds](#13-code-metrics-thresholds)
14. [Tooling & Enforcement](#14-tooling--enforcement)
15. [Complete Example — Zero-Complexity Service](#15-complete-example--zero-complexity-service)

---

## 1. Standards Reference Map

| Standard | Origin | Key Contribution to This Document |
|---|---|---|
| **Google C++ / Java Style Guide** | Google LLC | Naming, file layout, comment style, simplicity principle |
| **NASA JPL C Coding Standard** | Jet Propulsion Laboratory | Bounded loops, no dynamic allocation in critical paths, assertion density |
| **SEI CERT C# Coding Standard** | Carnegie Mellon SEI | Security, input validation, exception safety, thread safety |
| **MISRA C/C++** | Motor Industry Software Reliability Assoc. | Cyclomatic complexity ≤ 10, no recursion, no implicit type conversion |
| **Microsoft .NET Guidelines** | Microsoft | C# naming, async patterns, IDisposable, nullable reference types |
| **Clean Code** | Robert C. Martin | Single responsibility, small functions, no comments for bad code |
| **ISO/IEC 25010** | International Standards Org | Software quality model: maintainability, reliability, performance efficiency |

---

## 2. Project Structure — The Gold Template

```
MyCompany.MyProduct/
│
├── src/
│   ├── MyCompany.MyProduct.Domain/          # Entities, value objects, domain events
│   │   ├── Entities/
│   │   ├── ValueObjects/
│   │   ├── Events/
│   │   ├── Exceptions/
│   │   └── Interfaces/                      # Repository & service contracts (abstractions)
│   │
│   ├── MyCompany.MyProduct.Application/     # Use cases, commands, queries (CQRS)
│   │   ├── Commands/
│   │   ├── Queries/
│   │   ├── Handlers/
│   │   ├── DTOs/
│   │   └── Validators/
│   │
│   ├── MyCompany.MyProduct.Infrastructure/  # DB, external APIs, file system
│   │   ├── Persistence/
│   │   ├── Repositories/
│   │   ├── ExternalServices/
│   │   └── Configuration/
│   │
│   └── MyCompany.MyProduct.Api/             # Controllers, middleware, startup
│       ├── Controllers/
│       ├── Middleware/
│       ├── Filters/
│       └── Program.cs
│
├── tests/
│   ├── MyCompany.MyProduct.Domain.Tests/
│   ├── MyCompany.MyProduct.Application.Tests/
│   ├── MyCompany.MyProduct.Infrastructure.Tests/
│   └── MyCompany.MyProduct.Api.IntegrationTests/
│
├── docs/
│   ├── architecture-decision-records/       # ADRs — why we made each design choice
│   └── api/
│
├── .editorconfig                            # Enforces formatting rules in every IDE
├── .globalconfig                            # Analyzer severity rules
├── Directory.Build.props                    # Shared MSBuild properties
├── Directory.Packages.props                 # Centralized NuGet version management
└── README.md
```

### 📐 Why This Structure?
- **Google Principle:** One purpose per directory. No mixed concerns.
- **Clean Architecture:** Dependencies point inward. Domain knows nothing about Infrastructure.
- **NASA Principle:** Every module has a single, verifiable contract (the Interface).

---

## 3. Naming Conventions

### 3.1 General Rules (Microsoft + Google)

| Element | Convention | Example |
|---|---|---|
| Namespace | PascalCase | `MyCompany.Payments.Domain` |
| Class | PascalCase | `PaymentProcessor` |
| Interface | IPascalCase | `IPaymentGateway` |
| Record | PascalCase | `PaymentCreatedEvent` |
| Enum | PascalCase (singular) | `PaymentStatus` |
| Enum Value | PascalCase | `PaymentStatus.Completed` |
| Public Method | PascalCase | `ProcessPaymentAsync` |
| Private Method | PascalCase | `ValidateAmount` |
| Public Property | PascalCase | `TotalAmount` |
| Private Field | _camelCase (underscore) | `_paymentRepository` |
| Local Variable | camelCase | `orderTotal` |
| Parameter | camelCase | `paymentRequest` |
| Constant | PascalCase | `MaxRetryAttempts` |
| Generic Type | T or TDescriptive | `TEntity`, `TResult` |
| Async Method | Suffix "Async" | `GetOrderAsync` |
| Boolean | Prefix "is/has/can/should" | `isValid`, `hasPermission` |

### 3.2 Naming Principle: Intent Over Cleverness
```cs
// ❌ BAD — Google Style Guide violation: cryptic, saves no meaningful time
int d;
var x = GetD(userId, d);

// ✅ GOOD — Name reveals intent completely
int daysSinceLastLogin;
var loginActivity = GetDaysSinceLastLogin(userId, out daysSinceLastLogin);
```

### 3.3 No Abbreviations (Google Rule)
```cs
// ❌ BAD
var usrAcct = GetUsrAcct(usrId);

// ✅ GOOD
var userAccount = GetUserAccount(userId);

// Exception: universally understood abbreviations
// OK: Id, Url, Http, Api, Dto, Db, IO, Ok
```

---

## 4. Cyclomatic Complexity — The Core Enemy

### What Is Cyclomatic Complexity?

Cyclomatic Complexity (CC) = **number of linearly independent paths** through a method.  
Each `if`, `else if`, `case`, `for`, `while`, `foreach`, `&&`, `||`, `??`, `?:` adds +1.

| CC Score | Risk Level | Standard |
|---|---|---|
| 1–4 | Low — Simple, easy to test | ✅ Ideal |
| 5–10 | Moderate — Acceptable with good tests | ⚠️ MISRA limit |
| 11–20 | High — Refactor strongly recommended | 🔴 NASA: never in safety-critical |
| 21+ | Critical — Must refactor before release | ❌ All standards forbid |

### 4.1 The Problem (High Complexity)
```cs
// ❌ CC = 11 — 11 independent paths, nearly impossible to fully test
public decimal CalculateDiscount(Order order, User user, bool isBlackFriday)
{
    decimal discount = 0;

    if (user.IsPremium)
    {
        if (order.Total > 100)
        {
            discount += 10;
        }
        else if (order.Total > 50)
        {
            discount += 5;
        }

        if (isBlackFriday)
        {
            discount += 15;
            if (user.LoyaltyYears > 3)
                discount += 5;
        }
    }
    else
    {
        if (isBlackFriday)
            discount += 5;

        if (order.Items.Count > 10)
            discount += 3;
    }

    if (order.HasCoupon && order.Coupon.IsValid())
        discount += order.Coupon.Value;

    return discount;
}
```

### 4.2 The Solution — Strategy + Guard Clauses + Decomposition
```cs
// ✅ CC = 1 per method — Every path is independently testable

public decimal CalculateDiscount(Order order, User user, bool isBlackFriday)
{
    // Compose discount from isolated, pure calculators
    return new DiscountCalculator()
        .Add(new PremiumUserDiscount(user, order))
        .Add(new BlackFridayDiscount(user, isBlackFriday))
        .Add(new BulkOrderDiscount(order))
        .Add(new CouponDiscount(order))
        .Calculate();
}

// Each rule is a separate, independently testable class
public sealed class PremiumUserDiscount : IDiscountRule
{
    private readonly User _user;
    private readonly Order _order;

    public PremiumUserDiscount(User user, Order order)
    {
        _user = user;
        _order = order;
    }

    public decimal Apply() // CC = 1
    {
        if (!_user.IsPremium) return 0;
        return _order.Total switch
        {
            > 100 => 10,
            > 50  => 5,
            _     => 0
        };
    }
}

public sealed class BlackFridayDiscount : IDiscountRule
{
    private readonly User _user;
    private readonly bool _isBlackFriday;

    public BlackFridayDiscount(User user, bool isBlackFriday)
    {
        _user = user;
        _isBlackFriday = isBlackFriday;
    }

    public decimal Apply() // CC = 1
    {
        if (!_isBlackFriday) return 0;
        return _user.IsPremium ? GetPremiumBlackFridayDiscount() : 5;
    }

    private decimal GetPremiumBlackFridayDiscount() => // CC = 1
        _user.LoyaltyYears > 3 ? 20 : 15;
}
```

### 4.3 Techniques to Eliminate Complexity

#### Technique 1: Guard Clauses (Early Return)
```cs
// ❌ DEEPLY NESTED — CC grows with every indent level
public Result ProcessOrder(Order order)
{
    if (order != null)
    {
        if (order.IsValid())
        {
            if (order.HasStock())
            {
                // ... actual logic buried 3 levels deep
            }
        }
    }
    return Result.Failure("Invalid state");
}

// ✅ GUARD CLAUSES — Fail fast, no nesting, CC stays at 1+guards
public Result ProcessOrder(Order order)
{
    if (order is null)         return Result.Failure("Order cannot be null");
    if (!order.IsValid())      return Result.Failure("Order is invalid");
    if (!order.HasStock())     return Result.Failure("Insufficient stock");

    // Actual logic at top level — clean and readable
    return ExecuteOrder(order);
}
```

#### Technique 2: Replace Conditionals with Polymorphism
```cs
// ❌ Switch-based type dispatch — adds CC with every new type
public decimal CalculateTax(Product product)
{
    switch (product.Type)
    {
        case ProductType.Food:     return product.Price * 0.0m;
        case ProductType.Clothing: return product.Price * 0.05m;
        case ProductType.Luxury:   return product.Price * 0.20m;
        default:                   return product.Price * 0.10m;
    }
}

// ✅ POLYMORPHISM — Open/Closed Principle, zero CC per method
public interface ITaxStrategy
{
    decimal Calculate(decimal price);
}

public sealed class FoodTax     : ITaxStrategy { public decimal Calculate(decimal p) => 0m; }
public sealed class ClothingTax : ITaxStrategy { public decimal Calculate(decimal p) => p * 0.05m; }
public sealed class LuxuryTax   : ITaxStrategy { public decimal Calculate(decimal p) => p * 0.20m; }

// Registration in DI container maps ProductType → ITaxStrategy
```

#### Technique 3: Pattern Matching with Switch Expressions
```cs
// ✅ Switch expressions don't add CC the same way — and are exhaustive
public string DescribeOrder(Order order) => order.Status switch
{
    OrderStatus.Pending    => "Awaiting payment",
    OrderStatus.Paid       => "Processing your order",
    OrderStatus.Shipped    => $"Shipped on {order.ShippedAt:d}",
    OrderStatus.Delivered  => "Delivered — enjoy!",
    OrderStatus.Cancelled  => "Order was cancelled",
    _                      => throw new UnreachableException($"Unknown status: {order.Status}")
};
```

#### Technique 4: Replace Loops with LINQ
```cs
// ❌ Loop adds CC and is harder to read
public List<OrderDto> GetPaidOrders(List<Order> orders)
{
    var result = new List<OrderDto>();
    foreach (var order in orders)
    {
        if (order.Status == OrderStatus.Paid)
        {
            result.Add(new OrderDto(order.Id, order.Total));
        }
    }
    return result;
}

// ✅ LINQ — declarative, zero extra CC, pure transformation
public IReadOnlyList<OrderDto> GetPaidOrders(IEnumerable<Order> orders) =>
    orders
        .Where(o => o.Status == OrderStatus.Paid)
        .Select(o => new OrderDto(o.Id, o.Total))
        .ToList()
        .AsReadOnly();
```

---

## 5. Methods & Functions

### 5.1 The 20-Line Rule (Clean Code + Google)
> "A function should do ONE thing. It should do it WELL. It should do it ONLY." — Robert C. Martin

- **Hard limit:** Methods should rarely exceed 20 lines.
- **NASA Rule:** Every function has a single, documented entry point and exit contract.
- **Google Rule:** If you need to scroll to see the whole function, it's too long.

### 5.2 Method Design Checklist
```cs
// ✅ Every great method satisfies ALL of these:

// 1. Single Responsibility: Does exactly one thing
// 2. Descriptive name: Name tells you WHAT it does, not HOW
// 3. Small: Fits on one screen (≤ 20 lines)
// 4. ≤ 3 parameters (use a parameter object if more needed)
// 5. No side effects (pure functions where possible)
// 6. Returns a value OR mutates state — never both
// 7. Fails fast with guard clauses
// 8. No boolean trap parameters

// ❌ BAD — Boolean trap: what does 'true' mean here?
SendEmail(user, true);

// ✅ GOOD — Named parameter makes intent obvious
SendEmail(user, includeAttachments: true);

// ✅ EVEN BETTER — Separate methods
SendEmailWithAttachments(user);
SendEmailWithoutAttachments(user);
```

### 5.3 Parameter Objects
```cs
// ❌ BAD — Too many parameters, caller must remember order
public void CreateOrder(string customerId, decimal amount, string currency,
                         string shippingAddress, bool isPriority, DateTime dueDate) { }

// ✅ GOOD — Encapsulate into a record/value object
public record CreateOrderRequest(
    string CustomerId,
    decimal Amount,
    string Currency,
    string ShippingAddress,
    bool IsPriority,
    DateTime DueDate);

public void CreateOrder(CreateOrderRequest request) { }
```

---

## 6. Classes & SOLID Principles

### 6.1 Single Responsibility Principle (SRP)
```cs
// ❌ BAD — One class doing 3 jobs
public class OrderService
{
    public void ProcessOrder(Order order) { /* business logic */ }
    public void SaveToDatabase(Order order) { /* persistence */ }
    public void SendConfirmationEmail(Order order) { /* communication */ }
}

// ✅ GOOD — Each class owns one reason to change
public class OrderProcessor   { public Result Process(Order order) { } }
public class OrderRepository  { public Task SaveAsync(Order order) { } }
public class OrderNotifier    { public Task NotifyAsync(Order order) { } }
```

### 6.2 Open/Closed Principle (OCP)
```cs
// ✅ Open for extension (add new IDiscountRule),
//    Closed for modification (existing rules never change)
public interface IDiscountRule
{
    decimal Apply(Order order);
}

public sealed class DiscountCalculator
{
    private readonly IEnumerable<IDiscountRule> _rules;

    public DiscountCalculator(IEnumerable<IDiscountRule> rules)
        => _rules = rules;

    public decimal Calculate(Order order) =>                  // CC = 1
        _rules.Sum(rule => rule.Apply(order));
}
```

### 6.3 Liskov Substitution Principle (LSP)
```cs
// ✅ Any IPaymentGateway implementation must honor the contract:
// - Never throw on valid input
// - Return a meaningful Result, not throw for business failures
// - Postconditions never weaker than base contract
public interface IPaymentGateway
{
    Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken ct);
}
```

### 6.4 Interface Segregation Principle (ISP)
```cs
// ❌ BAD — Forces implementors to implement methods they don't need
public interface IRepository<T>
{
    Task<T> GetByIdAsync(Guid id);
    Task SaveAsync(T entity);
    Task DeleteAsync(Guid id);
    Task<IReadOnlyList<T>> SearchAsync(string query);  // Not all repos need search
    Task<int> CountAsync();
}

// ✅ GOOD — Compose what you need
public interface IReadRepository<T>  { Task<T?> GetByIdAsync(Guid id); }
public interface IWriteRepository<T> { Task SaveAsync(T entity); Task DeleteAsync(Guid id); }
public interface ISearchRepository<T>{ Task<IReadOnlyList<T>> SearchAsync(string query); }
```

### 6.5 Dependency Inversion Principle (DIP)
```cs
// ✅ High-level module depends on abstraction, not concrete class
public sealed class OrderService
{
    private readonly IOrderRepository _repository;   // ← abstraction
    private readonly IPaymentGateway _gateway;       // ← abstraction
    private readonly IOrderNotifier _notifier;       // ← abstraction

    public OrderService(
        IOrderRepository repository,
        IPaymentGateway gateway,
        IOrderNotifier notifier)
    {
        _repository = repository;
        _gateway = gateway;
        _notifier = notifier;
    }
}
```

---

## 7. Error Handling

### 7.1 Result Pattern over Exceptions (NASA + SEI CERT)
> NASA Rule: Exceptions for exceptional conditions only. Business failures are not exceptions.

```cs
// ✅ Result<T> pattern — errors are values, not thrown
public readonly record struct Result<T>
{
    public T? Value { get; init; }
    public string? Error { get; init; }
    public bool IsSuccess => Error is null;

    public static Result<T> Success(T value)   => new() { Value = value };
    public static Result<T> Failure(string err) => new() { Error = err };
}

// Usage — callers are forced to handle the failure case
var result = await _orderService.CreateOrderAsync(request);
if (!result.IsSuccess)
{
    logger.LogWarning("Order creation failed: {Error}", result.Error);
    return BadRequest(result.Error);
}
```

### 7.2 Exception Hierarchy (SEI CERT)
```cs
// ✅ Custom domain exceptions carry context
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
    protected DomainException(string message, Exception inner) : base(message, inner) { }
}

public sealed class OrderNotFoundException(Guid orderId)
    : DomainException($"Order {orderId} was not found.");

public sealed class InsufficientStockException(string productSku, int requested, int available)
    : DomainException($"Product {productSku}: requested {requested}, available {available}.");

// ✅ NEVER catch Exception blindly
// ❌ catch (Exception) { /* swallows everything */ }

// ✅ Catch what you can handle; let the rest propagate
try
{
    await _repository.SaveAsync(order);
}
catch (DbUpdateConcurrencyException ex)
{
    throw new OrderConcurrencyException(order.Id, ex);
}
// All other exceptions propagate to the global error handler
```

### 7.3 Global Exception Handler (ASP.NET Core)
```cs
// ✅ One place handles all unhandled exceptions — no scattered try/catch
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
        => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken ct)
    {
        var (statusCode, title) = exception switch
        {
            DomainException     => (StatusCodes.Status400BadRequest, "Domain Error"),
            NotFoundException   => (StatusCodes.Status404NotFound, "Not Found"),
            UnauthorizedException => (StatusCodes.Status403Forbidden, "Forbidden"),
            _                   => (StatusCodes.Status500InternalServerError, "Server Error")
        };

        _logger.LogError(exception, "Unhandled exception: {Title}", title);

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(
            new ProblemDetails { Title = title, Status = statusCode }, ct);

        return true;
    }
}
```

---

## 8. Memory & Resource Safety (NASA-Inspired)

### 8.1 NASA Power-of-Ten Rules (adapted for C#)

| Rule | C# Implementation |
|---|---|
| 1. No recursion | Use iterative algorithms. If recursion is needed, add explicit depth limit. |
| 2. Fixed upper bound on all loops | Use `.Take(MaxResults)` on all LINQ queries; limit all external data. |
| 3. No dynamic memory in critical paths | Pool objects with `ArrayPool<T>` and `ObjectPool<T>`. |
| 4. Functions ≤ 60 lines | Enforced by Roslyn analyzer. |
| 5. ≥ 2 assertions per function | Use `Debug.Assert` + argument guards. |
| 6. Minimal scope for all data | Declare variables as late as possible, `using` for all IDisposables. |
| 7. Check return values | Never ignore `Task` without reason; never ignore `Result<T>`. |

### 8.2 Dispose Pattern
```cs
// ✅ Always use 'using' — never rely on finalizer
await using var connection = await _connectionFactory.CreateAsync(ct);
await using var transaction = await connection.BeginTransactionAsync(ct);

try
{
    await _repository.SaveAsync(order, transaction, ct);
    await transaction.CommitAsync(ct);
}
catch
{
    await transaction.RollbackAsync(ct);
    throw;
}

// ✅ Implement IAsyncDisposable on your own resource-owning types
public sealed class ResourceOwner : IAsyncDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _semaphore.Dispose();
    }
}
```

### 8.3 Span<T> and Memory<T> — Zero-Allocation Patterns
```cs
// ✅ Use Span<T> for high-performance parsing — no heap allocation
public static bool TryParseOrderId(ReadOnlySpan<char> input, out Guid orderId)
{
    orderId = Guid.Empty;
    if (input.Length != 36) return false;
    return Guid.TryParse(input, out orderId);
}
```

---

## 9. Concurrency & Thread Safety

### 9.1 Async/Await Best Practices (Microsoft)
```cs
// ✅ RULE 1: Async all the way — never block on async code
// ❌ NEVER: var result = GetDataAsync().Result;      — deadlock risk
// ❌ NEVER: var result = GetDataAsync().GetAwaiter().GetResult();
// ✅ ALWAYS: var result = await GetDataAsync();

// ✅ RULE 2: ConfigureAwait(false) in library code
public async Task<Order> GetOrderAsync(Guid id, CancellationToken ct)
{
    var entity = await _dbContext.Orders
        .FirstOrDefaultAsync(o => o.Id == id, ct)
        .ConfigureAwait(false);                     // ← always in lib code

    return entity ?? throw new OrderNotFoundException(id);
}

// ✅ RULE 3: Always accept CancellationToken in async public APIs
public Task<Result<Order>> CreateOrderAsync(
    CreateOrderRequest request,
    CancellationToken ct = default);

// ✅ RULE 4: Never use async void (except event handlers)
// ❌ public async void ProcessOrder() { }   — exceptions silently swallowed
// ✅ public async Task ProcessOrderAsync() { }
```

### 9.2 Thread-Safe State
```cs
// ✅ Prefer immutable records for shared state
public record OrderSnapshot(Guid Id, decimal Total, OrderStatus Status);

// ✅ Use Interlocked for atomic counter operations
private long _processedCount;
public void IncrementProcessed() => Interlocked.Increment(ref _processedCount);

// ✅ Use SemaphoreSlim for async-compatible locking
private readonly SemaphoreSlim _lock = new(1, 1);
public async Task<T> ExecuteExclusivelyAsync<T>(Func<Task<T>> operation, CancellationToken ct)
{
    await _lock.WaitAsync(ct);
    try   { return await operation(); }
    finally { _lock.Release(); }
}
```

---

## 10. Security Coding (SEI CERT)

### 10.1 Input Validation — Never Trust Input
```cs
// ✅ Validate ALL external input at the boundary (FluentValidation)
public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    private const int MaxItemCount = 1_000;  // NASA: bounded inputs

    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty()
            .MaximumLength(50)
            .Matches(@"^[a-zA-Z0-9\-]+$");  // Allowlist, not denylist

        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .LessThanOrEqualTo(1_000_000);

        RuleFor(x => x.Items)
            .NotEmpty()
            .Must(items => items.Count <= MaxItemCount)
            .WithMessage($"Cannot exceed {MaxItemCount} items per order.");
    }
}
```

### 10.2 SQL Injection Prevention
```cs
// ❌ CRITICAL VULNERABILITY — Never concatenate SQL
var sql = $"SELECT * FROM Orders WHERE CustomerId = '{customerId}'";

// ✅ ALWAYS use parameterized queries or ORM
var order = await _context.Orders
    .Where(o => o.CustomerId == customerId)  // EF Core — safe by default
    .FirstOrDefaultAsync(ct);

// ✅ Raw SQL — must use parameters
var orders = await _context.Orders
    .FromSqlRaw("SELECT * FROM Orders WHERE CustomerId = {0}", customerId)
    .ToListAsync(ct);
```

### 10.3 Sensitive Data
```cs
// ✅ Never log sensitive data (SEI CERT rule IDS04)
// ❌ _logger.LogInformation("Processing card: {CardNumber}", request.CardNumber);
// ✅ Log only non-sensitive identifiers
_logger.LogInformation("Processing payment for order: {OrderId}", request.OrderId);

// ✅ Mask sensitive fields in ToString / records
public record PaymentRequest
{
    public string CardNumber { get; init; } = "";
    public override string ToString() =>
        $"PaymentRequest {{ CardNumber = ****-****-****-{CardNumber[^4..]} }}";
}

// ✅ Use SecureString or byte[] for passwords in memory; clear after use
```

### 10.4 Nullable Reference Types — Enable Everywhere
```cs
// In .csproj — non-negotiable
// <Nullable>enable</Nullable>
// <TreatWarningsAsErrors>true</TreatWarningsAsErrors>

// ✅ Nullable annotations make null-contract explicit
public sealed class OrderService
{
    // Returns null if not found — explicit in the type
    public async Task<Order?> FindOrderAsync(Guid id, CancellationToken ct) { }

    // Never returns null — explicit guarantee
    public async Task<IReadOnlyList<Order>> GetAllOrdersAsync(CancellationToken ct) { }
}
```

---

## 11. Comments & Documentation

### 11.1 The Comment Hierarchy (Clean Code + Google)

> **Rule:** Good code is self-documenting. A comment that explains *what* is a sign the code needs renaming. A comment that explains *why* is gold.

```cs
// ❌ USELESS — Restates the code
// Increment counter
counter++;

// ❌ LYING COMMENT — Code changed but comment didn't (worst kind)
// Returns the user's name
public string GetUserEmail() { }

// ✅ WHY comment — explains non-obvious business rule
// Orders from EU customers require tax-inclusive pricing per VAT Directive 2006/112/EC
if (customer.Region == Region.EuropeanUnion)
    ApplyVatInclusive(order);

// ✅ TODO with ticket reference — never anonymous TODOs
// TODO(PROJ-1234): Replace with distributed cache when Redis is provisioned
private readonly Dictionary<Guid, Order> _orderCache = new();
```

### 11.2 XML Documentation (for public APIs)
```cs
/// <summary>
/// Processes a payment for an order.
/// Returns a failure result if the payment gateway declines — does NOT throw.
/// </summary>
/// <param name="request">The payment details. CardNumber must be 16 digits.</param>
/// <param name="ct">Cancellation token for async operation.</param>
/// <returns>
/// Success result containing the transaction ID, or a Failure result
/// with the decline reason.
/// </returns>
/// <exception cref="ArgumentNullException">Thrown if <paramref name="request"/> is null.</exception>
public async Task<Result<string>> ProcessPaymentAsync(
    PaymentRequest request,
    CancellationToken ct = default)
```

---

## 12. Testing Standards

### 12.1 Test Naming — Arrange, Act, Assert
```cs
// ✅ Test name format: MethodName_Scenario_ExpectedBehavior
public sealed class OrderServiceTests
{
    [Fact]
    public async Task CreateOrderAsync_WhenStockIsInsufficient_ReturnsFailureResult()
    {
        // Arrange
        var request = new CreateOrderRequest(CustomerId: "C001", Amount: 100m, Items: 5);
        _stockService.Setup(s => s.HasStockAsync(request, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(false);

        // Act
        var result = await _sut.CreateOrderAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Insufficient stock");
    }
}
```

### 12.2 Coverage & Quality Targets

| Metric | Minimum Target | Ideal Target |
|---|---|---|
| Line Coverage | 80% | 90%+ |
| Branch Coverage | 75% | 85%+ |
| Domain Layer Coverage | 95% | 100% |
| Mutation Score | 60% | 80%+ |
| Tests per Feature | 1 happy + 2 edge cases | + boundary tests |

### 12.3 Test Structure Rules
- **One assertion concept per test** (can have multiple `.Should()` for the same concept)
- **No logic in tests** — no `if`, `for`, or `switch` in test code
- **Tests must be deterministic** — no `DateTime.Now`, use a clock abstraction
- **Tests must be independent** — no shared mutable state between tests
- **Name the subject under test `_sut`** — makes refactoring trivial

---

## 13. Code Metrics Thresholds

These must be enforced by your CI pipeline. Builds fail if any metric exceeds its threshold.

| Metric | Hard Limit | Tool |
|---|---|---|
| Cyclomatic Complexity | ≤ 10 per method | Roslyn / NDepend |
| Cognitive Complexity | ≤ 15 per method | SonarQube |
| Method Length | ≤ 40 lines | EditorConfig + Analyzer |
| Class Length | ≤ 300 lines | Roslyn Analyzer |
| Parameters per Method | ≤ 4 | Roslyn Analyzer |
| Inheritance Depth | ≤ 3 | NDepend |
| Coupling Between Objects | ≤ 7 | NDepend |
| Maintainability Index | ≥ 65 | Visual Studio Metrics |
| Code Duplication | ≤ 3% | SonarQube |
| Test Coverage (Domain) | ≥ 90% | Coverlet |

---

## 14. Tooling & Enforcement

### 14.1 .editorconfig (Root)
```ini
root = true

[*.cs]
indent_style = space
indent_size = 4
end_of_line = lf
charset = utf-8
trim_trailing_whitespace = true
insert_final_newline = true

# Naming rules
dotnet_naming_rule.private_fields_should_be_camel_case.symbols = private_fields
dotnet_naming_rule.private_fields_should_be_camel_case.style = camel_case_underscore
dotnet_naming_rule.private_fields_should_be_camel_case.severity = error

dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private

dotnet_naming_style.camel_case_underscore.capitalization = camel_case
dotnet_naming_style.camel_case_underscore.required_prefix = _

# Prefer expression body for simple members
csharp_style_expression_bodied_methods = when_on_single_line
csharp_style_expression_bodied_properties = true

# Prefer pattern matching
csharp_style_prefer_pattern_matching = true:error
csharp_style_prefer_switch_expression = true:error
```

### 14.2 Directory.Build.props
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <WarningsAsErrors />
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisMode>All</AnalysisMode>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <!-- Roslyn analyzers — enforces all rules at compile time -->
    <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="*" PrivateAssets="all" />
    <PackageReference Include="SonarAnalyzer.CSharp" Version="*" PrivateAssets="all" />
    <PackageReference Include="Roslynator.Analyzers" Version="*" PrivateAssets="all" />
    <PackageReference Include="StyleCop.Analyzers" Version="*" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

### 14.3 CI Pipeline Gates (GitHub Actions / Azure DevOps)
```yaml
# Every PR must pass all of these gates before merge is allowed:
quality-gate:
  steps:
    - name: Build (warnings as errors)
      run: dotnet build --configuration Release

    - name: Run Tests
      run: dotnet test --collect:"XPlat Code Coverage"

    - name: Coverage Gate (fail if < 80%)
      run: |
        dotnet tool run dotnet-coverage merge
        dotnet tool run reportgenerator
        # Fail build if coverage < 80%

    - name: SonarQube Analysis
      # Fails on: CC > 10, duplication > 3%, blocker/critical issues

    - name: Security Scan
      run: dotnet tool run security-scan   # Snyk / OWASP dependency check

    - name: Architecture Tests
      run: dotnet test --filter Category=Architecture
      # Enforces: Domain has no Infrastructure dependency, etc.
```

---

## 15. Complete Example — Zero-Complexity Service

This is what a production-grade, zero-cyclomatic-complexity feature looks like end-to-end.

```cs
// ============================================================
// DOMAIN LAYER
// ============================================================

// Value Object — immutable, self-validating
public sealed record Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Result<Money> Create(decimal amount, string currency) =>
        amount <= 0        ? Result<Money>.Failure("Amount must be positive") :
        string.IsNullOrWhiteSpace(currency) ? Result<Money>.Failure("Currency required") :
        Result<Money>.Success(new Money(amount, currency));

    public Money Add(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException("Cannot add different currencies");
        return new Money(Amount + other.Amount, Currency);
    }

    public override string ToString() => $"{Amount:F2} {Currency}";
}

// Entity — encapsulates its own invariants
public sealed class Order
{
    private readonly List<OrderLine> _lines = new();

    public Guid Id { get; } = Guid.NewGuid();
    public string CustomerId { get; }
    public OrderStatus Status { get; private set; } = OrderStatus.Pending;
    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();

    public Money Total => _lines.Aggregate(
        Money.Create(0, "USD").Value!,
        (acc, line) => acc.Add(line.Subtotal));

    private Order(string customerId) => CustomerId = customerId;

    public static Result<Order> Create(string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            return Result<Order>.Failure("CustomerId is required");
        return Result<Order>.Success(new Order(customerId));
    }

    public Result AddLine(OrderLine line)
    {
        if (Status != OrderStatus.Pending)
            return Result.Failure("Cannot modify a non-pending order");

        _lines.Add(line);
        return Result.Success();
    }

    public Result Confirm()
    {
        if (Status != OrderStatus.Pending)
            return Result.Failure("Order is already confirmed");
        if (_lines.Count == 0)
            return Result.Failure("Cannot confirm an empty order");

        Status = OrderStatus.Confirmed;
        return Result.Success();
    }
}

// ============================================================
// APPLICATION LAYER — Command Handler
// ============================================================

public sealed record CreateOrderCommand(
    string CustomerId,
    IReadOnlyList<CreateOrderLineDto> Lines);

public sealed class CreateOrderCommandHandler
{
    private readonly IOrderRepository _repository;
    private readonly IStockService _stockService;
    private readonly IOrderNotifier _notifier;
    private readonly ILogger<CreateOrderCommandHandler> _logger;

    public CreateOrderCommandHandler(
        IOrderRepository repository,
        IStockService stockService,
        IOrderNotifier notifier,
        ILogger<CreateOrderCommandHandler> logger)
    {
        _repository = repository;
        _stockService = stockService;
        _notifier = notifier;
        _logger = logger;
    }

    // CC = 1 — The method orchestrates; complexity lives in collaborators
    public async Task<Result<Guid>> HandleAsync(
        CreateOrderCommand command,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Creating order for customer {CustomerId}", command.CustomerId);

        var orderResult = BuildOrder(command);
        if (!orderResult.IsSuccess) return Result<Guid>.Failure(orderResult.Error!);

        var stockResult = await ValidateStockAsync(orderResult.Value!, command, ct);
        if (!stockResult.IsSuccess) return Result<Guid>.Failure(stockResult.Error!);

        await _repository.SaveAsync(orderResult.Value!, ct);
        await _notifier.OrderCreatedAsync(orderResult.Value!, ct);

        _logger.LogInformation("Order {OrderId} created successfully", orderResult.Value!.Id);
        return Result<Guid>.Success(orderResult.Value!.Id);
    }

    // Each sub-step is its own small, testable method
    private static Result<Order> BuildOrder(CreateOrderCommand command)
    {
        var order = Order.Create(command.CustomerId);
        if (!order.IsSuccess) return order;

        foreach (var lineDto in command.Lines)
        {
            var line = OrderLine.Create(lineDto.ProductSku, lineDto.Quantity, lineDto.UnitPrice);
            if (!line.IsSuccess) return Result<Order>.Failure(line.Error!);

            var addResult = order.Value!.AddLine(line.Value!);
            if (!addResult.IsSuccess) return Result<Order>.Failure(addResult.Error!);
        }

        return order.Value!.Confirm().IsSuccess
            ? order
            : Result<Order>.Failure("Failed to confirm order");
    }

    private async Task<Result> ValidateStockAsync(
        Order order,
        CreateOrderCommand command,
        CancellationToken ct)
    {
        var stockChecks = command.Lines
            .Select(line => _stockService.HasStockAsync(line.ProductSku, line.Quantity, ct));

        var results = await Task.WhenAll(stockChecks);

        return results.All(r => r)
            ? Result.Success()
            : Result.Failure("One or more items are out of stock");
    }
}

// ============================================================
// API LAYER
// ============================================================

[ApiController]
[Route("api/v1/orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly CreateOrderCommandHandler _handler;

    public OrdersController(CreateOrderCommandHandler handler)
        => _handler = handler;

    [HttpPost]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateOrder(
        [FromBody] CreateOrderRequest request,
        CancellationToken ct)
    {
        var command = request.ToCommand();
        var result = await _handler.HandleAsync(command, ct);

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetOrder), new { id = result.Value }, new CreateOrderResponse(result.Value))
            : BadRequest(new ProblemDetails { Title = "Order Creation Failed", Detail = result.Error });
    }
}
```

---

## Summary Cheat Sheet

```
┌─────────────────────────────────────────────────────────────────┐
│              C# ELITE STANDARDS — QUICK REFERENCE               │
├───────────────────┬─────────────────────────────────────────────┤
│ Cyclomatic CC     │ ≤ 10 hard limit; target ≤ 5 always          │
│ Method Length     │ ≤ 40 lines; target ≤ 20 lines               │
│ Parameters        │ ≤ 4 per method (use parameter objects)      │
│ Class Length      │ ≤ 300 lines                                  │
│ Nullable          │ ALWAYS enabled; warnings = errors            │
│ Async             │ All the way; always CancellationToken        │
│ Errors            │ Result<T> for business; throw for bugs       │
│ Naming            │ Intent-revealing; no abbreviations           │
│ Tests             │ 90%+ domain coverage; Arrange/Act/Assert     │
│ Security          │ Validate all input; no SQL concat; no log PII│
│ Resources         │ Always using/await using; pool where needed  │
│ Comments          │ Explain WHY, never WHAT                      │
├───────────────────┴─────────────────────────────────────────────┤
│ SOURCES: Google Style Guide · NASA JPL C Standard · SEI CERT   │
│          MISRA C++ · Microsoft .NET Guidelines · Clean Code      │
│          ISO/IEC 25010 · Robert C. Martin · Uncle Bob           │
└─────────────────────────────────────────────────────────────────┘
```

---

*Document Version: 1.0 | Effective: 2024 | Review: Annual or on major .NET release*  
*All rules are enforced automatically via Roslyn analyzers, SonarQube, and CI pipeline gates.*
