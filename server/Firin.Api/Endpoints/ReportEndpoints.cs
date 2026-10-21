using Firin.Api.Auth;
using Firin.Api.Reports;

namespace Firin.Api.Endpoints;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/reports").RequireAuthorization(AuthSetup.OwnerPolicy);

        // Tarihler string alınır: DateOnly? bağlaması hatalı girdide kodsuz 400/500 verir.
        g.MapGet("/summary", (string? from, string? to, ReportService r) =>
        {
            var (f, t) = r.ResolveRange(from, to);
            return r.SummaryAsync(f, t);
        });

        g.MapGet("/orders", (string? from, string? to, ReportService r) =>
        {
            var (f, t) = r.ResolveRange(from, to);
            return r.OrdersAsync(f, t);
        });

        g.MapGet("/orders/{id:int}", (int id, ReportService r) => r.OrderAuditAsync(id));
    }
}
