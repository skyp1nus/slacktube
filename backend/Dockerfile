# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/SlackTube.Api/SlackTube.Api.csproj src/SlackTube.Api/
RUN dotnet restore src/SlackTube.Api/SlackTube.Api.csproj
COPY . .
RUN dotnet publish src/SlackTube.Api/SlackTube.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SlackTube.Api.dll"]
