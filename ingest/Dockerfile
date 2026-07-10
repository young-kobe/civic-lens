# Build context is the REPO ROOT (the image needs data/), so build with:
#   docker build -f ingest/Dockerfile .
FROM golang:1.24-alpine AS build
WORKDIR /src
COPY ingest/go.mod ingest/go.sum ./
RUN go mod download
COPY ingest/ ./
RUN CGO_ENABLED=0 go build -trimpath -ldflags "-s -w" -o /out/civic-ingest ./cmd/civic-ingest

FROM alpine:3.20
# Must match the host `civic-lens` UID so writes to the bind-mounted
# /var/lib/civic-lens need no chown (check with `id -u civic-lens` on the box).
ARG APP_UID=990
RUN adduser -D -u ${APP_UID} civic-lens
COPY --from=build /out/civic-ingest /app/civic-ingest
# Committed config (seeds, officials, account lists) + migrations. Runtime
# data (db, raw, cache) is excluded via .dockerignore and bind-mounted.
COPY data/ /app/data/
COPY ingest/docker-entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/entrypoint.sh
USER civic-lens
ENTRYPOINT ["/app/entrypoint.sh"]
