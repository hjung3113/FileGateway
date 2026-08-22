---
name: systematic-debugging
description: Use when encountering any bug, test failure, or unexpected behavior, before proposing fixes
---

# Systematic Debugging

## Overview

**Core principle:** Always find the root cause before attempting fixes. Symptom-only fixes are failure.

## Iron Law

NO FIXES WITHOUT ROOT CAUSE INVESTIGATION FIRST.

## Phase 1: Root Cause Investigation

1. Read errors and warnings completely, including stack traces and locations.
2. Reproduce the problem consistently. If it is not reproducible, gather more evidence instead of guessing.
3. Check recent code, dependency, configuration, and environment changes.
4. For multi-component systems, inspect each component boundary and verify what enters, exits, and propagates across it. Add only the minimum diagnostics necessary to identify the failing layer; do not expose secrets or credentials.
5. Trace invalid state or values backward through callers until the original source is found. See `root-cause-tracing.md`.

## Phase 2: Pattern Analysis

1. Find a similar working example in the same codebase.
2. Read relevant reference implementations completely.
3. List every difference between the working and failing cases.
4. Identify dependencies, configuration, environment, and assumptions.

## Phase 3: Hypothesis and Testing

1. Form one explicit hypothesis: what the root cause is and why.
2. Test it with the smallest possible change and one variable at a time.
3. If the hypothesis fails, return to investigation and form a new one rather than stacking fixes.
4. If something is not understood, say so and investigate further instead of guessing.

## Phase 4: Implementation

1. Create the smallest failing automated test or reproduction before fixing.
2. Implement one fix addressing the identified root cause.
3. Verify the reproduction passes and no relevant tests regress. Use `superpowers:verification-before-completion` before claiming success.
4. If a fix fails, return to Phase 1. After three failed fix attempts, stop and question whether the architecture or underlying assumptions are wrong before attempting another fix.

## Red Flags

Return to Phase 1 if you catch yourself proposing a quick fix before evidence, changing multiple things at once, skipping the failing test, guessing at an unfamiliar pattern, or attempting another fix after repeated failures without reassessing the architecture.

## Supporting Techniques

- `root-cause-tracing.md` — trace failures backward to the original trigger.
- `defense-in-depth.md` — add validation at appropriate layers after the root cause is understood.
- `condition-based-waiting.md` — replace arbitrary sleeps/timeouts with condition-based waiting when timing is involved.

This project-local copy is based on Superpowers v6.3.0. A shell diagnostic example from upstream is intentionally omitted so project guidance does not encourage printing environment secrets during debugging.
