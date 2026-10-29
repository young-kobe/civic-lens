# mcr.microsoft.com/dotnet/sdk:10.0.403
FROM mcr.microsoft.com/dotnet/sdk@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src ./src
RUN dotnet restore src/CivicLens.Host --locked-mode \
 && dotnet restore src/CivicLens.Collector --locked-mode \
 && dotnet restore src/CivicLens.PublicApi --locked-mode
RUN dotnet publish src/CivicLens.Host --no-restore -c Release -p:UseAppHost=false -o /out/host \
 && dotnet publish src/CivicLens.Collector --no-restore -c Release -p:UseAppHost=false -o /out/collector \
 && dotnet publish src/CivicLens.PublicApi --no-restore -c Release -p:UseAppHost=false -o /out/search

# mcr.microsoft.com/dotnet/aspnet:10.0.14
FROM mcr.microsoft.com/dotnet/aspnet@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee AS runtime
RUN apt-get update \
 && apt-get install --yes --no-install-recommends libgssapi-krb5-2 \
 && rm -rf /var/lib/apt/lists/*
RUN groupadd --gid 10001 civic-lens \
 && useradd --uid 10001 --gid 10001 --no-create-home --shell /usr/sbin/nologin civic-lens
LABEL org.opencontainers.image.source="https://github.com/young-kobe/civic-lens"
USER 10001:10001

FROM runtime AS host
COPY --from=build /out/host /app/host
COPY --from=build /out/collector /app/collector
WORKDIR /app/host
ENTRYPOINT ["dotnet", "CivicLens.Host.dll"]

FROM runtime AS search
COPY --from=build /out/search /app/search
WORKDIR /app/search
ENTRYPOINT ["dotnet", "CivicLens.PublicApi.dll"]
