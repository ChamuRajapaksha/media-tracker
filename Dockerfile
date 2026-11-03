FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MediaTracker.Api/MediaTracker.Api.csproj MediaTracker.Api/
RUN dotnet restore MediaTracker.Api/MediaTracker.Api.csproj

COPY MediaTracker.Api/ MediaTracker.Api/
RUN dotnet publish MediaTracker.Api/MediaTracker.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
USER $APP_UID

ENTRYPOINT ["dotnet", "MediaTracker.Api.dll"]
