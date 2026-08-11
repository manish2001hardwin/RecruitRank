# RecruitRank

**Paste a job description, upload a pile of resumes, get a ranked shortlist with evidence — not a black-box score.**

RecruitRank is a self-hosted resume-to-JD matching tool. It extracts structured
candidate profiles from PDF/DOCX resumes (with OCR fallback for scanned PDFs),
matches them against a job description using semantic (embedding-based)
similarity plus hard-filter business rules, and shows *why* each candidate
ranked where they did — matched skills, missing skills, experience fit, and
location — so a recruiter can trust and audit the result instead of just
trusting a number.

No external AI API calls, no per-resume cost, no data leaving your network —
everything runs as three containers on your own machine or server.

---

## How It Works

```
┌─────────────┐        ┌──────────────────────┐        ┌────────────────────────┐
│   Frontend   │  HTTP  │   Backend (.NET 8)    │  HTTP  │  AI Service (Python)   │
│ React + Vite │ ─────> │  ASP.NET Core Web API │ ─────> │  FastAPI               │
│  (port 5173) │        │     (port 5000)        │        │    (port 8000)         │
└─────────────┘        └──────────────────────┘        └────────────────────────┘
                                  │                                  │
                          JD parsing (regex +                text extraction
                          shared skill taxonomy),             (PDF/DOCX + OCR),
                          hard-filter + tie-break              regex field parsing,
                          ranking, evidence generation          synonym-aware skills,
                                                                 batch sentence embeddings
```

1. **Frontend** — recruiter pastes a JD and uploads resumes (PDF, DOCX, or a
   ZIP of them). Sends everything as one `multipart/form-data` request.
2. **Backend** (`SearchController`) — saves uploads to a per-request temp
   folder (auto-cleaned in a `finally` block), unzips any `.zip` files,
   parses the JD into structured requirements, and forwards the resume file
   paths to the AI service.
3. **AI Service** — extracts raw text from each file (`pdfplumber` for PDFs,
   falling back to Tesseract OCR if a PDF looks scanned; `docx2txt` for
   Word docs), regex-parses name/email/phone/skills/experience/location,
   and batch-embeds every resume summary in one `sentence-transformers`
   call (`all-MiniLM-L6-v2`).
4. **Backend** (`MatchEngine`) — applies hard filters (must have every
   mandatory skill, meet minimum experience), scores survivors by cosine
   similarity against the JD's embedding, and breaks ties with a
   deterministic chain: skill coverage % → experience relevance (Gaussian
   curve centered on the JD's ideal experience) → job-title word overlap.
5. **Frontend** — renders the ranked list as evidence cards: matched/missing
   skills, an experience verdict, location status, and overall score.

---

## Features

- **Batch resume ingestion** — individual PDF/DOCX files or a ZIP of many, up
  to 200MB per request.
- **OCR fallback** — scanned (image-only) PDFs are still readable via
  Tesseract.
- **Synonym-aware skill matching** — a single `skills.json` taxonomy (shared
  identically by the C# and Python sides) maps aliases like `JS`, `ReactJS`,
  `C Sharp`, `.NET` to one canonical skill, so a JD asking for "JavaScript"
  correctly matches a resume that says "JS".
- **Semantic ranking, not just keyword matching** — candidates are scored by
  embedding similarity to the JD, not a simple skill-count.
- **Evidence-based results** — every ranked candidate shows matched skills,
  missing skills, an experience verdict, and location status, so results are
  auditable rather than opaque.
- **Per-file failure isolation** — one corrupt/unreadable resume in a batch
  is skipped and reported, not a reason to fail the whole request.
- **Resilient to AI-service downtime** — if the Python service is
  unreachable, the API reports a clear per-file failure instead of a 500.
- **Fully self-hosted** — no cloud AI API, no per-resume cost, no data
  leaves your network.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | React 18, Vite |
| Backend | ASP.NET Core 8 Web API (C#) |
| AI Service | FastAPI (Python), `sentence-transformers`, `pdfplumber`, `pytesseract`, `docx2txt` |
| Deployment | Docker, Docker Compose, Nginx (reverse proxy + static hosting) |

---

## Project Structure

```
RecruitRank/
├── ai_service/                 # FastAPI — text extraction, parsing, embeddings
│   ├── ai_service.py
│   ├── skills.json             # canonical skill -> aliases taxonomy
│   ├── requirements.txt
│   └── Dockerfile
├── backend/
│   └── RecruitRank.Api/        # ASP.NET Core Web API — JD parsing, ranking
│       ├── Controllers/SearchController.cs
│       ├── Services/
│       │   ├── JdParser.cs         # JD text -> structured requirements
│       │   ├── MatchEngine.cs      # hard filters + semantic ranking + tie-breaks
│       │   └── PythonServiceClient.cs
│       ├── Models/Models.cs
│       ├── skills.json         # same taxonomy as ai_service, kept in sync
│       └── Dockerfile
├── frontend/                   # React + Vite UI
│   ├── src/App.jsx
│   ├── src/components/CandidateCard.jsx
│   ├── nginx.conf              # proxies /api/* to backend in production
│   └── Dockerfile
├── docker-compose.yml          # runs all three services together
├── DEPLOYMENT.md               # step-by-step guide for a local/office server
└── README.md
```

---

## Quick Start (Docker — recommended)

Requires only [Docker](https://www.docker.com/products/docker-desktop/) installed.

```bash
git clone <your-repo-url>
cd RecruitRank
docker compose up --build -d
```

Open **http://localhost** in a browser. That's it — Nginx serves the React
app and proxies all `/api/*` calls to the backend automatically.

> First build downloads the embedding model (~90MB) once; it's cached in a
> Docker volume afterward. See [`DEPLOYMENT.md`](./DEPLOYMENT.md) for a full
> walkthrough of deploying this on an office server, plus a troubleshooting
> table.

---

## Local Development (without Docker)

Run all three services locally, each in its own terminal.

### 1. AI Service (Python)

```bash
cd ai_service
python -m venv venv
source venv/bin/activate        # Windows: venv\Scripts\activate
pip install -r requirements.txt
```

Tesseract OCR must be installed separately for the scanned-PDF fallback:

| OS | Install |
|---|---|
| Windows | [UB-Mannheim build](https://github.com/UB-Mannheim/tesseract/wiki) |
| macOS | `brew install tesseract` |
| Linux | `sudo apt install tesseract-ocr` |

```bash
uvicorn ai_service:app --port 8000
```

First run downloads the `all-MiniLM-L6-v2` embedding model (~90MB, needs
internet once). Verify it's up: `curl http://localhost:8000/health`

### 2. Backend (.NET 8)

```bash
cd backend/RecruitRank.Api
dotnet restore
dotnet run
```

Runs on `http://localhost:5000`. Calls the Python service at the URL in
`appsettings.json` (`PythonService:BaseUrl`) — change it there if Python runs
on a different port.

### 3. Frontend (React)

```bash
cd frontend
npm install
npm run dev
```

Opens on `http://localhost:5173`. If the backend runs on a different port,
set `VITE_API_BASE` in a `.env` file inside `frontend/`.

---

## API Reference

### `POST /api/search`

`multipart/form-data` request.

| Field | Type | Description |
|---|---|---|
| `jdText` | string | Full job description text |
| `files` | file[] | One or more `.pdf` / `.docx` files, or a `.zip` containing them |

**Response**

```json
{
  "ranked": [
    {
      "candidate": {
        "name": "Jane Doe",
        "email": "jane@example.com",
        "phone": "+91 98765 43210",
        "skills": ["c#", "asp.net core", "sql"],
        "total_experience": 4.5,
        "location": "Bengaluru",
        "current_title": "Backend Developer"
      },
      "evidence": {
        "matchedSkills": ["c#", "asp.net core"],
        "missingSkills": ["docker"],
        "experienceVerdict": "4.5 years (requirement: 3+ years)",
        "locationStatus": "Bengaluru (open to relocate)",
        "overallScore": 82.4
      }
    }
  ],
  "failed": [
    { "file": "corrupt_resume.pdf", "reason": "..." }
  ]
}
```

### `GET /health` (AI service, port 8000)

Returns `{"status": "ok", "model": "all-MiniLM-L6-v2"}` once the embedding
model has finished loading.

---

## Ranking Logic

`MatchEngine.Rank()` in the backend runs candidates through, in order:

1. **Hard filters** — must have every mandatory skill from the JD, meet
   minimum experience, and (if "strict location" is enabled) match the JD's
   location exactly. Anyone who fails these isn't ranked at all.
2. **Semantic score** — cosine similarity between the candidate's resume
   embedding and the JD's embedding (both from the same `all-MiniLM-L6-v2`
   model), primary sort key.
3. **Tie-break chain** (applied in order, each only breaking ties left by
   the previous): mandatory-skill coverage % → experience relevance (a
   Gaussian curve centered on the JD's ideal experience, with the curve's
   tolerance/`sigma` derived from the JD's own min–max range, or a 3-year
   fallback when the JD gives no upper bound) → job-title word overlap.

---

## Configuration

| Variable | Where | Default | Purpose |
|---|---|---|---|
| `PythonService__BaseUrl` | Backend | `http://localhost:8000` | Where the backend reaches the AI service |
| `VITE_API_BASE` | Frontend | `http://localhost:5000` (dev) / same-origin (Docker) | Where the frontend reaches the backend |
| `EMBED_MODEL` | AI service | `all-MiniLM-L6-v2` | Sentence-transformers model to load |

---

## Deployment

See [`DEPLOYMENT.md`](./DEPLOYMENT.md) for a full guide to running this on a
local/office server with Docker Compose — including firewall notes, updating
the app, and a troubleshooting table.

---

## Known Limitations (by design, for MVP scope)

- **Location and title extraction are heuristic** (regex/keyword-based) —
  treat them as a helpful signal for recruiter review, not ground truth for
  edge cases.
- **No database** — results aren't persisted between searches. Add SQLite
  (or similar) if you need search history or caching across sessions.
- **Skill taxonomy is a starter set** — extend `skills.json` (kept in sync
  between `ai_service/` and `backend/RecruitRank.Api/`) as you encounter
  resumes/JDs using terms it doesn't yet recognize.

---

## License

_Add your license here._
