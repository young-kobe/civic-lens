"""
Programmatic email alerter for the analysis runner.

Complements `deploy/scripts/send-alert.py` — which is invoked by systemd's
`OnFailure=` directive with only the failing unit's name. This module is
for in-process alerts from Python code: the runner calls it when a pipeline
finishes in a `partial` state (snapshots succeeded but one or more LLM
stages failed or were skipped by the time-budget guard). Systemd doesn't
see a failed exit in that case — the UI has fresh cache — so OnFailure
wouldn't fire, but the operator still wants to know.

Design goals:
- Stdlib only. SMTP is simple enough; no extra dep for a transient function.
- Same env-var contract as the systemd alerter so the operator configures
  once in /etc/civic-lens.env.
- Never raises. A failure in alerting must not mask the underlying pipeline
  signal — the caller already has logs.

Env contract (identical to deploy/scripts/send-alert.py):
    CIVIC_ALERT_SMTP_HOST   default smtp.gmail.com
    CIVIC_ALERT_SMTP_PORT   default 587
    CIVIC_ALERT_SMTP_USER   required — full sender address
    CIVIC_ALERT_SMTP_PASS   required — Gmail app password (NOT login password)
    CIVIC_ALERT_TO          required — recipient address
    CIVIC_ALERT_FROM        optional — defaults to SMTP_USER

When any required var is missing, `send_alert()` returns False without side
effects. The caller is responsible for logging that outcome if it wants to.
"""

from __future__ import annotations

import os
import smtplib
import socket
from email.mime.text import MIMEText

from analysis.src.common.logger import get_logger

logger = get_logger("alerts")

_SMTP_TIMEOUT_SECONDS = 15


def send_alert(subject: str, body: str) -> bool:
    """Send a plain-text email alert. Returns True on success, False on any
    error (including missing config). Never raises.

    The subject is prefixed with "[Civic Lens]" and suffixed with the host
    identifier so the operator can distinguish dev vs. prod at a glance.
    """
    host = os.environ.get("CIVIC_ALERT_SMTP_HOST", "smtp.gmail.com")
    port_raw = os.environ.get("CIVIC_ALERT_SMTP_PORT", "587")
    user = os.environ.get("CIVIC_ALERT_SMTP_USER")
    password = os.environ.get("CIVIC_ALERT_SMTP_PASS")
    to_addr = os.environ.get("CIVIC_ALERT_TO")
    from_addr = os.environ.get("CIVIC_ALERT_FROM") or user

    if not (user and password and to_addr):
        logger.debug(
            "send_alert skipped — CIVIC_ALERT_SMTP_USER / CIVIC_ALERT_SMTP_PASS / "
            "CIVIC_ALERT_TO not configured"
        )
        return False

    try:
        port = int(port_raw)
    except ValueError:
        logger.warning(f"send_alert: invalid CIVIC_ALERT_SMTP_PORT={port_raw!r}")
        return False

    hostname = socket.gethostname()
    msg = MIMEText(body, "plain", "utf-8")
    msg["Subject"] = f"[Civic Lens] {subject} ({hostname})"
    msg["From"] = from_addr
    msg["To"] = to_addr

    try:
        with smtplib.SMTP(host, port, timeout=_SMTP_TIMEOUT_SECONDS) as smtp:
            smtp.ehlo()
            smtp.starttls()
            smtp.ehlo()
            smtp.login(user, password)
            smtp.sendmail(from_addr, [to_addr], msg.as_string())
        logger.info(f"send_alert: sent {subject!r} to {to_addr}")
        return True
    except smtplib.SMTPAuthenticationError as exc:
        logger.warning(
            f"send_alert: SMTP auth failed ({exc.smtp_code}): {exc.smtp_error!r}. "
            f"For Gmail use an App Password: https://myaccount.google.com/apppasswords"
        )
        return False
    except Exception as exc:
        logger.warning(f"send_alert: send failed: {type(exc).__name__}: {exc}")
        return False
