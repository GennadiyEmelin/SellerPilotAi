using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactClient", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddHttpClient();
builder.Services.AddSingleton<MarketplaceDataService>();

var app = builder.Build();

app.UseCors("ReactClient");

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", product = "SellerPilot AI" }));

app.MapGet("/api/integrations/status", async (MarketplaceDataService marketplaceData) =>
{
    return Results.Ok(await marketplaceData.GetStatusAsync());
});

app.MapGet("/api/integrations/ozon/credentials", (MarketplaceDataService marketplaceData) =>
{
    return Results.Ok(marketplaceData.GetOzonCredentialsStatus());
});

app.MapPut("/api/integrations/ozon/credentials", async (OzonCredentialsRequest request, MarketplaceDataService marketplaceData) =>
{
    await marketplaceData.SaveOzonCredentialsAsync(request);
    return Results.Ok(marketplaceData.GetOzonCredentialsStatus());
});

app.MapPost("/api/integrations/sync", async (MarketplaceDataService marketplaceData) =>
{
    var result = await marketplaceData.SyncAsync();
    return Results.Ok(result);
});

app.MapGet("/api/integrations/ozon/finance-debug", async (MarketplaceDataService marketplaceData) =>
{
    return Results.Ok(await marketplaceData.GetOzonFinanceDebugAsync());
});

app.MapGet("/api/dashboard", async (MarketplaceDataService marketplaceData) =>
{
    var source = await marketplaceData.GetDashboardSourceAsync();
    var calculated = source.Products.Select(Calculate).ToArray();
    var revenue = calculated.Sum(item => item.GrossRevenue);
    var profit = calculated.Sum(item => item.Profit);
    var margin = revenue > 0 ? profit / revenue : 0;
    var riskCount = calculated.Count(item => item.Signal != "good");

    return Results.Ok(new DashboardResponse(
        revenue,
        profit,
        margin,
        riskCount,
        calculated,
        BuildRecommendations(calculated),
        BuildTasks(calculated),
        source.Source,
        source.IntegrationMessages));
});

app.MapGet("/api/products", async (MarketplaceDataService marketplaceData) =>
{
    var source = await marketplaceData.GetDashboardSourceAsync();
    return Results.Ok(source.Products.Select(Calculate));
});

app.MapGet("/api/recommendations", async (MarketplaceDataService marketplaceData) =>
{
    var source = await marketplaceData.GetDashboardSourceAsync();
    var calculated = source.Products.Select(Calculate).ToArray();
    return Results.Ok(BuildRecommendations(calculated));
});

app.MapGet("/api/tasks", async (MarketplaceDataService marketplaceData) =>
{
    var source = await marketplaceData.GetDashboardSourceAsync();
    var calculated = source.Products.Select(Calculate).ToArray();
    return Results.Ok(BuildTasks(calculated));
});

app.Run();

static ProductMetrics Calculate(Product item)
{
    var grossRevenue = item.Sold * item.Price;
    var returnLoss = grossRevenue * item.ReturnRate;
    var commission = item.CommissionExpense;
    var logistics = item.LogisticsExpense;
    var purchaseCost = item.Sold * item.PurchaseCost;
    var expenses = purchaseCost + commission + logistics + item.ServicesExpense + item.ReturnExpense + returnLoss;
    var profit = grossRevenue - expenses;
    var margin = grossRevenue > 0 ? profit / grossRevenue : 0;
    var stockDays = item.DailySales > 0 ? (decimal)item.Stock / item.DailySales : 999;
    var signal = GetSignal(profit, margin, stockDays);

    return new ProductMetrics(
        item.Sku,
        item.Name,
        item.Sold,
        item.Price,
        item.Stock,
        grossRevenue,
        expenses,
        commission,
        logistics,
        item.ServicesExpense,
        item.ReturnExpense,
        profit,
        margin,
        stockDays,
        signal);
}

static string GetSignal(decimal profit, decimal margin, decimal stockDays)
{
    if (profit < 0 || margin < 0.08m)
    {
        return "risk";
    }

    if (stockDays < 7 || margin < 0.18m)
    {
        return "watch";
    }

    return "good";
}

static IReadOnlyList<Recommendation> BuildRecommendations(IReadOnlyCollection<ProductMetrics> items)
{
    if (items.Count == 0)
    {
        return
        [
            new Recommendation(
                "medium",
                "Нет товаров из Ozon",
                "Проверьте ключи Ozon и права API. После успешного подключения здесь появятся рекомендации по реальным товарам.")
        ];
    }

    var lowMargin = items.OrderBy(item => item.Margin).First();
    var stockout = items.OrderBy(item => item.StockDays).First();
    var best = items.OrderByDescending(item => item.Profit).First();

    return
    [
        new Recommendation(
            lowMargin.Profit < 0 ? "high" : "medium",
            $"Проверьте цену и рекламу: {lowMargin.Name}",
            $"Маржа {Math.Round(lowMargin.Margin * 100)}%, прибыль {Money(lowMargin.Profit)}. Уменьшите рекламу или поднимите цену минимум на {Money(Math.Max(40, lowMargin.Price * 0.08m))}."),
        new Recommendation(
            stockout.StockDays < 7 ? "high" : "medium",
            $"Риск закончить остатки: {stockout.Name}",
            $"Остатка хватит примерно на {Math.Max(1, Math.Round(stockout.StockDays))} дн. Подготовьте поставку, чтобы не потерять позицию в выдаче."),
        new Recommendation(
            "good",
            $"Масштабируйте прибыльный SKU: {best.Name}",
            $"Товар дал {Money(best.Profit)} прибыли. Можно аккуратно увеличить рекламный лимит и проверить допоставку.")
    ];
}

static IReadOnlyList<SellerTask> BuildTasks(IReadOnlyCollection<ProductMetrics> items)
{
    var riskyCount = items.Count(item => item.Signal != "good");

    if (items.Count == 0)
    {
        return
        [
            new SellerTask("Подключить Ozon API", "Укажите OZON_CLIENT_ID и OZON_API_KEY, затем нажмите проверку интеграции.")
        ];
    }

    return
    [
        new SellerTask("Проверить рекламные ставки", $"{riskyCount} SKU требуют контроля по марже и расходам."),
        new SellerTask("Собрать поставку", "Приоритет товарам, где остатка меньше 7 дней продаж."),
        new SellerTask("Обновить себестоимость", "Загрузите свежий закупочный прайс, чтобы расчет прибыли был точным.")
    ];
}

static string Money(decimal value) => $"{Math.Round(value):N0} ₽".Replace(",", " ");

record Product(
    string Sku,
    string Name,
    int Sold,
    decimal Price,
    decimal PurchaseCost,
    decimal CommissionExpense,
    decimal LogisticsExpense,
    decimal ServicesExpense,
    decimal ReturnExpense,
    decimal ReturnRate,
    int Stock,
    int DailySales);

record ProductMetrics(
    string Sku,
    string Name,
    int Sold,
    decimal Price,
    int Stock,
    decimal GrossRevenue,
    decimal Expenses,
    decimal CommissionExpense,
    decimal LogisticsExpense,
    decimal ServicesExpense,
    decimal ReturnExpense,
    decimal Profit,
    decimal Margin,
    decimal StockDays,
    string Signal);

record Recommendation(string Level, string Title, string Text);

record SellerTask(string Title, string Text);

record DashboardResponse(
    decimal Revenue,
    decimal Profit,
    decimal Margin,
    int RiskCount,
    IReadOnlyList<ProductMetrics> Products,
    IReadOnlyList<Recommendation> Recommendations,
    IReadOnlyList<SellerTask> Tasks,
    string Source,
    IReadOnlyList<string> IntegrationMessages);

record OzonOptions(string BaseUrl, string ClientId, string ApiKey);

record OzonCredentialsRequest(string ClientId, string ApiKey);

record OzonCredentialsStatus(bool Configured, string ClientIdPreview);

record OzonProductIdentity(long ProductId, string OfferId);

record OzonSalesMetrics(string Sku, string Name, int Sold, decimal Revenue);

record OzonFinanceMetrics(
    string Sku,
    decimal CommissionExpense,
    decimal LogisticsExpense,
    decimal ServicesExpense,
    decimal ReturnExpense);

record OzonFinanceServiceDebug(string Name, decimal Total, int Count);

record IntegrationStatus(string Marketplace, bool Configured, bool Available, string Message);

record SyncResult(IReadOnlyList<IntegrationStatus> Statuses, IReadOnlyList<string> ImportedItems);

record DashboardSource(IReadOnlyList<Product> Products, string Source, IReadOnlyList<string> IntegrationMessages);

sealed class MarketplaceDataService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<MarketplaceDataService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string secretsPath = Path.Combine(AppContext.BaseDirectory, "secrets", "ozon.local.json");

    public async Task<IReadOnlyList<IntegrationStatus>> GetStatusAsync()
    {
        return [await CheckOzonAsync()];
    }

    public async Task<SyncResult> SyncAsync()
    {
        var statuses = await GetStatusAsync();
        var importedItems = new List<string>();

        foreach (var status in statuses.Where(item => item.Available))
        {
            importedItems.Add($"{status.Marketplace}: подключение активно, готово к импорту отчетов и остатков.");
        }

        return new SyncResult(statuses, importedItems);
    }

    public async Task<IReadOnlyList<OzonFinanceServiceDebug>> GetOzonFinanceDebugAsync()
    {
        var options = GetOzonOptions();
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return [];
        }

        var body = await FetchOzonFinancePageAsync(options, 1);
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        return ParseOzonFinanceServiceDebug(body);
    }

    public OzonCredentialsStatus GetOzonCredentialsStatus()
    {
        var options = GetOzonOptions();
        return new OzonCredentialsStatus(
            !string.IsNullOrWhiteSpace(options.ClientId) && !string.IsNullOrWhiteSpace(options.ApiKey),
            Preview(options.ClientId));
    }

    public async Task SaveOzonCredentialsAsync(OzonCredentialsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId) || string.IsNullOrWhiteSpace(request.ApiKey))
        {
            throw new BadHttpRequestException("Client-Id и Api-Key обязательны.");
        }

        var directory = Path.GetDirectoryName(secretsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
            secretsPath,
            JsonSerializer.Serialize(new OzonOptions("https://api-seller.ozon.ru", request.ClientId.Trim(), request.ApiKey.Trim()), JsonOptions));
    }

    public async Task<DashboardSource> GetDashboardSourceAsync()
    {
        var statuses = await GetStatusAsync();
        var messages = statuses.Select(status => $"{status.Marketplace}: {status.Message}").ToArray();
        var hasLiveConnection = statuses.Any(status => status.Available);

        if (!hasLiveConnection)
        {
            return new DashboardSource([], "ozon-not-connected", messages);
        }

        var products = await FetchOzonProductsAsync();
        var sourceMessages = messages
            .Concat([$"Ozon: загружено товаров {products.Count}"])
            .ToArray();

        return new DashboardSource(products, "ozon-api", sourceMessages);
    }

    private async Task<IntegrationStatus> CheckOzonAsync()
    {
        var options = GetOzonOptions();
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return new IntegrationStatus("Ozon", false, false, "укажите OZON_CLIENT_ID и OZON_API_KEY");
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl.TrimEnd('/')}/v3/product/list");
            request.Headers.Add("Client-Id", options.ClientId);
            request.Headers.Add("Api-Key", options.ApiKey);
            request.Content = JsonContent(new
            {
                filter = new { visibility = "ALL" },
                limit = 1,
                last_id = ""
            });

            using var response = await httpClientFactory.CreateClient().SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new IntegrationStatus("Ozon", true, false, $"API вернул {(int)response.StatusCode}: {Trim(body)}");
            }

            var count = CountJsonArray(body, "items");
            return new IntegrationStatus("Ozon", true, true, $"ключ работает, получен доступ к списку товаров ({count} item preview)");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ozon API check failed");
            return new IntegrationStatus("Ozon", true, false, $"ошибка подключения: {ex.Message}");
        }
    }

    private OzonOptions GetOzonOptions()
    {
        var section = configuration.GetSection("MarketplaceApi:Ozon");
        var localOptions = ReadLocalOzonOptions();

        return new OzonOptions(
            Environment.GetEnvironmentVariable("OZON_BASE_URL") ?? localOptions?.BaseUrl ?? section["BaseUrl"] ?? "https://api-seller.ozon.ru",
            Environment.GetEnvironmentVariable("OZON_CLIENT_ID") ?? localOptions?.ClientId ?? section["ClientId"] ?? "",
            Environment.GetEnvironmentVariable("OZON_API_KEY") ?? localOptions?.ApiKey ?? section["ApiKey"] ?? "");
    }

    private OzonOptions? ReadLocalOzonOptions()
    {
        if (!File.Exists(secretsPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(secretsPath);
            return JsonSerializer.Deserialize<OzonOptions>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read local Ozon credentials");
            return null;
        }
    }

    private async Task<IReadOnlyList<Product>> FetchOzonProductsAsync()
    {
        var options = GetOzonOptions();
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return [];
        }

        var identities = await FetchOzonProductIdsAsync(options);
        if (identities.Count == 0)
        {
            return [];
        }

        var products = await FetchOzonProductInfoAsync(options, identities);
        var sales = await FetchOzonSalesMetricsAsync(options);
        var finance = await FetchOzonFinanceMetricsAsync(options);

        return MergeProducts(products, sales, finance);
    }

    private async Task<IReadOnlyList<OzonProductIdentity>> FetchOzonProductIdsAsync(OzonOptions options)
    {
        var request = CreateOzonRequest(options, "/v3/product/list", new
        {
            filter = new { visibility = "ALL" },
            limit = 100,
            last_id = ""
        });

        using var response = await httpClientFactory.CreateClient().SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Ozon product list failed: {StatusCode} {Body}", response.StatusCode, Trim(body));
            return [];
        }

        return ParseOzonProductIdentities(body);
    }

    private async Task<IReadOnlyList<Product>> FetchOzonProductInfoAsync(OzonOptions options, IReadOnlyList<OzonProductIdentity> identities)
    {
        var productIds = identities
            .Where(item => item.ProductId > 0)
            .Select(item => item.ProductId)
            .ToArray();

        if (productIds.Length == 0)
        {
            return identities
                .Select(item => EmptyProduct(item.OfferId))
                .ToArray();
        }

        var request = CreateOzonRequest(options, "/v3/product/info/list", new
        {
            product_id = productIds
        });

        using var response = await httpClientFactory.CreateClient().SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Ozon product info failed: {StatusCode} {Body}", response.StatusCode, Trim(body));
            return identities
                .Select(item => EmptyProduct(item.OfferId))
                .ToArray();
        }

        return ParseOzonProductInfo(body, identities);
    }

    private HttpRequestMessage CreateOzonRequest<T>(OzonOptions options, string path, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl.TrimEnd('/')}{path}");
        request.Headers.Add("Client-Id", options.ClientId);
        request.Headers.Add("Api-Key", options.ApiKey);
        request.Content = JsonContent(body);

        return request;
    }

    private static IReadOnlyList<OzonProductIdentity> ParseOzonProductIdentities(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var products = new List<OzonProductIdentity>();
        foreach (var item in items.EnumerateArray())
        {
            var offerId = GetString(item, "offer_id");
            var productId = GetLong(item, "product_id");
            if (productId <= 0 && string.IsNullOrWhiteSpace(offerId))
            {
                continue;
            }

            products.Add(new OzonProductIdentity(productId, offerId));
        }

        return products;
    }

    private static IReadOnlyList<Product> ParseOzonProductInfo(string json, IReadOnlyList<OzonProductIdentity> identities)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("items", out var items) &&
            (!document.RootElement.TryGetProperty("result", out var result) ||
             !result.TryGetProperty("items", out items)))
        {
            return identities.Select(item => EmptyProduct(item.OfferId)).ToArray();
        }

        var products = new List<Product>();
        foreach (var item in items.EnumerateArray())
        {
            var offerId = GetString(item, "offer_id");
            var productId = GetString(item, "id");
            var ozonSku = GetString(item, "sku");
            var sku = FirstNotEmpty(ozonSku, offerId, productId, GetString(item, "product_id"));

            var name = GetString(item, "name");
            var price = GetPrice(item);
            var stock = GetStock(item);

            products.Add(new Product(
                string.IsNullOrWhiteSpace(sku) ? $"product-{products.Count + 1}" : sku,
                string.IsNullOrWhiteSpace(name) ? $"Ozon SKU {sku}" : name,
                0,
                price,
                0,
                0,
                0,
                0,
                0,
                0,
                stock,
                0));
        }

        return products;
    }

    private async Task<IReadOnlyList<OzonSalesMetrics>> FetchOzonSalesMetricsAsync(OzonOptions options)
    {
        var dateTo = DateOnly.FromDateTime(DateTime.UtcNow);
        var dateFrom = dateTo.AddDays(-30);
        var request = CreateOzonRequest(options, "/v1/analytics/data", new
        {
            date_from = dateFrom.ToString("yyyy-MM-dd"),
            date_to = dateTo.ToString("yyyy-MM-dd"),
            metrics = new[] { "revenue", "ordered_units" },
            dimension = new[] { "sku" },
            filters = Array.Empty<object>(),
            sort = new[]
            {
                new { key = "revenue", order = "DESC" }
            },
            limit = 1000,
            offset = 0
        });

        using var response = await httpClientFactory.CreateClient().SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Ozon analytics data failed: {StatusCode} {Body}", response.StatusCode, Trim(body));
            return [];
        }

        return ParseOzonSalesMetrics(body);
    }

    private async Task<IReadOnlyList<OzonFinanceMetrics>> FetchOzonFinanceMetricsAsync(OzonOptions options)
    {
        var dateTo = DateTime.UtcNow;
        var dateFrom = dateTo.AddDays(-30);
        var result = new List<OzonFinanceMetrics>();

        for (var page = 1; page <= 10; page++)
        {
            var body = await FetchOzonFinancePageAsync(options, page, dateFrom, dateTo);
            if (string.IsNullOrWhiteSpace(body))
            {
                break;
            }

            var pageItems = ParseOzonFinanceMetrics(body);
            if (pageItems.Count == 0)
            {
                break;
            }

            result.AddRange(pageItems);
            if (!HasNextFinancePage(body, page))
            {
                break;
            }
        }

        return result
            .GroupBy(item => item.Sku, StringComparer.OrdinalIgnoreCase)
            .Select(group => new OzonFinanceMetrics(
                group.Key,
                group.Sum(item => item.CommissionExpense),
                group.Sum(item => item.LogisticsExpense),
                group.Sum(item => item.ServicesExpense),
                group.Sum(item => item.ReturnExpense)))
            .ToArray();
    }

    private async Task<string> FetchOzonFinancePageAsync(OzonOptions options, int page)
    {
        return await FetchOzonFinancePageAsync(options, page, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);
    }

    private async Task<string> FetchOzonFinancePageAsync(OzonOptions options, int page, DateTime dateFrom, DateTime dateTo)
    {
        var request = CreateOzonRequest(options, "/v3/finance/transaction/list", new
        {
            filter = new
            {
                date = new
                {
                    from = dateFrom.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    to = dateTo.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                },
                operation_type = Array.Empty<string>(),
                posting_number = "",
                transaction_type = "all"
            },
            page,
            page_size = 1000
        });

        using var response = await httpClientFactory.CreateClient().SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Ozon finance transactions failed: {StatusCode} {Body}", response.StatusCode, Trim(body));
            return "";
        }

        return body;
    }

    private static IReadOnlyList<Product> MergeProducts(
        IReadOnlyList<Product> products,
        IReadOnlyList<OzonSalesMetrics> sales,
        IReadOnlyList<OzonFinanceMetrics> finance)
    {
        if (sales.Count == 0 && finance.Count == 0)
        {
            return products;
        }

        var salesBySku = sales
            .GroupBy(item => item.Sku, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var financeBySku = finance
            .GroupBy(item => item.Sku, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var productsBySku = products.ToDictionary(item => item.Sku, StringComparer.OrdinalIgnoreCase);
        var merged = new List<Product>();

        foreach (var product in products)
        {
            salesBySku.TryGetValue(product.Sku, out var salesMetric);
            financeBySku.TryGetValue(product.Sku, out var financeMetric);

            if (salesMetric is null && financeMetric is null)
            {
                merged.Add(product);
                continue;
            }

            var averagePrice = salesMetric?.Sold > 0 ? salesMetric.Revenue / salesMetric.Sold : product.Price;
            merged.Add(product with
            {
                Sold = salesMetric?.Sold ?? product.Sold,
                Price = averagePrice,
                CommissionExpense = financeMetric?.CommissionExpense ?? product.CommissionExpense,
                LogisticsExpense = financeMetric?.LogisticsExpense ?? product.LogisticsExpense,
                ServicesExpense = financeMetric?.ServicesExpense ?? product.ServicesExpense,
                ReturnExpense = financeMetric?.ReturnExpense ?? product.ReturnExpense
            });
        }

        foreach (var metric in sales)
        {
            if (productsBySku.ContainsKey(metric.Sku))
            {
                continue;
            }

            var averagePrice = metric.Sold > 0 ? metric.Revenue / metric.Sold : 0;
            merged.Add(new Product(
                metric.Sku,
                string.IsNullOrWhiteSpace(metric.Name) ? $"Ozon SKU {metric.Sku}" : metric.Name,
                metric.Sold,
                averagePrice,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0));
        }

        foreach (var metric in finance)
        {
            if (productsBySku.ContainsKey(metric.Sku) || salesBySku.ContainsKey(metric.Sku))
            {
                continue;
            }

            merged.Add(new Product(
                metric.Sku,
                $"Ozon SKU {metric.Sku}",
                0,
                0,
                0,
                metric.CommissionExpense,
                metric.LogisticsExpense,
                metric.ServicesExpense,
                metric.ReturnExpense,
                0,
                0,
                0));
        }

        return merged
            .OrderByDescending(item => item.Sold * item.Price)
            .ToArray();
    }

    private static IReadOnlyList<OzonSalesMetrics> ParseOzonSalesMetrics(string json)
    {
        using var document = JsonDocument.Parse(json);
        var data = GetDataArray(document.RootElement);
        if (data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<OzonSalesMetrics>();
        foreach (var row in data.EnumerateArray())
        {
            if (!row.TryGetProperty("dimensions", out var dimensions) ||
                dimensions.ValueKind != JsonValueKind.Array ||
                dimensions.GetArrayLength() == 0)
            {
                continue;
            }

            var dimension = dimensions[0];
            var sku = FirstNotEmpty(GetString(dimension, "id"), GetString(dimension, "value"), GetString(dimension, "name"));
            if (string.IsNullOrWhiteSpace(sku))
            {
                continue;
            }

            var name = GetString(dimension, "name");
            var revenue = 0m;
            var sold = 0;
            if (row.TryGetProperty("metrics", out var metrics) && metrics.ValueKind == JsonValueKind.Array)
            {
                revenue = GetDecimalAt(metrics, 0);
                sold = (int)Math.Round(GetDecimalAt(metrics, 1));
            }

            result.Add(new OzonSalesMetrics(sku, name, sold, revenue));
        }

        return result;
    }

    private static IReadOnlyList<OzonFinanceMetrics> ParseOzonFinanceMetrics(string json)
    {
        using var document = JsonDocument.Parse(json);
        var operations = GetFinanceOperationsArray(document.RootElement);
        if (operations.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<OzonFinanceMetrics>();
        foreach (var operation in operations.EnumerateArray())
        {
            var skus = GetOperationSkus(operation);
            if (skus.Count == 0)
            {
                continue;
            }

            var divisor = skus.Count;
            var commission = Math.Abs(GetDecimal(operation, "sale_commission")) / divisor;
            var services = GetServicesExpense(operation, out var logistics, out var returns);

            foreach (var sku in skus)
            {
                result.Add(new OzonFinanceMetrics(sku, commission, logistics / divisor, services / divisor, returns / divisor));
            }
        }

        return result;
    }

    private static IReadOnlyList<OzonFinanceServiceDebug> ParseOzonFinanceServiceDebug(string json)
    {
        using var document = JsonDocument.Parse(json);
        var operations = GetFinanceOperationsArray(document.RootElement);
        if (operations.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var totals = new Dictionary<string, (decimal Total, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var operation in operations.EnumerateArray())
        {
            if (!operation.TryGetProperty("services", out var services) || services.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var service in services.EnumerateArray())
            {
                var name = FirstNotEmpty(GetString(service, "name"), GetString(service, "service_name"), "unknown");
                var price = Math.Abs(GetDecimal(service, "price"));
                if (!totals.TryGetValue(name, out var current))
                {
                    current = (0, 0);
                }

                totals[name] = (current.Total + price, current.Count + 1);
            }
        }

        return totals
            .OrderByDescending(item => item.Value.Total)
            .Select(item => new OzonFinanceServiceDebug(item.Key, item.Value.Total, item.Value.Count))
            .ToArray();
    }

    private static JsonElement GetFinanceOperationsArray(JsonElement root)
    {
        if (root.TryGetProperty("result", out var result))
        {
            if (result.TryGetProperty("operations", out var operations))
            {
                return operations;
            }

            if (result.TryGetProperty("items", out var items))
            {
                return items;
            }
        }

        if (root.TryGetProperty("operations", out var rootOperations))
        {
            return rootOperations;
        }

        return default;
    }

    private static IReadOnlyList<string> GetOperationSkus(JsonElement operation)
    {
        if (!operation.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return items
            .EnumerateArray()
            .Select(item => FirstNotEmpty(GetString(item, "sku"), GetString(item, "offer_id")))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static decimal GetServicesExpense(JsonElement operation, out decimal logistics, out decimal returns)
    {
        logistics = 0;
        returns = 0;
        if (!operation.TryGetProperty("services", out var services) || services.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var other = 0m;
        foreach (var service in services.EnumerateArray())
        {
            var name = FirstNotEmpty(GetString(service, "name"), GetString(service, "service_name")).ToLowerInvariant();
            var price = Math.Abs(GetDecimal(service, "price"));
            if (price <= 0)
            {
                continue;
            }

            if (IsReturnService(name))
            {
                returns += price;
            }
            else if (IsLogisticsService(name))
            {
                logistics += price;
            }
            else
            {
                other += price;
            }
        }

        return other;
    }

    private static bool IsLogisticsService(string name)
    {
        return name.Contains("достав") ||
               name.Contains("delivery") ||
               name.Contains("last mile") ||
               name.Contains("магистраль") ||
               name.Contains("directflow") ||
               name.Contains("direct_flow") ||
               name.Contains("flowtrans") ||
               name.Contains("flowlogistic") ||
               name.Contains("delivtocustomer") ||
               name.Contains("dropoff") ||
               name.Contains("pickup") ||
               name.Contains("fulfillment") ||
               name.Contains("crossdocking");
    }

    private static bool IsReturnService(string name)
    {
        return name.Contains("возврат") ||
               name.Contains("return") ||
               name.Contains("returnflow") ||
               name.Contains("return_flow") ||
               name.Contains("redistributionreturns");
    }

    private static bool HasNextFinancePage(string json, int page)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("result", out var result))
        {
            return false;
        }

        var pageCount = GetInt(result, "page_count");
        return pageCount > page;
    }

    private static Product EmptyProduct(string sku)
    {
        var value = string.IsNullOrWhiteSpace(sku) ? "unknown" : sku;
        return new Product(value, $"Ozon SKU {value}", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    private static StringContent JsonContent<T>(T value)
    {
        return new StringContent(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");
    }

    private static int CountJsonArray(string json, string propertyName)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("result", out var result) &&
            result.TryGetProperty(propertyName, out var items) &&
            items.ValueKind == JsonValueKind.Array)
        {
            return items.GetArrayLength();
        }

        return 0;
    }

    private static string Trim(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "пустой ответ";
        }

        return value.Length <= 220 ? value : value[..220];
    }

    private static string Preview(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value.Length <= 4 ? "****" : $"{value[..Math.Min(4, value.Length)]}***";
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return "";
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? "",
            JsonValueKind.Number => property.GetRawText(),
            _ => ""
        };
    }

    private static JsonElement GetDataArray(JsonElement root)
    {
        if (root.TryGetProperty("result", out var result) &&
            result.TryGetProperty("data", out var resultData))
        {
            return resultData;
        }

        if (root.TryGetProperty("data", out var data))
        {
            return data;
        }

        return default;
    }

    private static long GetLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var number))
        {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out number))
        {
            return number;
        }

        return 0;
    }

    private static decimal GetPrice(JsonElement element)
    {
        if (!element.TryGetProperty("price", out var priceElement))
        {
            return 0;
        }

        if (priceElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[] { "price", "marketing_price", "min_price" })
            {
                var value = GetDecimal(priceElement, propertyName);
                if (value > 0)
                {
                    return value;
                }
            }
        }

        return GetDecimal(element, "price");
    }

    private static int GetStock(JsonElement element)
    {
        if (!element.TryGetProperty("stocks", out var stocks) || stocks.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        var present = GetInt(stocks, "present");
        var reserved = GetInt(stocks, "reserved");

        return Math.Max(0, present - reserved);
    }

    private static decimal GetDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
        {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String && decimal.TryParse(property.GetString(), out number))
        {
            return number;
        }

        return 0;
    }

    private static decimal GetDecimalAt(JsonElement array, int index)
    {
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() <= index)
        {
            return 0;
        }

        var value = array[index];
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out number))
        {
            return number;
        }

        return 0;
    }

    private static int GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
        {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out number))
        {
            return number;
        }

        return 0;
    }

    private static string FirstNotEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }
}
