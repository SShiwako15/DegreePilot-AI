FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY services/api/DegreePilot.Api.csproj services/api/
RUN dotnet restore services/api/DegreePilot.Api.csproj
COPY services/api/ services/api/
COPY data/ data/
RUN dotnet publish services/api/DegreePilot.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "DegreePilot.Api.dll"]
