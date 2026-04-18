import uvicorn
import os
import sys
from analysis.src.common.logger import get_logger

# Ensure analysis package is in path
sys.path.append(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))

logger = get_logger("main")

if __name__ == "__main__":
    from analysis.src.common.settings import get_settings
    settings = get_settings()
    # Bootstraps the FastAPI app defined in api/server.py
    logger.info(f"Starting {settings.app_name} on {settings.api_host}:{settings.api_port} (env={settings.environment})...")
    uvicorn.run(
        "analysis.src.api.server:app", 
        host=settings.api_host, 
        port=settings.api_port, 
        reload=(settings.environment == "development")
    )
