# GitHub Repository Governance & Branch Protection Settings

To ensure repository stability, high code quality, and a clean linear commit history, configure the following GitHub repository settings at:
`https://github.com/rtkelly13/Parquet.SourceGenerator/settings`

> **Not yet applied.** `main` currently has no protection rules. This file records the intended
> configuration; nothing enforces it until someone applies it by hand.

---

## 🔒 1. Branch Protection Rules (`main`)

Navigate to **Settings → Branches → Add branch protection rule** for pattern `main`:

- [x] **Require a pull request before merging**
  - Require approvals: `1`
  - Dismiss stale pull request approvals when new commits are pushed
  - Require review from Code Owners
- [x] **Require status checks to pass before merging**
  - Status checks required — the name must match the **job** name exactly, or the check never
    reports and every pull request is blocked indefinitely:
    - `Comprehensive E2E PR Verification (.NET 8 & 9 + Native AOT)` (`ci.yml`)
    - `Validate PR Conventional Commit Title` (`pr-title.yml`)
  - Do **not** require the benchmark workflow. It is `workflow_dispatch` only, so it would never
    report a status.
  - "Require branches to be up to date before merging" is deliberately omitted while there is a
    queue of open pull requests — it forces a rebase of every open branch after each merge, which
    serializes work that could otherwise land in parallel. Turn it on once the queue is drained.
- [x] **Require linear history**
  - Prevents non-linear merge commits and forces either rebase or squash merging.
- [x] **Include administrators**
  - Enforce branch protection rules on repository admins.

---

## 🔀 2. Pull Request & Merge Strategy Settings

Navigate to **Settings → General → Pull Requests**:

- [x] **Allow squash merging**: **ENABLED**
  - Default commit message: *Pull request title and description*
- [ ] **Allow merge commits**: **DISABLED** (enforces clean linear history)
- [ ] **Allow rebase merging**: **DISABLED** (or enabled if rebase is preferred)
- [x] **Automatically delete head branches**: **ENABLED**
  - Automatically deletes feature branches after PR merge to keep the repository clean.

---

## 🛡️ 3. Security & Dependency Analysis

Navigate to **Settings → Code security and analysis**:

- [x] **Dependency graph**: Enabled
- [x] **Dependabot alerts**: Enabled
- [x] **Dependabot security updates**: Enabled
- [x] **Secret scanning**: Enabled
