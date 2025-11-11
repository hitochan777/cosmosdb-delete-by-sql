# Cosmos DB Delete By SQL

A CLI tool to delete Azure Cosmos DB items that match a condition specified by SQL SELECT syntax.

## Features

- Delete items based on SQL query conditions
- Preview items before deletion with a formatted table
- Authentication via `DefaultAzureCredential` (supports managed identity, Azure CLI, environment variables, etc.)
- Smart deletion strategy:
  - Uses `DeleteAllItemsInPartitionKey` for single-partition queries (when supported by the account)
  - Falls back to individual item deletion for multi-partition queries or when batch deletion is not supported
- Progress tracking with Spectre.Console
- Batch processing for efficient deletion
- Automatic retry logic for rate limiting (429 errors)

## Prerequisites

- .NET 9.0 or later
- Azure Cosmos DB account with appropriate permissions
- Authentication configured (Azure CLI login, managed identity, or environment variables)

## Installation

```bash
dotnet build
```

## Usage

### Basic Usage

```bash
dotnet run -- \
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

### Examples

#### Delete items by status
```bash
dotnet run -- \
  --endpoint "https://myaccount.documents.azure.com:443/" \
  --database "mydb" \
  --container "users" \
  --query "SELECT * FROM c WHERE c.status = 'deleted'"
```

#### Delete old items with auto-confirmation
```bash
dotnet run -- \
  --endpoint "https://myaccount.documents.azure.com:443/" \
  --database "mydb" \
  --container "logs" \
  --query "SELECT * FROM c WHERE c._ts < 1640000000" \
  --yes
```

#### Delete items in a specific partition (uses efficient DeleteAllItemsInPartitionKey)
```bash
dotnet run -- \
  --endpoint "https://myaccount.documents.azure.com:443/" \
  --database "mydb" \
  --container "events" \
  --query "SELECT * FROM c WHERE c.tenantId = 'tenant-123'"
```

#### Show more preview items
```bash
dotnet run -- \
  --endpoint "https://myaccount.documents.azure.com:443/" \
  --database "mydb" \
  --container "products" \
  --query "SELECT * FROM c WHERE c.discontinued = true" \
  --preview-count 10
```

## Authentication

This tool uses `DefaultAzureCredential` from Azure Identity, which tries the following authentication methods in order:

1. Environment variables
2. Managed Identity
3. Visual Studio
4. Azure CLI
5. Azure PowerShell
6. Interactive browser

### Quick Setup with Azure CLI

```bash
az login
```

### Using Environment Variables

```bash
export AZURE_CLIENT_ID="your-client-id"
export AZURE_TENANT_ID="your-tenant-id"
export AZURE_CLIENT_SECRET="your-client-secret"
```

## How It Works

1. **Connect**: Establishes connection to Cosmos DB using DefaultAzureCredential and retrieves container metadata
2. **Analyze Query**: Parses the SQL query to detect if it contains a partition key equality condition (e.g., `WHERE c.partitionKey = 'value'`)
3. **Smart Deletion Path**:
   - **Single Partition Query**: If the query filters on a single partition key value with equality comparison:
     - Fetches preview items to show the user
     - Uses `DeleteAllItemsInPartitionKey` API for efficient deletion (if supported by the account)
     - Falls back to individual deletion if the API is not supported
   - **Multi-Partition Query**: If the query spans multiple partitions or uses non-equality operators:
     - Fetches all matching items
     - Deletes items individually in batches with progress tracking
4. **Preview**: Displays a sample of items in a formatted table
5. **Confirm**: Asks for user confirmation (unless `--yes` is specified)
6. **Delete**: Executes the appropriate deletion strategy with progress feedback

## Performance

- **Query Analysis**: Automatically detects single-partition queries by parsing SQL for partition key equality conditions, avoiding the need to fetch all items first
- **Single Partition Deletion**: Uses the efficient `DeleteAllItemsInPartitionKey` API when the query targets a single partition (e.g., `WHERE c.tenantId = 'abc123'`)
- **Multi-Partition Deletion**: Processes items in batches of 100 with parallel deletion
- **Rate Limiting**: Automatically retries on 429 errors with exponential backoff

## Supported Query Patterns for Optimized Deletion

The tool will use `DeleteAllItemsInPartitionKey` when it detects these query patterns:

✅ **Supported** (uses optimized deletion):
- `SELECT * FROM c WHERE c.partitionKey = 'value'`
- `SELECT * FROM c WHERE c.partitionKey = "value"`
- `SELECT c.id, c.name FROM c WHERE c.status = 'active' AND c.partitionKey = 'value'`

❌ **Not Supported** (falls back to individual deletion):
- `SELECT * FROM c WHERE c.partitionKey IN ('value1', 'value2')`
- `SELECT * FROM c WHERE c.partitionKey > 'value'`
- `SELECT * FROM c WHERE c.partitionKey = 'value1' OR c.partitionKey = 'value2'`
- `SELECT * FROM c WHERE c.otherField = 'value'` (no partition key filter)

## Error Handling

- Validates connection and permissions before starting
- Provides detailed error messages
- Automatically retries transient failures
- Falls back to individual deletion if bulk deletion is not supported

## License

MIT
