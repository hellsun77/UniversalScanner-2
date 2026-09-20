# Git Workflow — Universal Scanner

Standard Git workflow for using Claude (Architect/Reviewer) and Codex (Implementer) together on this project.

---

## Branch Strategy

### Feature Branches

All work happens on feature branches off `main` (or `master`). Never commit directly to the default branch.

```bash
# Start a new feature
git checkout main
git pull origin main
git checkout -b feature/<short-descriptive-name>

# Examples:
git checkout -b feature/add-axis-activator
git checkout -b fix/ssdp-duplicate-uuid
git checkout -b docs/update-vendor-matrix
```

**Branch naming convention:**
- `feature/<name>` — new capability, vendor support, protocol scanner
- `fix/<name>` — bug fixes
- `docs/<name>` — documentation-only changes
- `refactor/<name>` — restructuring without behavior change

### Keep Branches Short-Lived

Aim to merge within a few days. Long-lived branches accumulate conflicts and diverge from the patterns on `main`.

---

## Before Every AI/Codex Session

### 1. Save Current State

Commit or stash any in-progress manual work before letting Claude/Codex modify files.

```bash
# Check what's dirty
git status

# Option A: Commit work in progress
git add -A
git commit -m "wip: save manual changes before AI session"

# Option B: Stash if not ready to commit
git stash push -m "wip: manual changes before AI session"
```

### 2. Start Clean

Ensure you're on the right branch with a clean starting point.

```bash
git status              # Clean? On the right branch?
git log --oneline -5    # Confirm you're at the right commit
```

### 3. Create a Backup Tag (for major changes)

For large features, tag the starting point so you can always diff or reset back.

```bash
git tag backup/before-<feature-name>
```

---

## During AI/Codex Work

### Task Files

Every investigation, plan, or implementation must have a corresponding task file in `tasks/`. Claude creates these automatically. Active tasks live in `tasks/`, completed tasks are archived to `tasks/archive/`.

```bash
# Task files are created automatically by Claude
ls tasks/              # Active tasks
ls tasks/archive/      # Completed tasks
```

### Evidence Files

The `evidence/` folder stores screenshots, logs, PCAPs, configs, and exports used for bug investigation. All evidence files are **read-only** — never modified or deleted by Claude/Codex.

```bash
# Evidence is placed here manually by the user
ls evidence/screenshots/   # UI errors, dialogs, exceptions
ls evidence/logs/          # Runtime/application errors
ls evidence/pcaps/         # Network captures
ls evidence/configs/       # Configuration exports
ls evidence/exports/       # Project data exports
```

Evidence files are referenced by name and path in BUG task files. When investigating an issue, Claude auto-checks this folder before asking for more information.

### Let Claude/Codex Modify Files

Claude and Codex will read, edit, and create files. They won't run git commands unless explicitly asked. You control when changes are committed.

### Check Progress Periodically

```bash
# See what's changed so far
git diff --stat

# See detailed changes
git diff

# See new untracked files
git ls-files --others --exclude-standard
```

---

## After AI/Codex Work — Review Before Commit

### 1. Review All Changes

```bash
# Summary of all changes
git diff --stat

# Full diff of modified tracked files
git diff

# Show staged + unstaged changes together
git diff HEAD

# List new untracked files
git ls-files --others --exclude-standard
```

### 2. Diff Specific Files

```bash
git diff -- path/to/file.cs
git diff -- docs/
```

### 3. Check for Unwanted Changes

Before committing, verify:

- [ ] No unrelated files were modified
- [ ] No debug/console.log statements left in
- [ ] No commented-out code without explanation
- [ ] Documentation was updated alongside code changes
- [ ] `FEATURE_HISTORY.md` has an entry (for major features)
- [ ] `CHANGELOG_AI.md` was updated

```bash
# Quick scan for common mistakes
git diff | grep -E "^\+.*(console\.|Debug\.Write|TODO|FIXME|HACK)"
```

---

## Committing

### Commit Message Format

```
<type>(<scope>): <short description>

<optional body — what and why>

<optional footer — verification, related issues>
```

**Types:** `feat`, `fix`, `docs`, `refactor`, `chore`, `test`

**Examples:**

```
feat(wds-discovery): add support for Axis WS-Discovery namespaces

Axis cameras use a non-standard namespace prefix in Probe responses.
Added namespace normalization in Wsdiscovery.cs receiver.

Verified: Tested with Axis M2025-LE and P3225-LV. Existing ONVIF
discovery unaffected.
```

```
fix(ssdp): prevent duplicate UUID when device changes IP

The dedup logic in deviceFound() was matching on IP only, ignoring
UUID. Added UUID-first matching with IP as fallback.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```

```
docs(git-workflow): add standard Git workflow documentation

Covers branching, pre-session saves, review, commit format,
and recovery commands.
```

### Commit After Successful Testing

Only commit after you've verified the changes work:

1. Review the diff
2. Build the project
3. Test the changed functionality
4. Then commit

```bash
git add <specific-files>         # Prefer explicit files over git add -A
git commit -m "<message>"
```

Avoid `git add -A` for post-AI commits — it can sweep in unintended changes. Add files explicitly or use `git add -p` for interactive staging.

---

## Documentation Synchronization

### Rule: Docs and Code Commit Together

When a feature changes behavior, the corresponding doc updates must be in the **same commit** (or an adjacent commit on the same branch). Never merge code changes without their documentation.

### Which Docs to Update

| Change Type | Docs to Update |
|-------------|---------------|
| New vendor support | VENDOR_MATRIX.md, PROJECT_INDEX.md, PROJECT_SUMMARY.md |
| New protocol scanner | DISCOVERY_ENGINE.md, PROJECT_INDEX.md |
| New form or utility class | PROJECT_INDEX.md, ARCHITECTURE.md |
| Changed activation/IP flow | NETWORK_ARCHITECTURE.md, VENDOR_MATRIX.md |
| Bug fix | KNOWN_ISSUES.md (remove if listed), CHANGELOG_AI.md |
| Any major feature | FEATURE_HISTORY.md, CHANGELOG_AI.md |
| Pattern/convention change | IMPLEMENTATION_GUIDELINES.md |
| Documentation only | PROJECT_INDEX.md (if new file added) |

### Pre-Commit Doc Check

```bash
# Did code change without matching doc changes?
git diff --name-only | grep -v docs/
git diff --name-only | grep docs/

# If the first list is non-empty and the second is empty,
# ask: should docs have been updated?
```

---

## Recovery Commands

### Undo Uncommitted Changes to a File

```bash
# Discard changes to one file (restore from last commit)
git restore path/to/file.cs

# Discard changes to all tracked files
git restore .

# Restore a file from a specific commit
git restore --source=<commit-hash> path/to/file.cs
```

### Undo Staged Changes

```bash
# Unstage a file (keep changes in working tree)
git restore --staged path/to/file.cs

# Unstage everything
git restore --staged .
```

### Undo a Commit (Keep Changes)

```bash
# Undo last commit, keep changes staged
git reset --soft HEAD~1

# Undo last commit, keep changes unstaged
git reset HEAD~1
```

### Undo a Commit (Discard Changes)

```bash
# Undo last commit and DISCARD all changes — DANGER ZONE
git reset --hard HEAD~1
```

### Recover a Deleted File

```bash
# Restore a file deleted in a recent commit
git restore --source=HEAD~1 path/to/deleted-file.cs
```

### Revert a Merged Commit

```bash
# Create a new commit that undoes a previous commit (safe for shared branches)
git revert <commit-hash>
```

### Switch Branches (Discarding Changes)

```bash
# Force-switch to another branch, discarding uncommitted changes
git checkout -f main
```

### Recover from a Bad Merge

```bash
# Abort a merge in progress
git merge --abort

# Abort a rebase in progress
git rebase --abort
```

### Find When Something Broke

```bash
# See commits affecting a file
git log --oneline -- path/to/file.cs

# See what a specific commit changed
git show <commit-hash>

# See who last changed each line
git blame path/to/file.cs
```

### Recovery with Backup Tags

```bash
# See all backup tags
git tag -l "backup/*"

# Diff current state against a backup tag
git diff backup/before-<feature> --stat

# Reset entirely back to a backup tag (careful!)
git reset --hard backup/before-<feature>
```

---

## Workflow Summary

```
┌─────────────────────────────────────────────────────────┐
│  START: git checkout -b feature/<name>                   │
│         git tag backup/before-<name>                     │
│                                                          │
│  AI:   Claude reads & plans                              │
│        Claude writes Codex prompt                        │
│                                                          │
│  CODE: Codex implements                                  │
│        Claude reviews                                    │
│                                                          │
│  CHECK: git diff --stat                                  │
│         git diff                                         │
│         Verify docs updated                              │
│                                                          │
│  TEST: Build. Run. Verify.                               │
│                                                          │
│  COMMIT: git add <files>                                 │
│          git commit -m "feat(scope): description"        │
│                                                          │
│  MERGE: git checkout main                                │
│         git merge feature/<name>                         │
│         git push origin main                             │
│                                                          │
│  RECOVER (if needed):                                    │
│         git restore <file>          # undo file changes   │
│         git reset --soft HEAD~1    # undo commit         │
│         git revert <hash>          # undo merged commit  │
│         git reset --hard backup/   # back to tag         │
└─────────────────────────────────────────────────────────┘
```

---

## Quick Reference

| Action | Command |
|--------|---------|
| New feature branch | `git checkout -b feature/<name>` |
| Save before AI work | `git add -A && git commit -m "wip: ..."` |
| See what changed | `git diff --stat` |
| Full diff | `git diff` |
| Undo file changes | `git restore <file>` |
| Undo last commit (keep) | `git reset --soft HEAD~1` |
| Undo last commit (nuke) | `git reset --hard HEAD~1` |
| Revert merged commit | `git revert <hash>` |
| Clean untracked files | `git clean -fd` (careful!) |
| Tag a starting point | `git tag backup/before-<name>` |
| See backup tags | `git tag -l "backup/*"` |
