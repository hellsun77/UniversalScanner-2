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
