FROM python:3.12-slim
WORKDIR /app
COPY services/ai/requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt
COPY services/ai/app ./app
COPY services/ai/tests ./tests
COPY services/ai/pyproject.toml ./
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 RUFF_CACHE_DIR=/tmp/.ruff_cache
USER 10001
EXPOSE 8000
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000", "--no-access-log", "--limit-concurrency", "8"]
