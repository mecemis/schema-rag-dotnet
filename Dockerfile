FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/SchemaRag.Domain/SchemaRag.Domain.csproj src/SchemaRag.Domain/
COPY src/SchemaRag.Application/SchemaRag.Application.csproj src/SchemaRag.Application/
COPY src/SchemaRag.Infrastructure/SchemaRag.Infrastructure.csproj src/SchemaRag.Infrastructure/
COPY src/SchemaRag.Api/SchemaRag.Api.csproj src/SchemaRag.Api/
RUN dotnet restore src/SchemaRag.Api/SchemaRag.Api.csproj

COPY src/ src/
RUN dotnet publish src/SchemaRag.Api/SchemaRag.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS final
WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "SchemaRag.Api.dll"]
