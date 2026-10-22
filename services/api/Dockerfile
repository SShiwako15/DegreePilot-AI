FROM python:3.12-slim
WORKDIR /workspace
COPY services/api/requirements.txt /tmp/requirements.txt
RUN pip install --no-cache-dir -r /tmp/requirements.txt
COPY services/api/degreepilot_api ./services/api/degreepilot_api
COPY services/api/alembic.ini ./services/api/alembic.ini
COPY services/api/migrations ./services/api/migrations
COPY data ./data
COPY policies ./policies
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 PYTHONPATH=/workspace/services/api
USER 10001
EXPOSE 8080
CMD ["python", "-m", "uvicorn", "degreepilot_api.main:app", "--host", "0.0.0.0", "--port", "8080", "--no-access-log", "--no-proxy-headers"]
