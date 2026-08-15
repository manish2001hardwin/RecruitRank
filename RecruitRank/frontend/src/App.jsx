import React, { useState } from 'react';
import CandidateCard from './components/CandidateCard.jsx';

// Empty string (production/Docker default) means "same origin" — nginx
// proxies /api/* to the backend container. Falls back to localhost:5000
// only when the env var is genuinely undefined (local `npm run dev`).
const API_BASE = import.meta.env.VITE_API_BASE !== undefined
  ? import.meta.env.VITE_API_BASE
  : 'http://localhost:5000';

export default function App() {
  const [jdText, setJdText] = useState('');
  const [files, setFiles] = useState([]);
  const [loading, setLoading] = useState(false);
  const [results, setResults] = useState(null);
  const [failed, setFailed] = useState([]);
  const [error, setError] = useState('');

  async function handleSearch() {
    setError('');
    if (!jdText.trim()) {
      setError('Please paste a job description first.');
      return;
    }
    if (files.length === 0) {
      setError('Please upload at least one resume (PDF, DOCX, or a ZIP of them).');
      return;
    }

    const formData = new FormData();
    formData.append('jdText', jdText);
    for (const f of files) formData.append('files', f);

    setLoading(true);
    setResults(null);
    try {
      const res = await fetch(`${API_BASE}/api/search`, { method: 'POST', body: formData });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || `Request failed (${res.status})`);
      }
      const data = await res.json();
      setResults(data.ranked || []);
      setFailed(data.failed || []);
    } catch (e) {
      setError(e.message || 'Something went wrong while searching.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="container">
      <h1>RecruitRank</h1>
      <p className="subtitle">Paste a job description, upload resumes, get a ranked shortlist.</p>

      <div className="card">
        <label><strong>Job Description</strong></label>
        <textarea
          value={jdText}
          onChange={(e) => setJdText(e.target.value)}
          placeholder="Paste the full job description here..."
        />
      </div>

      <div className="card">
        <label><strong>Resumes</strong> (PDF, DOCX, or a ZIP containing multiple)</label>
        <br />
        <input
          type="file"
          multiple
          accept=".pdf,.docx,.zip"
          onChange={(e) => setFiles(Array.from(e.target.files))}
        />
        {files.length > 0 && <div className="meta">{files.length} file(s) selected</div>}
        <br />
        <button onClick={handleSearch} disabled={loading}>
          {loading ? 'Searching…' : 'Find Candidates'}
        </button>
      </div>

      {error && <div className="error-box">{error}</div>}

      {failed.length > 0 && (
        <div className="error-box">
          {failed.length} file(s) could not be processed:
          <ul>
            {failed.map((f, i) => (
              <li key={i}>{f.file.split(/[/\\]/).pop()} — {f.reason}</li>
            ))}
          </ul>
        </div>
      )}

      {results && results.length === 0 && !error && (
        <div className="card">No candidates matched the mandatory requirements.</div>
      )}

      {results && results.length > 0 && (
        <div>
          <h3>{results.length} candidate(s) ranked</h3>
          {results.map((r, i) => (
            <CandidateCard key={i} rank={i + 1} candidate={r.candidate} evidence={r.evidence} apiBase={API_BASE} />
          ))}
        </div>
      )}
    </div>
  );
}
