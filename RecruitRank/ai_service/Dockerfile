FROM python:3.11-slim

# Tesseract + poppler needed for OCR fallback and PDF-to-image conversion
RUN apt-get update && apt-get install -y --no-install-recommends \
    tesseract-ocr \
    poppler-utils \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY . .

# Model downloads on first startup and gets cached in the image layer/volume
EXPOSE 8000
CMD ["uvicorn", "ai_service:app", "--host", "0.0.0.0", "--port", "8000"]
