using System.Net;
using System.Security.Cryptography;
using System.Text;
using SceneWorkbench.Core;
using SceneWorkbench.Web;

var workspace = Environment.GetEnvironmentVariable("SCENE_WORKBENCH_ROOT") ?? Directory.GetCurrentDirectory();
workspace = SafePath.Canonical(workspace);
if (!File.Exists(Path.Combine(workspace, "SceneWorkbench.slnx"))) throw new InvalidOperationException("請從工作區 scripts/start-web.ps1 啟動。");
var portIndex = Array.IndexOf(args, "--port");
var port = portIndex >= 0 && portIndex + 1 < args.Length ? int.Parse(args[portIndex + 1], System.Globalization.CultureInfo.InvariantCulture) : 5286;
if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
var contentRoot = Path.Combine(workspace, "src", "SceneWorkbench.Web");
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = contentRoot });
builder.WebHost.ConfigureKestrel(options => { options.Listen(IPAddress.Loopback, port); options.Limits.MaxRequestBodySize = 16 * 1024; });
// Development logs belong to the owning console; Windows EventLog is not an application prerequisite.
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.TimestampFormat = "HH:mm:ss ");
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Services.AddSingleton(new WorkbenchService(workspace));
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.MaxDepth = 32);
var app = builder.Build();
var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
var origin = $"http://127.0.0.1:{port}";
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Cache-Control"] = "no-store";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' blob: data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
    if (context.Request.Path == "/index.html") { context.Response.Redirect("/"); return; }
    if (context.Request.Host.Host != "127.0.0.1" || context.Request.Host.Port != port
        || (context.Request.Headers.Origin.Count > 0 && context.Request.Headers.Origin.ToString() != origin)
        || context.Request.Headers["Sec-Fetch-Site"] == "cross-site")
    { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { error = "只接受本機同來源請求。" }); return; }
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        var supplied = context.Request.Headers["X-Workbench-Token"].ToString();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(token)))
        { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { error = "開發服務工作階段已變更，請重新整理頁面；尚未儲存的欄位請先複製保留。" }); return; }
    }
    try { await next(context); }
    catch (OperationCanceledException) { if (!context.Response.HasStarted) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = "作業已取消，請同步目前狀態後繼續。", code = "CANCELLED" }); } }
    catch (Exception ex) when (ex is FormatException or OverflowException)
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new { error = "欄位數值無效或超出可支援範圍，請修正輸入。", code = "INVALID_VALUE" });
    }
    catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException or KeyNotFoundException or YamlDotNet.Core.YamlException)
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = ex is SaveConflictException or StaleSessionException ? 409 : 400;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message, code = ex is SaveConflictException ? "FILE_CONFLICT" : ex is StaleSessionException ? "STALE_SESSION" : "INVALID_OPERATION" });
    }
});
app.MapGet("/", async () => Results.Content((await File.ReadAllTextAsync(Path.Combine(contentRoot, "wwwroot", "index.html"))).Replace("__WORKBENCH_TOKEN__", token, StringComparison.Ordinal), "text/html; charset=utf-8"));
app.UseStaticFiles();
app.MapGet("/api/bootstrap", (WorkbenchService service) => service.Bootstrap);
app.MapGet("/api/state", (WorkbenchService service, CancellationToken ct) => service.StateAsync(ct));
app.MapGet("/api/activity", (WorkbenchService service) => service.Activity);
app.MapPost("/api/cancel", (WorkbenchService service) => { service.Cancel(); return Results.Ok(); });
app.MapPost("/api/project/open", (OpenProjectRequest request, WorkbenchService service, CancellationToken ct) => service.OpenProjectAsync(request, ct));
app.MapPost("/api/project/copy", (CopyRequest request, WorkbenchService service, CancellationToken ct) => service.CopyAsync(request, ct));
app.MapPost("/api/scene/open", (OpenSceneRequest request, WorkbenchService service, CancellationToken ct) => service.OpenSceneAsync(request, ct));
app.MapPost("/api/scene/edit", (FieldEditRequest request, WorkbenchService service, CancellationToken ct) => service.EditAsync(request, ct));
app.MapPost("/api/scene/undo", (RevisionRequest request, WorkbenchService service, CancellationToken ct) => service.HistoryAsync(request, false, ct));
app.MapPost("/api/scene/redo", (RevisionRequest request, WorkbenchService service, CancellationToken ct) => service.HistoryAsync(request, true, ct));
app.MapPost("/api/scene/save", (RevisionRequest request, WorkbenchService service, CancellationToken ct) => service.SaveAsync(request, ct));
app.MapPost("/api/scene/recover", (RevisionRequest request, WorkbenchService service, CancellationToken ct) => service.RecoverAsync(request, ct));
app.MapGet("/api/assets", (string? directory, string? query, int? offset, WorkbenchService service, CancellationToken ct) => service.AssetsAsync(directory ?? "", query ?? "", offset ?? 0, ct));
app.MapGet("/api/asset", (string path, WorkbenchService service, CancellationToken ct) => service.AssetInfoAsync(path, ct));
app.MapGet("/api/image", async (string path, WorkbenchService service, CancellationToken ct) => { var result = await service.ImageAsync(path, ct); return Results.File(result.Bytes, result.Mime); });
app.MapGet("/api/preview", (string path, WorkbenchService service, CancellationToken ct) => service.PreviewAsync(path, ct));
app.MapGet("/api/model", (string path, string? fileId, WorkbenchService service, CancellationToken ct) => service.ModelAsync(path, fileId ?? "0", ct));
app.MapGet("/api/texture", (string path, WorkbenchService service, CancellationToken ct) => service.TextureInfoAsync(path, ct));
app.MapGet("/api/source", async (string path, WorkbenchService service, CancellationToken ct) => { var result = await service.SourceAsync(path, ct); return Results.File(result.Bytes, result.Mime); });
app.MapPost("/api/material/open", (OpenMaterialRequest request, WorkbenchService service, CancellationToken ct) => service.OpenMaterialAsync(request, ct));
app.MapPost("/api/material/close", (CloseMaterialRequest request, WorkbenchService service, CancellationToken ct) => service.CloseMaterialAsync(request, ct));
app.MapPost("/api/material/edit", (MaterialEditRequest request, WorkbenchService service, CancellationToken ct) => service.EditMaterialAsync(request, ct));
app.MapPost("/api/material/edits", (MaterialBatchRequest request, WorkbenchService service, CancellationToken ct) => service.EditMaterialsAsync(request, ct));
app.Run();
