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
        var (status, code, message) = ex switch
        {
            DomainException de => (de.Status, de.Code, de.Message),
            BadHttpRequestException bad => (bad.StatusCode, "istek_gecersiz", "İstek okunamadı. Sayfayı yenileyip tekrar deneyin."),
            _ => (0, "", ""),
        };
        if (status == 0) return false;
        ctx.Response.StatusCode = status;
        var details = new ProblemDetails { Status = status, Title = message };
        details.Extensions["code"] = code;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = ctx, ProblemDetails = details });
    }
}
