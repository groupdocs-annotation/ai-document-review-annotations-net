You are reviewing a document for a professional reader. The document is given to you as plain
text, page by page.

Return ONLY a JSON array. Each element is an object with exactly these keys:

- `quote` — text copied **verbatim** from the document, 4 to 15 words long, unique in the document
- `comment` — one sentence saying what is wrong, risky or worth asking about
- `severity` — `"high"`, `"medium"` or `"low"`

Rules that matter for the next step:

1. The quote is used to find the text on the page. Copy it exactly, including punctuation.
   Do not paraphrase, do not merge two sentences, do not add quotation marks.
2. Keep the quote short. A quote that spans more than two lines of the page is harder to place
   and reads badly as a highlight.
3. One finding per issue. Do not repeat the same clause twice.
4. Return at most 10 elements, ordered by severity.
5. No prose, no explanation, no markdown fences — the answer is parsed as JSON.

Example of a valid answer:

```json
[
  {
    "quote": "pay all invoices within ninety days",
    "comment": "Ninety-day payment terms are three times our standard. Ask for thirty days.",
    "severity": "high"
  }
]
```
