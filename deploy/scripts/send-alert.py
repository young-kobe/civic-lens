#!/usr/bin/env python3
"""
Civic Lens systemd failure alerter.

Invoked by systemd `OnFailure=` when a civic-lens-* unit fails. Sends an
email (via authenticated SMTP — Gmail app-password style) that includes
the service name, the exit status, and the last ~50 journal lines so the
operator can tell at a glance whether it was a timeout, a crash, or an
infrastructure issue.

Configuration (read from /etc/civic-lens.env via the systemd unit):

    CIVIC_ALERT_SMTP_HOST   default: smtp.gmail.com
    CIVIC_ALERT_SMTP_PORT   default: 587
    CIVIC_ALERT_SMTP_USER   required — full sender address (Gmail account)
    CIVIC_ALERT_SMTP_PASS   required — Gmail app password (not the login password)
    CIVIC_ALERT_TO          required — recipient address
    CIVIC_ALERT_FROM        optional — defaults to SMTP_USER

When required vars are missing, the script logs to stderr and exits 0. A
failing alerter must not mask the original service failure or flood
journald with retry attempts — cron will call it again on the next failure.

Usage:
    send-alert.py <service-name>

`<service-name>` is passed by the templated unit as %i (the systemd unit
instance identifier).
"""

from __future__ import annotations

import os
import smtplib
import socket
import subprocess
import sys
from datetime import datetime, timezone
from email.mime.text import MIMEText


JOURNAL_LINES = 50
SMTP_TIMEOUT = 15  # seconds — don't block a system that's already unhealthy


def _fetch_journal(service: str) -> str:
    """Return the last N journal lines for `service`, or a diagnostic string
    if journalctl isn't available (e.g., when running under a sandboxed
    unit that lacks access). Never raises."""
    try:
        result = subprocess.run(
            ["journalctl", "-u", service, "-n", str(JOURNAL_LINES), "--no-pager"],
            check=False,
            capture_output=True,
            text=True,
            timeout=10,
        )
        if result.returncode != 0:
            return f"(journalctl exited {result.returncode}): {result.stderr.strip()}"
        return result.stdout or "(journal empty)"
    except FileNotFoundError:
        return "(journalctl not found on PATH)"
    except Exception as exc:
        return f"(journalctl fetch failed: {type(exc).__name__}: {exc})"


def _fetch_status(service: str) -> str:
    """Return a short `systemctl status` summary — mostly to surface the
    exit-code line and whether it was killed vs exited. Never raises."""
    try:
        result = subprocess.run(
            ["systemctl", "status", "--no-pager", "--lines=0", service],
            check=False,
            capture_output=True,
            text=True,
            timeout=10,
        )
        return result.stdout or result.stderr or "(no output)"
    except Exception as exc:
        return f"(systemctl fetch failed: {type(exc).__name__}: {exc})"


def _build_body(service: str) -> str:
    hostname = socket.gethostname()
    now = datetime.now(timezone.utc).isoformat()
    status = _fetch_status(service)
    journal = _fetch_journal(service)
    return (
        f"Civic Lens systemd unit FAILED\n"
        f"\n"
        f"  Service:  {service}\n"
        f"  Host:     {hostname}\n"
        f"  Time:     {now}\n"
        f"\n"
        f"--- systemctl status (head) ---\n"
        f"{status}\n"
        f"\n"
        f"--- last {JOURNAL_LINES} journal lines ---\n"
        f"{journal}\n"
    )


def main(service: str) -> int:
    host = os.environ.get("CIVIC_ALERT_SMTP_HOST", "smtp.gmail.com")
    port_raw = os.environ.get("CIVIC_ALERT_SMTP_PORT", "587")
    user = os.environ.get("CIVIC_ALERT_SMTP_USER")
    password = os.environ.get("CIVIC_ALERT_SMTP_PASS")
    to_addr = os.environ.get("CIVIC_ALERT_TO")
    from_addr = os.environ.get("CIVIC_ALERT_FROM") or user

    if not (user and password and to_addr):
        # Alerter config not set up yet — exit silently. We don't want a
        # missing-config error to cascade into a second systemd failure.
        print(
            "send-alert: skipping — CIVIC_ALERT_SMTP_USER / CIVIC_ALERT_SMTP_PASS / "
            "CIVIC_ALERT_TO not set",
            file=sys.stderr,
        )
        return 0

    try:
        port = int(port_raw)
    except ValueError:
        print(f"send-alert: invalid CIVIC_ALERT_SMTP_PORT={port_raw!r}", file=sys.stderr)
        return 1

    body = _build_body(service)
    msg = MIMEText(body, "plain", "utf-8")
    msg["Subject"] = f"[Civic Lens] {service} FAILED on {socket.gethostname()}"
    msg["From"] = from_addr
    msg["To"] = to_addr

    try:
        with smtplib.SMTP(host, port, timeout=SMTP_TIMEOUT) as smtp:
            smtp.ehlo()
            smtp.starttls()
            smtp.ehlo()
            smtp.login(user, password)
            smtp.sendmail(from_addr, [to_addr], msg.as_string())
        print(f"send-alert: sent failure notification for {service} to {to_addr}", file=sys.stderr)
        return 0
    except smtplib.SMTPAuthenticationError as exc:
        # Common cause: Gmail account without an app password, or the
        # password was rotated. Log loudly — silent failure here would
        # defeat the whole point.
        print(
            f"send-alert: SMTP auth failed ({exc.smtp_code}): {exc.smtp_error!r}. "
            f"For Gmail, use an App Password: https://myaccount.google.com/apppasswords",
            file=sys.stderr,
        )
        return 1
    except Exception as exc:
        print(f"send-alert: send failed: {type(exc).__name__}: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: send-alert.py <service-name>", file=sys.stderr)
        sys.exit(2)
    sys.exit(main(sys.argv[1]))
