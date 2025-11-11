using System.ComponentModel;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using Spectre.Console.Cli;

public class DeleteCommand : AsyncCommand<DeleteCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("--endpoint <ENDPOINT>")]
        [Description("Cosmos DB account endpoint URL")]
        public required string Endpoint { get; init; }

        [CommandOption("--database <DATABASE>")]
        [Description("Database name")]
        public required string Database { get; init; }

        [CommandOption("--container <CONTAINER>")]
        [Description("Container name")]
        public required string Container { get; init; }

        [CommandOption("--query <QUERY>")]
        [Description("SQL query to select items to delete (e.g., 'SELECT * FROM c WHERE c.status = \"inactive\"')")]
        public required string Query { get; init; }

        [CommandOption("--preview-count <COUNT>")]
        [Description("Number of items to show in preview (default: 5)")]
        [DefaultValue(5)]
        public int PreviewCount { get; init; } = 5;

        [CommandOption("--yes")]
        [Description("Skip confirmation prompt")]
        [DefaultValue(false)]
        public bool SkipConfirmation { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            await DeleteItemsAsync(settings);
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message.EscapeMarkup()}[/]");
            AnsiConsole.WriteException(ex);
            return 1;
        }
    }

    private async Task DeleteItemsAsync(Settings settings)
    {
        CosmosDbService cosmosService = await AnsiConsole.Status()
            .StartAsync("Connecting to Cosmos DB...", async ctx =>
            {
                return await CosmosDbService.CreateAsync(settings.Endpoint, settings.Database, settings.Container);
            });

        using (cosmosService)
        {
            AnsiConsole.MarkupLine($"[green]Connected to:[/] {settings.Database.EscapeMarkup()}/{settings.Container.EscapeMarkup()}");
            AnsiConsole.MarkupLine($"[green]Partition key path:[/] {cosmosService.PartitionKeyPath.EscapeMarkup()}");
            AnsiConsole.MarkupLine($"[green]Query:[/] {settings.Query.EscapeMarkup()}");
            AnsiConsole.WriteLine();

            // Analyze query to determine if we can use DeleteAllItemsInPartitionKey
            var canUsePartitionDelete = QueryAnalyzer.CanUseDeleteAllItemsInPartition(
                settings.Query,
                cosmosService.PartitionKeyPath);

            var partitionKeyValue = QueryAnalyzer.ExtractPartitionKeyValue(
                settings.Query,
                cosmosService.PartitionKeyPath);

            if (canUsePartitionDelete && partitionKeyValue != null)
            {
                AnsiConsole.MarkupLine($"[green]Query targets a single partition (partition key = '{partitionKeyValue.EscapeMarkup()}'). Will use DeleteAllItemsInPartitionKey for efficient deletion.[/]");
                await DeleteWithPartitionKeyAsync(cosmosService, settings, partitionKeyValue);
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]Query spans multiple partitions or is not partition-specific. Will fetch items and delete individually.[/]");
                await DeleteIndividuallyAsync(cosmosService, settings);
            }
        }
    }

    private async Task DeleteWithPartitionKeyAsync(CosmosDbService cosmosService, Settings settings, string partitionKeyValue)
    {
        // First, fetch a preview of items to show the user what will be deleted
        var previewItems = await AnsiConsole.Status()
            .StartAsync("Fetching preview items...", async ctx =>
            {
                return await cosmosService.FetchItemsAsync(settings.Query, limit: settings.PreviewCount + 10);
            });

        if (previewItems.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No items found matching the query.[/]");
            return;
        }

        // Get total count for display
        var totalCount = await AnsiConsole.Status()
            .StartAsync("Counting items...", async ctx =>
            {
                return await cosmosService.CountItemsAsync(settings.Query);
            });

        if (!await ShowPreviewAndConfirm(previewItems, totalCount, settings, "Are you sure you want to delete all items in this partition?"))
        {
            return;
        }

        // Perform deletion using DeleteAllItemsInPartitionKey
        var partitionKey = new PartitionKey(partitionKeyValue);

        try
        {
            await AnsiConsole.Status()
                .StartAsync("Deleting all items in partition...", async ctx =>
                {
                    await cosmosService.DeleteAllItemsByPartitionKeyAsync(partitionKey);
                });

            AnsiConsole.MarkupLine($"[green]Successfully deleted all items in partition '{partitionKeyValue.EscapeMarkup()}'.[/]");
        }
        catch (Exception ex) when (ex is CosmosException cosmosEx && cosmosEx.StatusCode == System.Net.HttpStatusCode.BadRequest || ex is NotSupportedException)
        {
            AnsiConsole.MarkupLine("[yellow]DeleteAllItemsInPartitionKey is not supported on this account. Falling back to stream deletion.[/]");
            await StreamDeleteAsync(cosmosService, settings);
        }
    }

    private async Task DeleteIndividuallyAsync(CosmosDbService cosmosService, Settings settings)
    {
        // Fetch preview items only (not all items)
        var previewItems = await AnsiConsole.Status()
            .StartAsync("Fetching preview items...", async ctx =>
            {
                return await cosmosService.FetchItemsAsync(settings.Query, limit: settings.PreviewCount + 10);
            });

        if (previewItems.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No items found matching the query.[/]");
            return;
        }

        // Get total count for display
        var totalCount = await AnsiConsole.Status()
            .StartAsync("Counting items...", async ctx =>
            {
                return await cosmosService.CountItemsAsync(settings.Query);
            });

        if (!await ShowPreviewAndConfirm(previewItems, totalCount, settings, $"Are you sure you want to delete {totalCount} items?"))
        {
            return;
        }

        await StreamDeleteAsync(cosmosService, settings);
    }

    private async Task StreamDeleteAsync(CosmosDbService cosmosService, Settings settings)
    {
        int totalDeleted = 0;

        await AnsiConsole.Progress()
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[yellow]Deleting items (streaming)...[/]");

                totalDeleted = await cosmosService.DeleteItemsAsync(settings.Query, progress =>
                {
                    task.Value = progress;
                    if (!task.IsStarted)
                    {
                        task.StartTask();
                    }
                    // Update max value if we don't know it yet
                    if (task.MaxValue < progress)
                    {
                        task.MaxValue = progress;
                    }
                });

                task.StopTask();
            });

        AnsiConsole.MarkupLine($"[green]Successfully deleted {totalDeleted} items.[/]");
    }

    private async Task<bool> ShowPreviewAndConfirm(
        List<JObject> items,
        int totalCount,
        Settings settings,
        string confirmationMessage)
    {
        AnsiConsole.MarkupLine($"[cyan]Found {totalCount} items to delete.[/]");
        AnsiConsole.WriteLine();

        ShowPreview(items, settings.PreviewCount);

        // Confirm deletion
        if (!settings.SkipConfirmation)
        {
            var confirm = AnsiConsole.Confirm($"[bold red]{confirmationMessage}[/]", false);
            if (!confirm)
            {
                AnsiConsole.MarkupLine("[yellow]Operation cancelled.[/]");
                return false;
            }
        }

        return true;
    }

    private void ShowPreview(List<JObject> items, int previewCount)
    {
        var itemsToShow = items.Take(previewCount).ToList();

        AnsiConsole.MarkupLine($"[cyan]Preview of items to delete (showing {itemsToShow.Count} of {items.Count}):[/]");

        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn("#");
        table.AddColumn("ID");
        table.AddColumn("Document (truncated)");

        for (int i = 0; i < itemsToShow.Count; i++)
        {
            var item = itemsToShow[i];
            var docString = item.ToString();
            var truncated = docString.Length > 100 ? docString[..100] + "..." : docString;

            table.AddRow(
                (i + 1).ToString().EscapeMarkup(),
                item["id"]!.ToString().EscapeMarkup(),
                truncated.EscapeMarkup()
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }
}
