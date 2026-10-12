using Einvoice.Worker.Api;
using Einvoice.Worker.Domain;
using Einvoice.Worker.Handlers;
using Einvoice.Worker.Idempotency;
using Einvoice.Worker.Messaging;
using Einvoice.Worker.Pdf;
using Einvoice.Worker.Storage;
using RabbitMQ.Client;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

string[] required = ["RabbitMq:Host", "Redis:Configuration", "Minio:Endpoint", "Api:BaseUrl", "Api:ClientSecret"];
string[] missing = required
    .Where(key => string.IsNullOrWhiteSpace(builder.Configuration[key]))
    .ToArray();
if (missing.Length > 0)
    throw new InvalidOperationException(
        $"Missing required configuration: {string.Join(", ", missing)}. See .env.example.");

var rabbitFactory = new ConnectionFactory
{
    HostName = builder.Configuration["RabbitMq:Host"]!,
    Port = int.TryParse(builder.Configuration["RabbitMq:Port"], out var rabbitPort) ? rabbitPort : 5672,
    UserName = builder.Configuration["RabbitMq:Username"] ?? "guest",
    Password = builder.Configuration["RabbitMq:Password"] ?? "guest",
    AutomaticRecoveryEnabled = true,
    NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
};
builder.Services.AddSingleton<IConnection>(_ => rabbitFactory.CreateConnectionAsync().GetAwaiter().GetResult());

builder.Services.AddSingleton<IDatabase>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:Configuration"]!).GetDatabase());
builder.Services.AddSingleton<IProcessingMarker, RedisProcessingMarker>();

builder.Services.AddSingleton(new OAuthClientOptions(
    new Uri(new Uri(builder.Configuration["Api:BaseUrl"]!), "/connect/token").AbsoluteUri,
    builder.Configuration["Api:ClientId"] ?? "einvoice-worker",
    builder.Configuration["Api:ClientSecret"]!,
    builder.Configuration["Api:Scope"] ?? "invoice:read"));
builder.Services.AddHttpClient<OAuthTokenProvider>();
builder.Services.AddHttpClient<IInvoiceApiClient, InvoiceApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"]!);
});

builder.Services.AddSingleton<BackoffCalculator>();
builder.Services.AddSingleton<IPdfInvoiceGenerator, PdfInvoiceGenerator>();
builder.Services.AddSingleton<IDocumentStore>(_ => new MinioDocumentStore(
    builder.Configuration["Minio:Endpoint"]!,
    builder.Configuration["Minio:AccessKey"] ?? "minioadmin",
    builder.Configuration["Minio:SecretKey"] ?? "minioadmin",
    builder.Configuration["Minio:Bucket"] ?? WorkerDefaults.Bucket));
builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
builder.Services.AddSingleton<IInvoiceCreatedHandler, InvoiceCreatedHandler>();

builder.Services.AddHostedService<EventSubscriber>();

var app = builder.Build();
app.Run();
