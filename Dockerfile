ARG DOTNET_VERSION=10.0

# ---- Build ----
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/LeWiK.Store.Api/LeWiK.Store.Api.csproj src/LeWiK.Store.Api/
COPY src/LeWiK.Store.App/LeWiK.Store.App.csproj src/LeWiK.Store.App/
RUN dotnet restore src/LeWiK.Store.Api/LeWiK.Store.Api.csproj

COPY . .
RUN dotnet publish src/LeWiK.Store.Api/LeWiK.Store.Api.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

# ---- Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "LeWiK.Store.Api.dll"]