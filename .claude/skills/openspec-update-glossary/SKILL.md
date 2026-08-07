---
name: openspec-update-glossary
description: Update the Ubiquitous Language Glossary (openspec/GLOSSARY.md) with domain terms introduced or changed by an OpenSpec change. Use when the user wants to keep the glossary in sync after writing a proposal/design/specs, or after finishing a change.
allowed-tools: Bash(openspec:*)
license: MIT
compatibility: Requires openspec CLI.
---

Update `openspec/GLOSSARY.md` — the Ubiquitous Language Glossary — with the domain
terms an OpenSpec change's planning artifacts introduce or redefine. This is
documentation maintenance only: it never edits code or other change artifacts.

**Store selection:** If the user names a store (a standalone OpenSpec repo
registered on this machine) or the work lives in one, run `openspec store list
--json` to discover registered store ids and pass `--store <id>` on `status`
and `list`. Without a store, commands act on the nearest local `openspec/`
root, and the glossary lives at `<planningHome.root>/openspec/GLOSSARY.md`.

**Input**: Optionally a change name. If omitted, infer it from conversation
context. If ambiguous, run `openspec list --json` and ask the user to choose —
show both active and recently archived changes, since glossary-worthy terms
often only settle once a change is finished. Always announce: "Using change:
<name>" and how to override (`/openspec-update-glossary <other>`).

**Steps**

1. **Resolve the change**

   ```bash
   openspec status --change "<name>" --json
   ```

   Take `planningHome.root` and `artifactPaths` from the response. The
   glossary file is not part of the artifact schema (it's a project-maintained
   file, not something `openspec status` tracks) — it always lives at
   `<planningHome.root>/openspec/GLOSSARY.md`. If it doesn't exist yet, tell
   the user and ask before creating one from scratch (a title, a one-paragraph
   purpose statement, and the first `##` section) rather than assuming this
   skill should originate the file.

2. **Read the source material**

   Read every existing file under `artifactPaths.proposal.existingOutputPaths`,
   `artifactPaths.design.existingOutputPaths`, and
   `artifactPaths.specs.existingOutputPaths` for the selected change — these
   describe intent and behavior and are the richest source of domain
   vocabulary. Read `tasks.md` only if the above leave a term's meaning
   unclear.

3. **Read the current glossary in full**

   Never guess whether a term already exists under a different heading or as
   a synonym mentioned in another entry's body — read the whole file first.

4. **Identify candidate terms**

   - New domain concepts this change introduces that aren't already an entry
     (by name or by a synonym already covered elsewhere).
   - Existing entries whose *meaning or behavior* changed because of this
     change — not just wording.
   - Exclude implementation detail (class/method/endpoint names, file names,
     variables) and plain words with no special meaning in this domain.
   - When it's a close call, leave it out. The glossary is small and
     load-bearing by design (~25 focused entries across a handful of areas);
     it stays useful only if it doesn't accumulate noise.

5. **Draft entries in the existing format**

   - `### Term Name` header, one focused paragraph — see entries like
     **Sidecar** or **Review Status** for the target length and level of
     detail: what it is, how it relates to neighboring terms, one
     distinguishing nuance.
   - The first mention of another glossary term inside a new/edited entry is
     **bold**.
   - Place each new term under the most fitting existing `##` area heading.
     Only introduce a new `##` area if none fit, and say why in the summary.
   - Preserve the rest of the file untouched — append within a section rather
     than reordering or rewriting neighboring entries.

6. **Show the proposed diff and confirm before writing**

   Present new terms and modified terms as two short lists (term name +
   proposed body). Apply only after the user confirms. If they reject one
   entry, drop just that one and keep the rest.

7. **Write** the confirmed entries into `openspec/GLOSSARY.md`.

**Output**

```markdown
## Glossary Updated: <change-name>

**Added:**
- <Term> — <one-line reason it's ubiquitous language>

**Modified:**
- <Term> — <what changed and why>

**Considered and left out:** <term — reason>, if any borderline calls were made.

File: openspec/GLOSSARY.md
```

**Guardrails**

- Read the full glossary before proposing anything — never duplicate a term
  under a slightly different name.
- Ground every definition in what the artifacts actually say; don't invent it.
- Confirm with the user before writing.
- Touch only terms related to the selected change.
- Keep new/edited entries proportional to their neighbors — a short paragraph,
  not a spec excerpt.
- Never edit code or other OpenSpec artifacts from this skill.
