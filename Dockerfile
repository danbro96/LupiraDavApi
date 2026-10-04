# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY nuget.config Directory.Build.props ./
COPY src/LupiraDavApi/LupiraDavApi.csproj src/LupiraDavApi/
RUN --mount=type=secret,id=packages_token,env=PACKAGES_TOKEN dotnet restore src/LupiraDavApi/LupiraDavApi.csproj
COPY . .
RUN dotnet publish src/LupiraDavApi/LupiraDavApi.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# curl: compose healthcheck. libldap2: System.DirectoryServices.Protocols (DAV Basic -> LDAP bind).
# (Base image is Ubuntu 24.04 — the package is libldap2, not Debian's libldap-2.5-0.)
RUN apt-get update && apt-get install -y --no-install-recommends curl libldap2 && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "LupiraDavApi.dll"]
