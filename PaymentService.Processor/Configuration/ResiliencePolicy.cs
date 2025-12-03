using Microsoft.Extensions.Logging;
using Npgsql;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace PaymentService.Processor.Configuration;

public static class ResiliencePolicy
{
    private static AsyncRetryPolicy? _retryPolicy;
    private static AsyncCircuitBreakerPolicy? _circuitBreakerPolicy;

    public static AsyncPolicy GetPostgresPolicy(ILogger logger)
    {

        var policyBuilder = Policy
            .Handle<NpgsqlException>() 
            .Or<SocketException>()     
            .Or<TimeoutException>()    
            .OrInner<SocketException>()
            .Or<Exception>();

        _circuitBreakerPolicy ??= policyBuilder
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (ex, time) =>
                {
                    logger.LogError(ex, "[CIRCUIT BREAKER] Circuito ABERTO por {Duration}s. Falha crítica: {Message}", time.TotalSeconds, ex.Message);
                },
                onReset: () => logger.LogInformation("[CIRCUIT BREAKER] Circuito FECHADO. Sistema recuperado."),
                onHalfOpen: () => logger.LogWarning("[CIRCUIT BREAKER] Circuito MEIO-ABERTO. Testando recuperação...")
            );

        _retryPolicy ??= policyBuilder
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (ex, time, retryCount, context) =>
                {
                    logger.LogWarning(ex, "[RETRY] Tentativa {RetryCount} falhou. Esperando {Duration}s. Erro: {Message}", retryCount, time.TotalSeconds, ex.Message);
                }
            );
        return _retryPolicy.WrapAsync(_circuitBreakerPolicy);
    }
}