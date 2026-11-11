using MediatR;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Application.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        _logger.LogInformation("Handling {RequestName}", requestName);

        var start = DateTime.UtcNow;
        var response = await next(cancellationToken);
        var elapsed = DateTime.UtcNow - start;

        _logger.LogInformation("Handled {RequestName} in {ElapsedMs}ms", requestName, elapsed.TotalMilliseconds);

        return response;
    }
}
