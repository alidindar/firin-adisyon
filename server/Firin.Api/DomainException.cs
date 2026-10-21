using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Firin.Api;

/// <summary>İş kuralı ihlali. Kullanıcıya gösterilecek Türkçe mesaj taşır.</summary>
public sealed class DomainException(string code, string message, int status = StatusCodes.Status400BadRequest)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public sealed class DomainExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        if (ex is not DomainException de) return false;
        ctx.Response.StatusCode = de.Status;
        var details = new ProblemDetails { Status = de.Status, Title = de.Message };
        details.Extensions["code"] = de.Code;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = ctx, ProblemDetails = details });
    }
}
