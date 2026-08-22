---
name: using-superpowers
description: Use when starting any conversation - establishes how to find and use project skills before responding or acting
---

# Using Superpowers

## Rule

Invoke relevant or explicitly requested skills before responding or taking action, including clarification, codebase exploration, implementation, debugging, and review.

If a skill turns out not to apply after reading it, continue normally. User/project instructions always take precedence over skills.

## Priority

When several skills apply, process skills set the approach before implementation skills.

- New feature/design work → `brainstorming`
- Bug/test failure → `systematic-debugging`
- Implementation with a written plan → `subagent-driven-development` or `executing-plans`
- Before completion claims → `verification-before-completion`

## Project Skill Location

This repository vendors Superpowers under `.superpowers/skills` and exposes the same directory to supported agents through project-local symlinks.

Do not install a second copy inside the project unless the user explicitly requests a different version.

## Codex

When running under Codex, read `references/codex-tools.md` for the project-specific mapping of subagent and worktree behavior.

Claude Code, OpenCode, and OMP use the project-local skill directory exposed for their harness and otherwise follow the generic skill instructions.

This is a project-normalized copy based on Superpowers v6.3.0.
