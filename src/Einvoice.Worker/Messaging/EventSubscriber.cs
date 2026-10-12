using System.Globalization;
using Einvoice.Worker.Domain;
using Einvoice.Worker.Handlers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Einvoice.Worker.Messaging;

public sealed class EventSubscriber(
    IConnection connection,
    IInvoiceCreatedHandler handler,
    BackoffCalculator backoff,
    ILogger<EventSubscriber> logger) : BackgroundService
{
    private IChannel _channel = null!;
    private CancellationToken _stopping = CancellationToken.None;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stopping = stoppingToken;

        _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await RabbitMqTopology.DeclareAsync(_channel, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnReceivedAsync;
        await _channel.BasicConsumeAsync(
            queue: WorkerDefaults.Queue, autoAck: false, consumer: consumer,
            cancellationToken: stoppingToken);

        logger.LogInformation("Consuming {Queue}", WorkerDefaults.Queue);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var attempt = AttemptResolver.Resolve(AsReadOnly(ea.BasicProperties.Headers));

        HandleResult result;
        try
        {
            var envelope = CloudEventParser.Parse(ea.Body);
            result = await handler.HandleAsync(envelope, attempt, _stopping);
        }
        catch (PermanentProcessingException ex)
        {
            logger.LogWarning(ex, "Permanent failure on delivery {Tag} (attempt {Attempt})", ea.DeliveryTag, attempt);
            result = HandleResult.Permanent;
        }
        catch (RetryableProcessingException ex)
        {
            logger.LogWarning(ex, "Retryable failure on delivery {Tag} (attempt {Attempt})", ea.DeliveryTag, attempt);
            result = HandleResult.Retryable;
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return; // shutting down: leave unacked for redelivery
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected failure on delivery {Tag}", ea.DeliveryTag);
            result = HandleResult.Retryable;
        }

        try
        {
            switch (result)
            {
                case HandleResult.Success:
                    await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: _stopping);
                    break;

                case HandleResult.Permanent:
                    await SendToDeadLetterAsync(ea);
                    await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: _stopping);
                    break;

                case HandleResult.Retryable when attempt >= WorkerDefaults.MaxRetries:
                    logger.LogWarning("Max retries reached for delivery {Tag}; parking in DLQ", ea.DeliveryTag);
                    await SendToDeadLetterAsync(ea);
                    await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: _stopping);
                    break;

                case HandleResult.Retryable:
                    await SendToRetryAsync(ea, backoff.Delay(attempt), attempt + 1);
                    await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: _stopping);
                    break;
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // shutting down mid-routing: keep message unacked
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to route delivery {Tag}; requeueing", ea.DeliveryTag);
            try
            {
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true,
                    cancellationToken: CancellationToken.None);
            }
            catch (Exception nackEx)
            {
                logger.LogWarning(nackEx, "Could not nack delivery {Tag}", ea.DeliveryTag);
            }
        }
    }

    private async Task SendToRetryAsync(BasicDeliverEventArgs ea, TimeSpan delay, int nextAttempt)
    {
        var headers = CopyHeaders(ea);
        headers[AttemptResolver.AttemptHeader] = nextAttempt;
        var properties = new BasicProperties
        {
            DeliveryMode = DeliveryModes.Persistent,
            Expiration = ((long)delay.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
            Headers = headers
        };
        await _channel.BasicPublishAsync(
            exchange: string.Empty, routingKey: WorkerDefaults.RetryQueue, mandatory: false,
            basicProperties: properties, body: ea.Body, cancellationToken: _stopping);
    }

    private async Task SendToDeadLetterAsync(BasicDeliverEventArgs ea)
    {
        var properties = new BasicProperties
        {
            DeliveryMode = DeliveryModes.Persistent,
            Headers = CopyHeaders(ea)
        };
        await _channel.BasicPublishAsync(
            exchange: string.Empty, routingKey: WorkerDefaults.DeadLetterQueue, mandatory: false,
            basicProperties: properties, body: ea.Body, cancellationToken: _stopping);
    }

    private static Dictionary<string, object?> CopyHeaders(BasicDeliverEventArgs ea) =>
        ea.BasicProperties.Headers is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(ea.BasicProperties.Headers);

    private static IReadOnlyDictionary<string, object?>? AsReadOnly(IDictionary<string, object?>? headers) =>
        headers as IReadOnlyDictionary<string, object?>
        ?? (headers is null ? null : new Dictionary<string, object?>(headers));
}
