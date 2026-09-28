# UniversalScanner-2 Development Instructions

These instructions apply to all future development tasks in this repository.

## Before Every Development Task

1. Check the current Git branch and working tree.
2. Fetch the latest changes from `origin`.
3. Check whether the local branch is behind `origin/main`.
4. If the working tree is clean, pull the latest changes using fast-forward only.
5. If uncommitted local changes exist, preserve them and report the situation before attempting synchronization.
6. Never overwrite, discard, or reset existing work.
7. If the repository is already synchronized, proceed with the development task.

## During Development

1. Preserve existing application functionality.
2. Work on the latest synchronized source code.
3. Do not modify unrelated files.
4. Do not commit credentials, temporary files, or build artifacts.

## Deep Code Review Workflow

1. Before making code changes, prepare the intended implementation plan and submit it to Deep Code for review from the repository root using `deepcode -x -p "<plan review prompt>"`.
2. The Deep Code plan review prompt must clearly say not to modify files and must ask Deep Code to identify risks, missing checks, and safer implementation steps.
3. Consider Deep Code feedback before editing, but preserve these `AGENTS.md` instructions as the controlling workflow if Deep Code suggests conflicting process steps.
4. After implementing and before committing, submit the completed work or current diff to Deep Code from the repository root using a non-mutating prompt. Use `git diff HEAD -- <task files> | deepcode -x -p "Review this diff for correctness and risks. Do not modify files."` so staged and unstaged task changes are both included. If the task adds new files, stage only those task files before this review or include their contents explicitly so Deep Code can see them.
5. Address valid Deep Code findings before committing. If Deep Code is unavailable, record that in the verification notes and continue with the normal manual review/build workflow.

## After Every Completed Development Task

1. Verify that the project builds successfully.
2. Add a new entry to `FIXES_AND_UPDATES.txt` describing the fix or update, changed files, verification, and commit hash placeholder.
3. Review the modified files.
4. Stage only files related to the completed task, including `FIXES_AND_UPDATES.txt`.
5. Create a descriptive Git commit.
6. Replace the commit hash placeholder in `FIXES_AND_UPDATES.txt` with the actual commit hash, then amend the commit.
7. Fetch the latest remote changes.
8. Push the commit to GitHub if the remote branch has not diverged.
9. If the remote branch has changed, integrate the changes safely before pushing.
10. Report the commit ID and push status.

## Safety Rules

- Never force-push.
- Never use `git reset --hard`.
- Never discard local changes.
- Never overwrite remote changes.
- Stop and ask the user if a merge conflict occurs.
- Do not modify the original project folder outside this Git repository.
