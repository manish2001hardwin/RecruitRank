# RecruitRank MVP

Resume-to-JD matching tool. Three parts, run all three locally:

```
ai_service/   FastAPI — text extraction, parsing, embeddings   (port 8000)
backend/      ASP.NET Core Web API — JD parsing, matching       (port 5000)
frontend/     React + Vite UI                                   (port 5173)
```

## 1. AI Service (Python)

```bash
cd ai_service
python -m venv venv
source venv/bin/activate      # Windows: venv\Scripts\activate
pip install -r requirements.txt

# Tesseract OCR must be installed separately for scanned-PDF fallback:
#   Windows: https://github.com/UB-Mannheim/tesseract/wiki
#   Mac:     brew install tesseract
#   Linux:   sudo apt install tesseract-ocr

uvicorn ai_service:app --port 8000
```

First run downloads the `all-MiniLM-L6-v2` embedding model (~90MB, needs internet
once). Check it's up: `curl http://localhost:8000/health`

## 2. Backend (.NET)

Requires .NET 8 SDK.

```bash
cd backend/RecruitRank.Api
dotnet restore
dotnet run
```

Runs on `http://localhost:5000` by default. It calls the Python service at the
URL in `appsettings.json` (`PythonService:BaseUrl`) — change it there if you
run Python on a different port.

## 3. Frontend (React)

```bash
cd frontend
npm install
npm run dev
```

Opens on `http://localhost:5173`. If your backend runs on a different port,
set `VITE_API_BASE` in a `.env` file in `frontend/`.

## What was fixed vs. the original blueprint

1. **Synonym-aware skill matching** — `skills.json` maps aliases (JS, ReactJS,
   C Sharp, .NET...) to one canonical skill, used identically on both the
   Python and C# sides, so "JS" in a resume matches "JavaScript" in a JD.
2. **Batch embeddings** — the Python service encodes all resume summaries in
   one `model.encode(list, batch_size=32)` call instead of one-by-one, which
   is the actual fix for slow throughput (not a language/framework issue).
3. **Per-file error isolation** — a corrupt/unreadable resume is skipped and
   reported in a `failed` list; it no longer crashes the whole batch.
4. **Python-service-down handling** — if the AI service is unreachable, the
   API returns a clear per-file failure instead of a 500 crash.
5. **Temp file cleanup** — each request gets its own temp folder, deleted in
   a `finally` block whether the request succeeds or fails.
6. **Concrete sigma for experience scoring** — `sigma = (max-min)/2`, falls
   back to `3` years when the JD gives no upper bound, instead of "choose
   based on the range."

## Known limitations (by design, for MVP scope)

- Location and title extraction are heuristic (regex/keyword-based) — flag
  results for recruiter review rather than trusting them blindly for edge
  cases.
- No database — results aren't persisted between searches (add SQLite later
  if you need history/caching).
- Skill taxonomy in `skills.json` is a starter set — extend it as you hit
  resumes/JDs that use terms it doesn't recognize.
