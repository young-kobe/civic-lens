# AI text rules survey (origin/main cfe9259a, 2026-11-13)

Stages: A1 action from CRS summary, A2 action from bill text, A3 key terms, V vote, S statement, G statement gate, R relationship, B brief, BC brief check, D document change, E executive summary.

## Key differences
1. Grade checks exist only on A2 and E. Code also approves A1, V and S (own office) with no person and no grade check.
2. ExecutiveSummaryPrompt.cs:11 claims the bill text figure rules; ExecutiveSummaryResolver has no figure check.
3. Quote rules differ: minimum 4 words (A2, A3, E) or none (S, G, R, D); maximum 300 / 2,000 / 4,096 / 8,192 characters; first match (A2, E) or unique match (S, G, R, D).
4. E borrows the ActionDraftRevision constants (sentence count, sentence length, quote length and words).
5. Copies: grade repair message (ActionDraftResolver:163, ExecutiveSummaryResolver:72); grade approval message (ActionTextApproval:27, ExecutiveSummaryApproval:17); MaximumValidationPasses = 2 in nine task classes; DocumentChangeDraftPrompt:84-87 has its own rejection text; about eight wordings of the untrusted source, neutral, no judgment and confidence prompt sentences.
6. Banned phrase list in code only for E. A2 has a similar prompt rule with no check. The internal words ban (tracked, entry, candidate) is only in the A1 and V prompts. The bill status ban has no code check.
7. Labels: owners in PublicWeb (PublishedRecordLabels) and Publication.Contracts (BillTextWords, ExecutiveSummaryWords, PublishedWording), about 20 SQL copies, seven texts. The relationship reason is public model text with no label and no text origin. The static Host brief page shows no label.
8. Document change headline: prompt 120 characters, code 256.
9. PageTextCeiling (test) and ReadingGrade.MaximumGrade will both be 14, but they are different rules.
10. ReadingGrade lives in Application/Actions but serves executive actions and page tests.

## Options
1. One owner per layer (recommended, narrow): Application/Analysis/AiTextRules (target 12, maximum 14, passage grade check with repair and approval messages, validation pass count, shared prompt sentences; ReadingGrade moves to Analysis as the measure). Core: one quoted sentence limits type used by ActionDraftRevision, ExecutiveActionSummary and ActionKeyTerm. Publication.Contracts: one AI label owner (move ModelWording, CodeWording, StatementModelWording, StatementCheckedByPersonWording beside BillTextWords). Keep quote finders, names rule, BillFigureSet and ExecutiveSummaryPhrases with their stages. Precedents: AnalysisAnswerRules, ReadingGrade/SentenceEnds, PublishedWording, BillStatusMeaning. Against: shared prompt sentences bump prompt versions.
2. A shared QuotedSummaryCheck for A2 and E: only two callers, different quote finders; not now.
3. Docs standard plus parity tests only: copies stay; use only for the SQL label copies.

## Decisions for the owner
1. Grade check on A1, V, S (code approved).
2. Grade check on person-approved B, D, R, or advice only.
3. Figure check on E, or drop the claim.
4. One phrase ban list for all public model text.
5. One quote rule set, or a stated reason per stage.
6. Label wording family (owner decided 2026-11-12: keep texts, unify rendering).
7. Label and text origin for the relationship reason.
8. Document change headline limit (120 or 256).
9. Internal words ban in all public prompts.
10. Fixed text ceiling and AI maximum: one constant or two.
