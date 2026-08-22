---
name: writing-skills
description: Use when creating new skills, editing existing skills, or verifying skills work before deployment
---

# Writing Skills

## Overview

Writing skills is test-driven development applied to process documentation.

**Core principle:** If you did not observe the agent fail without the skill, you do not know whether the skill teaches the right thing.

## What a Skill Is

A skill is reusable guidance for techniques, patterns, workflows, or references. Project-specific conventions belong in `AGENTS.md`, not in a reusable skill.

## Skill Structure

```text
skills/
  skill-name/
    SKILL.md
    supporting-file.*
```

`SKILL.md` requires YAML frontmatter with `name` and `description`.

- Use lowercase/clear hyphenated names.
- Start descriptions with `Use when...` and describe triggering conditions.
- Do not summarize the full workflow in the description; agents may use that summary instead of reading the skill.
- Keep the main file concise. Move heavy references or deterministic tools into supporting files.

## RED-GREEN-REFACTOR for Skills

### RED: Establish Baseline

Before adding or changing guidance, run representative scenarios without the guidance. Record the actual failure, confusion, rationalization, or wrong output shape.

### GREEN: Write Minimal Guidance

Write only enough instruction to correct the observed failure. Re-run the same scenarios and verify the agent now behaves correctly.

### REFACTOR: Close Loopholes

If the agent finds a new rationalization or ambiguity, tighten the guidance and re-test. Keep useful behavior intact while removing unnecessary wording.

## Match Guidance to the Failure

| Failure | Preferred guidance |
|---|---|
| Agent knowingly skips a rule | Firm rule + rationalization table + red flags |
| Output has wrong shape | Positive output contract/template |
| Required element is omitted | Put it structurally in the template |
| Behavior depends on a condition | Explicit conditional tied to an observable predicate |

Avoid vague exception clauses such as “unless appropriate”; they reopen interpretation without defining a testable condition.

## Discovery

Optimize for how agents find skills:

1. Encounter a problem.
2. Match it against skill descriptions.
3. Load the relevant `SKILL.md`.
4. Read supporting files only when needed.

Use concrete trigger terms, error names, symptoms, tools, and domain vocabulary near the top of the skill.

## Examples and References

- Prefer one strong, runnable example over many variants.
- Use flowcharts only for non-obvious decisions or loops; use lists/tables for linear/reference material.
- Reference other skills by name rather than duplicating their full content.
- Keep file references shallow so supporting material is easy to discover.

## Verification Checklist

- [ ] Baseline failure was observed before writing the guidance.
- [ ] `name` and `description` frontmatter are valid and specific.
- [ ] Description focuses on when the skill applies.
- [ ] Guidance directly addresses the observed failure.
- [ ] The skill is concise and avoids project-specific rules.
- [ ] Supporting files exist only when they add real value.
- [ ] Representative scenarios pass with the skill enabled.
- [ ] New loopholes found during testing were closed and re-tested.

This is a project-normalized copy based on Superpowers v6.3.0, kept self-contained to reduce project skill footprint.
