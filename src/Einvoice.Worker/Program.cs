using Einvoice.Worker.Contracts;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<PlaceholderService>();
var app = builder.Build();
app.Run();

internal sealed class PlaceholderService(
    Microsoft.Extensions.Hosting.IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = new InvoiceCreatedData(Guid.Empty, "scaffold", "EUR", 0m);
        lifetime.StopApplication();
        return Task.CompletedTask;
    }
}
