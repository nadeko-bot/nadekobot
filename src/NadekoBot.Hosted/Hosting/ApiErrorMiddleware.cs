using Microsoft.AspNetCore.Http;

namespace NadekoBot.Hosted;

// Gives framework replies the {"error":code} body of endpoint errors, and audits each request which changes state.
public sealed class ApiErrorMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http)
    {
        try
        {
            await next(http);
        }
        catch (BadHttpRequestException ex) when (!http.Response.HasStarted)
        {
            Log.Warning("Bad API request on {Method} {Path}: {Error}", http.Request.Method, http.Request.Path, ex.Message);
            http.Response.Clear();
            http.Response.StatusCode = ex.StatusCode;
        }
        catch (Exception ex) when (!http.Response.HasStarted && !http.RequestAborted.IsCancellationRequested)
        {
            Log.Error(ex, "Unhandled API error on {Method} {Path}", http.Request.Method, http.Request.Path);
            http.Response.Clear();
            http.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }

        if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method))
        {
            Log.Information("API {Method} {Path} -> {Status}",
                http.Request.Method,
                http.Request.Path,
                http.Response.StatusCode);
        }

        if (http.Response.HasStarted || http.Response.StatusCode < StatusCodes.Status400BadRequest)
            return;

        await http.Response.WriteAsJsonAsync(
            new ErrorDto(ApiErrors.CodeFor(http.Response.StatusCode)),
            ApiJsonContext.Default.ErrorDto);
    }
}
