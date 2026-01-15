import logging
import sys
import json
from typing import Any, Dict

def get_logger(name: str) -> logging.Logger:
    """
    Returns a configured logger instance.
    Configures the root logger if not already configured.
    """
    logger = logging.getLogger(name)
    
    # Check if root logger is already configured to avoid duplicate logs
    if not logging.getLogger().handlers:
        handler = logging.StreamHandler(sys.stdout)
        formatter = logging.Formatter(
            '%(asctime)s - %(name)s - %(levelname)s - %(message)s'
        )
        handler.setFormatter(formatter)
        logging.getLogger().setLevel(logging.INFO)
        logging.getLogger().addHandler(handler)
        
    return logger

def log_json(logger: logging.Logger, level: int, event: str, data: Dict[str, Any] = None):
    """
    Helper to log structured JSON events.
    """
    payload = {"event": event}
    if data:
        payload.update(data)
    
    logger.log(level, json.dumps(payload))
