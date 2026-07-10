# Shared image for the API service and the analyze one-shot (identical deps,
# identical code tree — only the compose `command` differs).
# Build context is the REPO ROOT (the image needs data/), so build with:
#   docker build -f analysis/Dockerfile .
FROM python:3.12-slim
ARG APP_UID=10001
ARG APP_GID=10001
WORKDIR /app
COPY analysis/requirements.txt analysis/requirements.txt
RUN pip install --no-cache-dir --upgrade pip wheel \
    && pip install --no-cache-dir -r analysis/requirements.txt
COPY analysis/ analysis/
# Committed YAMLs + reference seeds resolve relative to the project root
# (job_runner project_root, context_seeds parents[3]) — keep data/ beside
# analysis/ under /app. Runtime data is excluded via .dockerignore.
COPY data/ data/
ENV PYTHONPATH=/app
RUN groupadd --gid ${APP_GID} civic-lens \
    && useradd --uid ${APP_UID} --gid ${APP_GID} --no-create-home civic-lens
USER civic-lens
# No ENTRYPOINT/CMD — each compose service sets its own command
# (uvicorn for api, job_runner for analyze).
