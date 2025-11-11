# Cosmos DB Delete By SQL

A CLI tool to delete Azure Cosmos DB items that match a condition specified by SQL SELECT syntax.

## Features

- Delete items based on SQL query conditions
- Smart deletion strategy:
  - Uses `DeleteAllItemsInPartitionKey` for single-partition queries (when supported by the account)
  - Falls back to individual item deletion for multi-partition queries or when batch deletion is not supported
- Preview items before deletion with a formatted table

## Prerequisites

- .NET 9.0 or later
- Azure Cosmos DB account with appropriate permissions
- Authentication configured (Azure CLI login, managed identity, or environment variables)

## Usage

### Basic Usage

```bash
dotnet run --project CosmosDbDeleteBySql/CosmosDbDeleteBySql.csproj -- \
  --endpoint "https://your-account.documents.azure.com:443/" \
  --database "your-database" \
  --container "your-container" \
  --query "SELECT * FROM c WHERE c.status = 'inactive'"
```

### Command Line Options

| Option | Required | Description |
|--------|----------|-------------|
| `--endpoint` | Yes | Cosmos DB account endpoint URL |
| `--database` | Yes | Database name |
| `--container` | Yes | Container name |
| `--query` | Yes | SQL query to select items to delete |
| `--preview-count` | No | Number of items to show in preview (default: 5) |
| `--yes` | No | Skip confirmation prompt |

### Quick Setup with Azure CLI

```bash
az login
```

## Supported Query Patterns for Optimized Deletion

The tool will use `DeleteAllItemsInPartitionKey` when it detects these query patterns:

✅ **Supported** (uses optimized deletion):
- `SELECT * FROM c WHERE c.partitionKey = 'value'`
- `SELECT * FROM c WHERE c.partitionKey = "value"`

❌ **Not Supported** (falls back to individual deletion):
- `SELECT * FROM c WHERE c.partitionKey IN ('value1', 'value2')`
- `SELECT * FROM c WHERE c.partitionKey > 'value'`
- `SELECT * FROM c WHERE c.partitionKey = 'value1' OR c.partitionKey = 'value2'`
- `SELECT c.id, c.name FROM c WHERE c.status = 'active' AND c.partitionKey = 'value'`
- `SELECT * FROM c WHERE c.otherField = 'value'` (no partition key filter)

## License

MIT
