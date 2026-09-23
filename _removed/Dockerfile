# Build from the solution root:
#   docker build -f src/K3SManager.Web/Dockerfile -t k3smanager:1.0.0 .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/K3SManager.Core/K3SManager.Core.csproj src/K3SManager.Core/
COPY src/K3SManager.Kubernetes/K3SManager.Kubernetes.csproj src/K3SManager.Kubernetes/
COPY src/K3SManager.Data/K3SManager.Data.csproj src/K3SManager.Data/
COPY src/K3SManager.Web/K3SManager.Web.csproj src/K3SManager.Web/
RUN dotnet restore src/K3SManager.Web/K3SManager.Web.csproj
COPY . .
RUN dotnet publish src/K3SManager.Web/K3SManager.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "K3SManager.Web.dll"]
