# Agent Guidelines for Cosmos DB Delete By SQL

## Build & Test Commands

- **Build**: `dotnet build cosmosdb-delete-by-sql.sln`
- **Run tests**: `dotnet test CosmosDbDeleteBySql.Tests/CosmosDbDeleteBySql.Tests.csproj`
- **Run single test**: `dotnet test CosmosDbDeleteBySql.Tests/CosmosDbDeleteBySql.Tests.csproj --filter "MethodName"`
- **Run app**: `dotnet run --project CosmosDbDeleteBySql/CosmosDbDeleteBySql.csproj -- [OPTIONS]`

## Code Style & Guidelines

**Language**: C# 12 (.NET 9.0)

**Imports**: Use file-scoped namespaces (`namespace Foo;`), no braces. Order: System → third-party → project classes.

**Formatting**: 4-space indentation, PascalCase for public classes/properties, camelCase for private fields. Use implicit usings.

**Types**: Enable nullable reference types (`<Nullable>enable</Nullable>`). Use `required` for mandatory properties, `?` for optional values.

**Naming**: Suffix async methods with `Async`. Use descriptive names: `DeleteItemWithRetryAsync`, `FetchItemsAsync`, `CanUseDeleteAllItemsInPartition`.

**Error Handling**: Catch specific exceptions (`CosmosException`, `NotSupportedException`), include retry logic with exponential backoff for transient failures (429 TooManyRequests).

**Methods**: Keep small and focused. Use LINQ where appropriate. Document public methods with `/// <summary>` XML comments.

**Testing**: Use xUnit with `[Theory]` & `[InlineData]` for parameterized tests. Follow AAA pattern (Arrange, Act, Assert).

**Commits & PRs**: When asked to create a commit or pull request, always use a title that clearly reflects the change included. For pull requests, also provide a body that summarizes the motivation and impact of the changes.
