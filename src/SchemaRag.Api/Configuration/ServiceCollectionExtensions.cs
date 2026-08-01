using System.Threading.RateLimiting;

using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using SchemaRag.Application.Abstractions;
using SchemaRag.Application.Indexing;
using SchemaRag.Application.Prompts;
using SchemaRag.Application.Queries;
using SchemaRag.Application.Sql;
using SchemaRag.Infrastructure.Configuration;
using SchemaRag.Infrastructure.DataSources;
using SchemaRag.Infrastructure.Ollama;
using SchemaRag.Infrastructure.Persistence;
using SchemaRag.Infrastructure.Qdrant;
using SchemaRag.Infrastructure.QueryExecution;
using SchemaRag.Infrastructure.Schema;

namespace SchemaRag.Api.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSchemaRag(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddValidatedOptions(configuration);
        services.AddProblemDetails();
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new()
            {
                Title = "Schema RAG API",
                Version = "v1",
                Description = "A small RAG-based natural-language-to-PostgreSQL sample."
            });
        });
        services.AddRateLimiting();

        services.AddDbContext<SchemaRagDbContext>((serviceProvider, dbOptions) =>
        {
            var database = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            dbOptions.UseNpgsql(
                database.SetupConnectionString,
                npgsqlOptions =>
                {
                    npgsqlOptions.CommandTimeout(database.CommandTimeoutSeconds);
                    npgsqlOptions.EnableRetryOnFailure(3);
                });
        });
        services.AddScoped<DatabaseInitializer>();

        services.AddSingleton<ConfiguredPostgresDataSourceRegistry>();
        services.AddSingleton<IDataSourceRegistry>(serviceProvider =>
            serviceProvider.GetRequiredService<ConfiguredPostgresDataSourceRegistry>());
        services.AddSingleton<ISchemaCatalog, PostgresSchemaCatalog>();
        services.AddSingleton<IGeneratedQueryExecutor, NpgsqlGeneratedQueryExecutor>();

        services.AddHttpClient<IChatCompletionService, OllamaChatCompletionService>(
            ConfigureOllamaClient);
        services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>(
            ConfigureOllamaClient);
        services.AddHttpClient<ISchemaVectorStore, QdrantSchemaVectorStore>(
            ConfigureQdrantClient);

        services.AddSingleton(serviceProvider =>
        {
            var database = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            return new SqlPromptBuilder(database.MaxRows);
        });
        services.AddSingleton(serviceProvider =>
        {
            var database = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            return new SqlQueryValidator(database.MaxRows);
        });
        services.AddSingleton(serviceProvider =>
        {
            var rag = serviceProvider.GetRequiredService<IOptions<RagOptions>>().Value;
            return new QueryPipelineOptions(
                rag.SchemaRetrievalCount,
                rag.MaxTablesPerIndex);
        });
        services.AddSingleton(serviceProvider =>
        {
            var rag = serviceProvider.GetRequiredService<IOptions<RagOptions>>().Value;
            return new SchemaIndexingOptions(
                rag.MaxTablesPerIndex,
                rag.EmbeddingConcurrency);
        });
        services.AddScoped<NaturalLanguageQueryService>();
        services.AddScoped<SchemaIndexingService>();

        return services;
    }

    private static void AddValidatedOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.SetupConnectionString),
                "Database:SetupConnectionString is required.")
            .Validate(options => options.CommandTimeoutSeconds is >= 1 and <= 60,
                "Database:CommandTimeoutSeconds must be between 1 and 60.")
            .Validate(options => options.MaxRows is >= 1 and <= 1000,
                "Database:MaxRows must be between 1 and 1000.")
            .ValidateOnStart();

        services.AddOptions<DataSourcesOptions>()
            .Bind(configuration.GetSection(DataSourcesOptions.SectionName))
            .Validate(options => options.Profiles.Count is >= 1 and <= 20,
                "DataSources:Profiles must contain between 1 and 20 entries.")
            .Validate(options =>
                    IsSafeDataSourceId(options.DefaultId) &&
                    options.Profiles.ContainsKey(options.DefaultId),
                "DataSources:DefaultId must identify a configured profile.")
            .Validate(options => options.Profiles.Keys.All(IsSafeDataSourceId),
                "Data source IDs may contain only lowercase ASCII letters, digits, underscores, and hyphens.")
            .Validate(options => options.Profiles.Values.All(profile =>
                    !string.IsNullOrWhiteSpace(profile.DisplayName) && profile.DisplayName.Length <= 100),
                "Every data source profile needs a display name of at most 100 characters.")
            .Validate(options => options.Profiles.Values.All(HasSafeIncludedSchemas),
                "Every data source profile needs 1-20 distinct non-system PostgreSQL schemas.")
            .Validate(options => options.Profiles.Values.All(HasValidPostgresConnection),
                "Every data source profile needs a PostgreSQL connection string with host, database, and username.")
            .ValidateOnStart();

        services.AddOptions<OllamaOptions>()
            .Bind(configuration.GetSection(OllamaOptions.SectionName))
            .Validate(options => IsHttpUri(options.BaseUrl),
                "Ollama:BaseUrl must be an absolute HTTP(S) URL.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ChatModel),
                "Ollama:ChatModel is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.EmbeddingModel),
                "Ollama:EmbeddingModel is required.")
            .Validate(options => options.EmbeddingDimensions > 0,
                "Ollama:EmbeddingDimensions must be positive.")
            .Validate(options => options.RequestTimeoutSeconds is >= 1 and <= 600,
                "Ollama:RequestTimeoutSeconds must be between 1 and 600.")
            .ValidateOnStart();

        services.AddOptions<QdrantOptions>()
            .Bind(configuration.GetSection(QdrantOptions.SectionName))
            .Validate(options => IsHttpUri(options.BaseUrl),
                "Qdrant:BaseUrl must be an absolute HTTP(S) URL.")
            .Validate(options => IsSafeCollectionName(options.CollectionName),
                "Qdrant:CollectionName may contain only letters, digits, underscores, and hyphens.")
            .Validate(options => options.RequestTimeoutSeconds is >= 1 and <= 120,
                "Qdrant:RequestTimeoutSeconds must be between 1 and 120.")
            .ValidateOnStart();

        services.AddOptions<RagOptions>()
            .Bind(configuration.GetSection(RagOptions.SectionName))
            .Validate(options => options.SchemaRetrievalCount is >= 3 and <= 5,
                "Rag:SchemaRetrievalCount must be between 3 and 5.")
            .Validate(options => options.MaxQuestionLength is >= 20 and <= 2000,
                "Rag:MaxQuestionLength must be between 20 and 2000.")
            .Validate(options => options.MaxTablesPerIndex is >= 1 and <= 500,
                "Rag:MaxTablesPerIndex must be between 1 and 500.")
            .Validate(options => options.EmbeddingConcurrency is >= 1 and <= 16,
                "Rag:EmbeddingConcurrency must be between 1 and 16.")
            .ValidateOnStart();
    }

    private static void AddRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
            options.AddFixedWindowLimiter("schema-index", limiter =>
            {
                limiter.PermitLimit = 5;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
        });
    }

    private static void ConfigureOllamaClient(
        IServiceProvider serviceProvider,
        HttpClient client)
    {
        var options = serviceProvider.GetRequiredService<IOptions<OllamaOptions>>().Value;
        client.BaseAddress = EnsureTrailingSlash(options.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
    }

    private static void ConfigureQdrantClient(
        IServiceProvider serviceProvider,
        HttpClient client)
    {
        var options = serviceProvider.GetRequiredService<IOptions<QdrantOptions>>().Value;
        client.BaseAddress = EnsureTrailingSlash(options.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
    }

    private static Uri EnsureTrailingSlash(string value) =>
        new(value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/");

    private static bool IsHttpUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static bool IsSafeCollectionName(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.All(character =>
            char.IsLetterOrDigit(character) || character is '_' or '-');

    private static bool HasSafeIncludedSchemas(PostgresDataSourceOptions profile)
    {
        return profile.IncludedSchemas.Length is >= 1 and <= 20 &&
            profile.IncludedSchemas.Distinct(StringComparer.Ordinal).Count() == profile.IncludedSchemas.Length &&
            profile.IncludedSchemas.All(IsSafeSchemaName);
    }

    private static bool HasValidPostgresConnection(PostgresDataSourceOptions profile)
    {
        try
        {
            var settings = new Npgsql.NpgsqlConnectionStringBuilder(profile.ReadOnlyConnectionString);
            return !string.IsNullOrWhiteSpace(settings.Host) &&
                !string.IsNullOrWhiteSpace(settings.Database) &&
                !string.IsNullOrWhiteSpace(settings.Username);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsSafeDataSourceId(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 64 &&
        char.IsAsciiLetterOrDigit(value[0]) &&
        value.All(character =>
            char.IsAsciiDigit(character) ||
            (character is >= 'a' and <= 'z') ||
            character is '_' or '-');

    private static bool IsSafeSchemaName(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 63 &&
        (char.IsAsciiLetter(value[0]) || value[0] == '_') &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_') &&
        !value.StartsWith("pg_", StringComparison.OrdinalIgnoreCase) &&
        !value.Equals("information_schema", StringComparison.OrdinalIgnoreCase);
}