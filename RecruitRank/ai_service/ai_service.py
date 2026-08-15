"""
RecruitRank AI Service
Handles: text extraction (PDF/DOCX + OCR fallback), structured parsing,
synonym-aware skill extraction, and batch embedding generation.

Run: uvicorn ai_service:app --port 8000
"""

import json
import os
import re
from datetime import datetime
from typing import List, Optional

import docx2txt
import pdfplumber
import pytesseract
from dateutil.relativedelta import relativedelta
from fastapi import FastAPI
from pdf2image import convert_from_path
from pydantic import BaseModel
from sentence_transformers import SentenceTransformer

app = FastAPI(title="RecruitRank AI Service")

# ---------------------------------------------------------------------------
# Model + skill taxonomy loaded ONCE at startup
# ---------------------------------------------------------------------------
MODEL_NAME = os.getenv("EMBED_MODEL", "all-MiniLM-L6-v2")
embed_model: Optional[SentenceTransformer] = None

SKILLS_PATH = os.path.join(os.path.dirname(__file__), "skills.json")
with open(SKILLS_PATH, encoding="utf-8") as f:
    SKILL_TAXONOMY = json.load(f)  # canonical -> [aliases]

# Build a flat alias -> canonical lookup, longest alias first so
# "asp.net core" matches before the shorter "asp.net" / ".net"
ALIAS_TO_CANONICAL = []
for canonical, aliases in SKILL_TAXONOMY.items():
    for alias in aliases:
        ALIAS_TO_CANONICAL.append((alias.lower(), canonical))
ALIAS_TO_CANONICAL.sort(key=lambda x: len(x[0]), reverse=True)


@app.on_event("startup")
def load_model():
    global embed_model
    embed_model = SentenceTransformer(MODEL_NAME)


# ---------------------------------------------------------------------------
# Request / response models
# ---------------------------------------------------------------------------
class ProcessRequest(BaseModel):
    file_paths: List[str]


class JdEmbedRequest(BaseModel):
    summary: str


# ---------------------------------------------------------------------------
# Text extraction
# ---------------------------------------------------------------------------
def extract_text(path: str) -> str:
    text = ""
    if path.lower().endswith(".pdf"):
        with pdfplumber.open(path) as pdf:
            text = "\n".join(p.extract_text() or "" for p in pdf.pages)
        if len(text.strip()) < 100:  # likely a scanned PDF -> OCR fallback
            images = convert_from_path(path)
            text = "\n".join(pytesseract.image_to_string(img) for img in images)
    elif path.lower().endswith(".docx"):
        text = docx2txt.process(path)
    else:
        raise ValueError(f"Unsupported file type: {path}")
    return text


# ---------------------------------------------------------------------------
# Field extraction (regex-based, no spaCy)
# ---------------------------------------------------------------------------
EMAIL_RE = re.compile(r"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}")
PHONE_RE = re.compile(r"(\+\d{1,3}[-.\s]?)?(\(?\d{2,5}\)?[-.\s]?){2,4}\d{2,4}")
DATE_RANGE_RE = re.compile(
    r"(?P<start>[A-Za-z]{3,9}\.?\s+\d{4}|\d{1,2}/\d{4}|\d{4})"
    r"\s*(?:-|to|–|—)\s*"
    r"(?P<end>[A-Za-z]{3,9}\.?\s+\d{4}|\d{1,2}/\d{4}|\d{4}|[Pp]resent|[Cc]urrent)",
)


def extract_email(text: str) -> Optional[str]:
    m = EMAIL_RE.search(text)
    return m.group(0) if m else None


def extract_phone(text: str) -> Optional[str]:
    m = PHONE_RE.search(text)
    return m.group(0).strip() if m else None


def extract_name(text: str) -> Optional[str]:
    # Heuristic: first non-empty line that isn't an email/phone and looks
    # like "First Last" (2-4 title-cased words, no digits).
    for line in text.splitlines()[:6]:
        line = line.strip()
        if not line or EMAIL_RE.search(line) or PHONE_RE.search(line):
            continue
        words = line.split()
        if 1 < len(words) <= 4 and all(w.replace(".", "").isalpha() for w in words):
            return line
    return None


def extract_skills(text: str) -> List[str]:
    """Synonym-aware skill matching: any alias found -> canonical skill returned once."""
    lower_text = text.lower()
    found = set()
    for alias, canonical in ALIAS_TO_CANONICAL:
        # word-boundary match so "js" doesn't match inside "objects"
        pattern = r"(?<![a-zA-Z0-9])" + re.escape(alias) + r"(?![a-zA-Z0-9])"
        if re.search(pattern, lower_text):
            found.add(canonical)
    return sorted(found)


def parse_date(raw: str) -> Optional[datetime]:
    raw = raw.strip()
    if raw.lower() in ("present", "current"):
        return datetime.now()
    for fmt in ("%b %Y", "%B %Y", "%b. %Y", "%m/%Y", "%Y"):
        try:
            return datetime.strptime(raw, fmt)
        except ValueError:
            continue
    return None


def extract_work_experience(text: str) -> List[dict]:
    jobs = []
    for m in DATE_RANGE_RE.finditer(text):
        start = parse_date(m.group("start"))
        end = parse_date(m.group("end"))
        if start:
            jobs.append({"start": m.group("start"), "end": m.group("end"), "start_dt": start, "end_dt": end})
    return jobs


def compute_total_exp(jobs: List[dict]) -> float:
    total_months = 0
    for j in jobs:
        start = j.get("start_dt")
        end = j.get("end_dt") or datetime.now()
        if not start:
            continue
        diff = relativedelta(end, start)
        total_months += max(diff.years * 12 + diff.months, 0)
    return round(total_months / 12, 1)


LOCATION_HINTS = [
    "bengaluru", "bangalore", "mumbai", "delhi", "hyderabad", "pune", "chennai",
    "kolkata", "gurgaon", "gurugram", "noida", "ahmedabad", "remote",
]


def extract_location(text: str) -> Optional[str]:
    lower_text = text.lower()
    for city in LOCATION_HINTS:
        if re.search(r"(?<![a-zA-Z])" + city + r"(?![a-zA-Z])", lower_text):
            return city.title()
    return None


def extract_current_title(jobs: List[dict], text: str) -> Optional[str]:
    # naive: look at the line just before the first (most recent) date range
    lines = text.splitlines()
    for i, line in enumerate(lines):
        if DATE_RANGE_RE.search(line) and i > 0:
            candidate = lines[i - 1].strip()
            if candidate and len(candidate.split()) <= 6:
                return candidate
    return None


def build_summary(title: Optional[str], skills: List[str], total_exp: float) -> str:
    skill_str = ", ".join(skills) if skills else "not specified"
    return (
        f"Role: {title or 'not specified'}. Skills: {skill_str}. "
        f"Experience: {total_exp} years."
    )


def parse_resume(text: str) -> dict:
    skills = extract_skills(text)
    jobs = extract_work_experience(text)
    total_exp = compute_total_exp(jobs)
    title = extract_current_title(jobs, text)
    return {
        "name": extract_name(text),
        "email": extract_email(text),
        "phone": extract_phone(text),
        "skills": skills,
        "total_experience": total_exp,
        "location": extract_location(text),
        "current_title": title,
        "summary": build_summary(title, skills, total_exp),
        "work_history": [{"start": j["start"], "end": j["end"]} for j in jobs],
    }


# ---------------------------------------------------------------------------
# Endpoints
# ---------------------------------------------------------------------------
@app.post("/process")
def process(request: ProcessRequest):
    candidates = []
    failed = []
    summaries = []
    parsed_profiles = []

    # Pass 1: extract + parse each file, collecting failures without
    # aborting the whole batch.
    for path in request.file_paths:
        try:
            text = extract_text(path)
            profile = parse_resume(text)
            profile["source_file"] = path
            parsed_profiles.append(profile)
            summaries.append(profile["summary"])
        except Exception as e:  # noqa: BLE001 - intentionally broad, per-file isolation
            failed.append({"file": path, "reason": str(e)})

    # Pass 2: batch-embed all summaries together (much faster than
    # calling model.encode() once per candidate).
    if summaries:
        embeddings = embed_model.encode(summaries, batch_size=32).tolist()
        for profile, embedding in zip(parsed_profiles, embeddings):
            profile["embedding"] = embedding
            candidates.append(profile)

    return {"candidates": candidates, "failed": failed}


@app.post("/embed_jd")
def embed_jd(request: JdEmbedRequest):
    embedding = embed_model.encode([request.summary])[0].tolist()
    return {"embedding": embedding}


@app.get("/health")
def health():
    return {"status": "ok", "model": MODEL_NAME}
