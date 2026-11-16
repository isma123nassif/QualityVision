# Facts: how the project was built with AI agents (written by the orchestrator, source of truth for the docs)

## Setup
- **Owner:** the human developer, who decides scope, approves each milestone (hard STOP after each one) and gives system permissions (sudo was never run by AI: the human installed cuDNN himself when the permission classifier blocked it).
- **Orchestrator:** Claude Code main session (model Claude Opus). It plans, decomposes, writes the contracts, launches agents, integrates, runs ALL builds/tests/format (agents cannot run `dotnet`), does live verification (curl, Playwright/Chromium screenshots, reading the annotated MJPEG frames), runs git, and keeps memory.
- **Global rule (CLAUDE.md of the user):** the orchestrator does not write product code itself; it delegates. The original plan was OpenCode with DeepSeek v4 Pro; its quota was exhausted, so agents are Claude CLI sessions.
- **Visible agents:** each agent runs in its own Konsole window (script `lanzar-claude.sh <name> <model> <dir>` + `run-claude.sh`), placed by a KWin script on the "Desarrollo" desktop beside the terminal, so the human SEES every agent work. The exit code is written to `<name>.exit`; the orchestrator waits on it, then closes the window.
- **Roles** (`.claude/agents/*.md`, each prompt = role definition + task): architect, backend-dev, vision-engineer, frontend-dev, qa-engineer, code-reviewer (read-only, may only write `docs/reviews/MX-review.md`), tech-writer.
- **Model per task:** Sonnet for most implementation and tests (fast and cheap); Opus for the code reviews and the most delicate UI work; Haiku for a trivial fix. 67 agent prompts in total.

## Method per milestone
1. The orchestrator writes `docs/contracts/MX-contract.md`, with exact signatures, ownership per role and the files each role may touch.
2. Independent roles run IN PARALLEL against the contract (e.g. backend ║ frontend ║ vision ║ architect).
3. Integration: the orchestrator runs `dotnet build -warnaserror`, `dotnet test`, `dotnet format --verify-no-changes`.
4. QA writes tests after the implementation. The code-reviewer (Opus) writes a review with BLOCKER/MAJOR/MINOR findings. A fix agent takes the MAJORs plus the cheap MINORs.
5. Live E2E verification by the orchestrator.
6. tech-writer: a Spanish learning note `docs/aprendizaje/MX.md`, with 3 interview questions.
7. One Conventional Commit per milestone. STOP and wait for the human's OK.

## Quality gates (Definition of Done)
- Zero warnings (TreatWarningsAsErrors, analyzers, IDE0005).
- Tests green.
- Format clean.
- Review without BLOCKER.
- Real functional check.
- Learning note.
- Commit.

## Numbers
- 8 commits (M1 to M6 + M4b + detection) between 15-nov 15:50 and 16-nov 11:42.
- ~5,600 lines of product code (C#/Razor) and ~4,500 lines of tests.
- **417 tests.**
- 5 ADRs, 6 contracts, 7 reviews, 8 learning notes.

## Real incidents and lessons (good interview material: honest, shows control)
- **Reviews caught real bugs before the demo.**
  - M5: the rules editor would have wiped every rule when `rules.json` contained a comment.
  - M6: CSV formula injection (OWASP), and the Plan-B video playing at 1.5x speed.
  - M4b: the CPU native `libonnxruntime.so` (34.8 MB) silently overwrote the GPU build (38.1 MB), because `ExcludeAssets` does not flow through `ProjectReference`. This is now guarded by a test.
- **The orchestrator verified rather than trusted.**
  - The M3 reviewer claimed YoloDotNet was GPL; the nuspec says MIT, so the claim was corrected.
  - An agent reported the wrong test count; it was corrected from the real run.
- **Tooling accidents and fixes.**
  - `pkill -f <project name>` killed an agent whose prompt contained that text. The rule now is to kill by PID only.
  - An unquoted heredoc made the shell execute backticks from a prompt (no damage). Prompts are now always written with quoted heredocs.
- **Limits:** a usage limit interrupted an agent mid-milestone. It was relaunched with a note about which files already existed. A half-finished agent was resumed with "check git diff, keep what is correct, complete the rest".
- **Product decisions taken with data:**
  - GPU: measured yolo11m at 198 ms on CPU vs 6.5 ms on CUDA.
  - The marker: COCO has no such class. Lowering the threshold is useless; YOLO-World with text classes and distractors got 0.86–0.97 live. The professional path is fine-tuning on labelled images of real parts.
  - No separate SignalR hub: Blazor Server's circuit already is SignalR.
- **Human in the loop:** the human set the scope, rejected Tailscale ("nothing to do with this project"), chose the iPhone page over DroidCam because of latency, asked for full GPU power, and asked for the marker detection.

## Timeline (approximate)
- 14-nov: M0 (environment, .NET 10 SDK, uv/Python only for the model export).
- 15-nov: M1 (scaffold, agents, ADR-001), M2 (capture, MJPEG, iPhone page), M3 (YOLO11 + boxes), M4 (rules engine, zone, debounce, hot reload).
- 16-nov morning: M4b (GPU), M5 (dashboard, alarm, rules editor), YOLO-World + `run-demo.sh`, M6 (SQLite, CSV, recording).
