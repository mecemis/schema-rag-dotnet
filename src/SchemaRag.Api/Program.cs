using SchemaRag.Api.Configuration;
using SchemaRag.Api.Endpoints;
using SchemaRag.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSchemaRag(builder.Configuration);

var app = builder.Build();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";

    if (context.Request.Path == "/" || context.Request.Path == "/index.html")
    {
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self'; " +
            "img-src 'self' data:; connect-src 'self'; base-uri 'none'; " +
            "form-action 'self'; frame-ancestors 'none'";
    }

    await next(context);
});
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        // This local demo evolves quickly and serves non-fingerprinted assets.
        // Prevent an older HTML/JavaScript pair from leaving the UI controls unwired.
        context.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Context.Response.Headers.Pragma = "no-cache";
        context.Context.Response.Headers.Expires = "0";
    }
});
app.UseRateLimiter();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Schema RAG API v1");
    options.DisplayRequestDuration();
});

app.MapHealthEndpoints();
app.MapDataSourceEndpoints();
app.MapSchemaEndpoints();
app.MapQueryEndpoints();

if (!app.Environment.IsEnvironment("Testing"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync(CancellationToken.None);
}

await app.RunAsync();

public partial class Program;