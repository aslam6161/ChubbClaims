# Build context: ChubbClaims_BE/
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (cached layer as long as project files don't change).
COPY ChubbClaims.slnx ./
COPY ChubbClaims.Domain/ChubbClaims.Domain.csproj ChubbClaims.Domain/
COPY ChubbClaims.Repository/ChubbClaims.Repository.csproj ChubbClaims.Repository/
COPY ChubbClaims.Service/ChubbClaims.Service.csproj ChubbClaims.Service/
COPY ChubbClaims.Messaging/ChubbClaims.Messaging.csproj ChubbClaims.Messaging/
COPY ChubbClaims.Presentation/ChubbClaims.Presentation.csproj ChubbClaims.Presentation/
RUN dotnet restore ChubbClaims.Presentation/ChubbClaims.Presentation.csproj

COPY . .
RUN dotnet publish ChubbClaims.Presentation/ChubbClaims.Presentation.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# The folder for claim documents (a volume in compose) must belong to the non-root user the API
# runs as, or the first upload fails with "access denied".
USER root
RUN mkdir -p /app/data/documents && chown -R $APP_UID /app/data
USER $APP_UID
# aspnet images listen on 8080 by default.
EXPOSE 8080
ENTRYPOINT ["dotnet", "ChubbClaims.Presentation.dll"]
