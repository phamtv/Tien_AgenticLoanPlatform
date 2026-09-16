#!/usr/bin/env bash
set -euo pipefail

# Pushes the loan-platform repo to a new GitHub repository.
#
# Prerequisites (do this part manually first):
#   1. Create a NEW, EMPTY repo on GitHub — do NOT initialize it with a
#      README, .gitignore, or license. This repo already has all of that;
#      an empty remote avoids a merge conflict on first push.
#   2. Copy that repo's URL (e.g. https://github.com/YOUR-USERNAME/loan-platform.git)
#
# Usage:
#   ./push-to-github.sh https://github.com/YOUR-USERNAME/loan-platform.git

if [ -z "${1:-}" ]; then
  echo "Usage: $0 <github-repo-url>"
  echo "Example: $0 https://github.com/phamtv/loan-platform.git"
  exit 1
fi

REPO_URL="$1"

echo "Repo state before pushing:"
git log --oneline
echo ""
git status --short || true
echo ""

# Add the remote — or update it if one's already configured, e.g. from a
# previous run of this script.
if git remote get-url origin >/dev/null 2>&1; then
  echo "Remote 'origin' already exists, updating its URL..."
  git remote set-url origin "$REPO_URL"
else
  git remote add origin "$REPO_URL"
fi

git branch -M main
git push -u origin main

echo ""
echo "Pushed. Verify at: $REPO_URL"
