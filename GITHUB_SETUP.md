# GitHub Setup Guide
**Sorcery+ Remake - Push to GitHub**

---

## Quick Setup (First Time)

### 1. Create GitHub Repository

Go to GitHub and create a new repository:
- **Name:** `sorcery-plus-remake` (or your preferred name)
- **Description:** "Faithful remake of Amstrad CPC Sorcery+ using C# MonoGame"
- **Visibility:** Public or Private (your choice)
- **DO NOT** initialize with README, .gitignore, or license (we already have these)

### 2. Link Local Repository to GitHub

After creating the GitHub repo, copy the repository URL and run:

```bash
# Replace YOUR_USERNAME with your GitHub username
git remote add origin https://github.com/YOUR_USERNAME/sorcery-plus-remake.git

# Verify remote was added
git remote -v
```

**Example:**
```bash
git remote add origin https://github.com/johndoe/sorcery-plus-remake.git
```

### 3. Push Your Code

```bash
# Push to GitHub (main branch)
git push -u origin main
```

**Note:** The `-u` flag sets `origin main` as the default upstream, so future pushes can just be `git push`.

---

## Verify Upload

After pushing, go to your GitHub repository URL in your browser:
- You should see all files uploaded
- Check that `README.md` displays correctly
- Verify the commit message appears

---

## Future Commits (After First Push)

### Daily Workflow

```bash
# 1. Check what changed
git status

# 2. Stage changes (option A: stage all)
git add .

# OR (option B: stage specific files)
git add Core/PlayerController.cs Graphics/SpriteConfig.cs

# 3. Commit with message
git commit -m "Fix: Updated player animation timing"

# 4. Push to GitHub
git push
```

### Commit Message Guidelines

Use this format for clear history:

```bash
# Feature additions
git commit -m "feat: Add enemy AI system"

# Bug fixes
git commit -m "fix: Correct sprite animation frame positions"

# Documentation
git commit -m "docs: Update Phase 2 development log"

# Refactoring
git commit -m "refactor: Improve physics integration loop"

# Performance
git commit -m "perf: Optimize sprite batching"
```

---

## GitHub Authentication

### Option 1: Personal Access Token (Recommended)

1. Go to GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic)
2. Click "Generate new token (classic)"
3. Select scopes: `repo` (full control)
4. Copy the token (save it securely!)
5. When git asks for password during `git push`, paste the token

### Option 2: SSH Key

```bash
# 1. Generate SSH key (if you don't have one)
ssh-keygen -t ed25519 -C "your_email@example.com"

# 2. Copy public key
cat ~/.ssh/id_ed25519.pub

# 3. Add to GitHub: Settings → SSH and GPG keys → New SSH key

# 4. Change remote to SSH URL
git remote set-url origin git@github.com:YOUR_USERNAME/sorcery-plus-remake.git
```

---

## Useful Git Commands

### Check Status
```bash
# See what files changed
git status

# See detailed changes
git diff

# See commit history
git log --oneline
```

### Undo Changes
```bash
# Undo changes to a file (before staging)
git checkout -- Core/Entity.cs

# Unstage a file
git reset HEAD Core/Entity.cs

# Amend last commit message
git commit --amend -m "New message"
```

### Branching (For Features)
```bash
# Create and switch to feature branch
git checkout -b feature/collision-system

# Work on feature, commit changes
git add .
git commit -m "feat: Add pixel-perfect collision"

# Switch back to main
git checkout main

# Merge feature into main
git merge feature/collision-system

# Delete feature branch
git branch -d feature/collision-system
```

---

## .gitignore Overview

The `.gitignore` file prevents these from being committed:
- ✅ `bin/` and `obj/` (build artifacts)
- ✅ `Content/bin/` and `Content/obj/` (MonoGame intermediates)
- ✅ `.vs/` (Visual Studio cache)
- ✅ `*.user` files (personal settings)

**What IS committed:**
- ✅ Source code (`.cs` files)
- ✅ Project files (`.csproj`, `.sln`)
- ✅ Assets (`assets/images/`)
- ✅ Documentation (`docs/`)
- ✅ Build scripts (`build.bat`, `build.sh`)

---

## GitHub Features to Explore

### Issues
Track bugs and feature requests:
- Go to your repo → Issues → New issue
- Label them: `bug`, `enhancement`, `documentation`

### Releases
Create version tags:
```bash
# Tag Phase 1
git tag -a v0.1.0 -m "Phase 1: Core engine complete"
git push origin v0.1.0
```

### GitHub Actions (CI/CD)
Automatically build/test on every push (future enhancement).

---

## Troubleshooting

### "Permission denied (publickey)"
→ Use Personal Access Token or set up SSH key

### "Updates were rejected"
→ Pull first: `git pull origin main`, then push

### "LF will be replaced by CRLF"
→ Normal on Windows, Git auto-converts line endings

### Large files won't upload
→ Files over 100 MB need Git LFS:
```bash
git lfs install
git lfs track "*.psd"  # Example for large asset files
```

---

## Current Repository Status

✅ **First commit created:** `92e278d` - "Phase 1: Complete - Core Game Engine & Animation System"

✅ **Files committed:** 38 files, 6088 lines of code

✅ **Ready to push** to GitHub!

---

## Next Steps

1. **Create GitHub repo** at https://github.com/new
2. **Add remote:** `git remote add origin <YOUR_REPO_URL>`
3. **Push:** `git push -u origin main`
4. **Verify** upload on GitHub website
5. **Continue development** with regular commits

---

**Happy coding!** 🚀
