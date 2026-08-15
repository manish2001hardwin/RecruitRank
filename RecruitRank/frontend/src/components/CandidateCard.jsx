import React, { useState } from 'react';

export default function CandidateCard({ rank, candidate, evidence }) {
  const [showContact, setShowContact] = useState(false);

  return (
    <div className="candidate-card">
      <div className="candidate-header">
        <div>
          <strong>#{rank} {candidate.name || 'Unknown Candidate'}</strong>
          <div className="meta">{candidate.current_title || 'Title not detected'}</div>
        </div>
        <div className="score">{evidence.overallScore ?? evidence.OverallScore ?? 0}%</div>
      </div>

      <div className="skills-row">
        {(evidence.matchedSkills || evidence.MatchedSkills || []).map((s) => (
          <span key={s} className="skill-match">✓ {s} &nbsp;</span>
        ))}
        {(evidence.missingSkills || evidence.MissingSkills || []).map((s) => (
          <span key={s} className="skill-missing">✗ {s} &nbsp;</span>
        ))}
      </div>

      <div className="meta">
        {evidence.experienceVerdict || evidence.ExperienceVerdict} · {evidence.locationStatus || evidence.LocationStatus}
      </div>

      <div style={{ marginTop: 8 }}>
        <button onClick={() => setShowContact(!showContact)}>
          {showContact ? 'Hide Contact' : 'View Contact'}
        </button>
      </div>
      {showContact && (
        <div className="meta">
          {candidate.email || 'No email found'} · {candidate.phone || 'No phone found'}
        </div>
      )}
    </div>
  );
}
