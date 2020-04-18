using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Prometheus;

public class RequestMiddleware
{
    private readonly ILogger _logger;
    private readonly RequestDelegate _next;
    private readonly string counter_metric_name = "http_server_request";
    private readonly string histo_metric_name = "http_server_request_duration";

    public RequestMiddleware(
        RequestDelegate next
        , ILoggerFactory loggerFactory
    )
    {
        _next = next;
        _logger = loggerFactory.CreateLogger<RequestMiddleware>();
    }

    public async Task Invoke(HttpContext httpContext)
    {
        var path = httpContext.Request.Path.Value;
        var method = httpContext.Request.Method;
        var labelNames = new[] {"path", "method", "status"};

        var counter = Metrics.CreateCounter(counter_metric_name, "HTTP Requests Total",
            new CounterConfiguration
            {
                LabelNames = labelNames
            });
        var histo = Metrics.CreateHistogram(histo_metric_name, "HTTP Request Duration",
            labelNames);

        var statusCode = 200;
        var stopWatch = Stopwatch.StartNew();
        try
        {
            await _next.Invoke(httpContext);
        }
        catch (Exception)
        {
            statusCode = 500;
            counter.Labels(path, method, statusCode.ToString()).Inc();
            throw;
        }
        finally
        {
            stopWatch.Stop();
        }

        if (path != "/metrics" && path != "/alive" && path != "/ready")
        {
            statusCode = httpContext.Response.StatusCode;
            counter.Labels(path, method, statusCode.ToString()).Inc();
            histo.Labels(path, method, statusCode.ToString())
                .Observe(stopWatch.Elapsed.TotalSeconds);
        }
    }
}

public static class RequestMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RequestMiddleware>();
    }
}