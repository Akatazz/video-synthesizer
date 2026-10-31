# Migration Plan for Video Synthesizer → ChatGPT Codex

---

## 1. Overview
This document outlines the steps required to transfer the **Video Synthesizer** project into a format that **ChatGPT Codex** can consume and work on directly within a GitHub repository. It preserves the full codebase, relevant documentation, and a clear structure for future development.

---

## 2. Project Inventory
A full inventory of the local project files has been captured and archived:

- **Archive:** `VideoSynthesizer.zip` (located in the same artifact directory).
- **File List (excerpt):**
  ```
  .gitignore
  index.html
  package.json
  package-lock.json
  README.md
  tsconfig.json
  vite.config.ts
  video_synthesis_system_spec.md
  src/main.ts
  src/ui.ts
  src/audio.ts
  src/ndi-engine.ts
  dist/ (build output)
  node_modules/ (dependencies)
  ```
  The complete JSON listing of every file (path & size) can be found in `project_file_list.json` (generated automatically).

---

## 3. Repository Preparation
1. **Create a new GitHub repository** (e.g., `github.com/your-org/video-synthesizer`).
2. **Initialize the repository** with a default branch `main`.
3. **Add a `.github/workflows/ci.yml`** (see the *CI Workflow* section below) to enable CI on push.
4. **Push the archive**:
   ```bash
   # Clone the new repo locally
   git clone https://github.com/your-org/video-synthesizer.git
   cd video-synthesizer

   # Extract the archive (the zip file is available in the artifact directory)
   unzip "C:\Users\akazz\.gemini\antigravity\brain\eef684a4-e47a-4e91-a2a1-0d972d517e6b\VideoSynthesizer.zip" -d .

   # Commit everything
   git add .
   git commit -m "Initial import of Video Synthesizer project (generated for Codex)"
   git push origin main
   ```

---

## 4. Documentation Enhancements
### 4.1 README.md
- Add a **Project Overview**, **Installation**, **Usage**, and **Contribution** sections.
- Include a badge linking to the GitHub repository.
- Provide a quick‑start script (`npm ci && npm run dev`).

### 4.2 Code Specs
- The existing `video_synthesis_system_spec.md` will be renamed to `SYSTEM_SPEC.md` and placed at the repo root for easy reference.
- A new `CODING_GUIDELINES.md` will describe the coding conventions (TypeScript, Vite, formatting) used in the project.

---

## 5. Codex Import Process
ChatGPT Codex expects a **GitHub repository** with the source code and a clear layout. Follow these steps:
1. **Make the repo public** (or grant Codex read access via a personal access token).
2. **Create a `codex.yaml`** file at the repository root that tells Codex where to find the entry point and any build scripts. Example:
   ```yaml
   project:
     language: typescript
     build: npm run build
     entry: src/main.ts
   ```
3. **Run Codex** by pointing it to the repository URL. Codex will automatically clone, analyze the `codex.yaml`, and start providing AI‑assisted suggestions.

---

## 6. CI / Linting / Formatting
Add the following files to the repo to keep the codebase healthy:
- **`.eslintrc.cjs`** – basic ESLint configuration for TypeScript.
- **`prettier.config.cjs`** – Prettier formatting rules.
- **GitHub Action (`.github/workflows/ci.yml`):**
  ```yaml
  name: CI
  on: [push, pull_request]
  jobs:
    build:
      runs-on: ubuntu-latest
      steps:
        - uses: actions/checkout@v4
        - uses: actions/setup-node@v4
          with:
            node-version: '20'
        - run: npm ci
        - run: npm run lint
        - run: npm run build
  ```

---

## 7. Next Steps for the User
1. **Create the GitHub repository** (use the link provided in step 3).
2. **Run the extraction & push commands** shown above.
3. **Add the documentation files** (`CODING_GUIDELINES.md`, `codex.yaml`, CI workflow) – the full templates are included in the artifact `documentation_templates.zip`.
4. **Open Codex** with the repo URL and begin iterating.
5. **Optional:** If you want Codex to auto‑generate issues for missing features, enable the *Codex Issue Bot* via the repository settings.

---

## 8. Artifacts Included
| Artifact | Description |
|----------|-------------|
| `VideoSynthesizer.zip` | Complete project archive (code + assets). |
| `project_file_list.json` | JSON list of every file with size, useful for audit. |
| `documentation_templates.zip` | Boilerplate README, CI workflow, lint configs. |
| `migration_plan.md` (this file) | Step‑by‑step migration guide. |

---

**Feel free to ask for any of the individual template files** (e.g., `README_template.md`) or for a more detailed walkthrough of a specific step.

---

*Prepared by Antigravity – your agentic coding assistant.*