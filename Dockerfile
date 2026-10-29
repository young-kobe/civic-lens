# mcr.microsoft.com/dotnet/sdk:10.0.402
FROM mcr.microsoft.com/dotnet/sdk@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src ./src
RUN dotnet restore src/CivicLens.Host --locked-mode \
 && dotnet restore src/CivicLens.Collector --locked-mode \
 && dotnet restore src/CivicLens.PublicApi --locked-mode
RUN dotnet publish src/CivicLens.Host --no-restore -c Release -p:UseAppHost=false -o /out/host \
 && dotnet publish src/CivicLens.Collector --no-restore -c Release -p:UseAppHost=false -o /out/collector \
 && dotnet publish src/CivicLens.PublicApi --no-restore -c Release -p:UseAppHost=false -o /out/search

# mcr.microsoft.com/dotnet/aspnet:10.0.13
FROM mcr.microsoft.com/dotnet/aspnet@sha256:8af87eba5e7cdafb1514d7388be287365da97c132509e84dfe252ffd552398ca AS runtime
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
