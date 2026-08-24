# Piko Context Bridge

This VS Code extension sends privacy-safe summaries to the local Piko Runtime over a current-user-only Windows named pipe.

It sends only:

- build start/result and duration;
- test task success/failure and duration;
- total diagnostic error/warning counts;
- Git branch plus staged/unstaged change counts.

It never sends source code, diagnostic messages, terminal output, task labels, repository paths, filenames, or workspace names. Piko Desktop's per-capability privacy switches are authoritative; development and Git summaries are denied by default until the user enables them.

## Development

```powershell
npm install
npm run check
npm run compile
```
