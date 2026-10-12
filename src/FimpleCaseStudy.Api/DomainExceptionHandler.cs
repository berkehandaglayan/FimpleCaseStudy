using FimpleCaseStudy.Domain;
using Microsoft.AspNetCore.Diagnostics;

namespace FimpleCaseStudy.Api;

internal sealed class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is BadHttpRequestException badRequest)
        {
            await Results.Problem(statusCode: badRequest.StatusCode, title: "Invalid request",
                detail: "The request body or parameters are invalid.",
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_REQUEST" }).ExecuteAsync(context);
            return true;
        }
        if (exception is not DomainException error) return false;
        var status = error.Kind switch
        {
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        await Results.Problem(statusCode: status, title: error.Code, detail: error.Message,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code }).ExecuteAsync(context);
        return true;
    }
}
