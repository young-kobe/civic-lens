# Infra audit trail

Changes to deployment, hosting, CI/CD, scheduled jobs, secrets handling, hardening.

Scope: `deploy/`, `.github/workflows/`, `run.sh`, `setup-cron.sh`, systemd unit files, firewall/fail2ban/sshd config, Caddyfile, relevant `.env` schema changes.

Out of scope: application code on any layer — only the plumbing that runs it.
