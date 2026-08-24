import * as crypto from "node:crypto";
import * as net from "node:net";
import * as vscode from "vscode";

const pipePath = "\\\\.\\pipe\\PikoDesktopPet.Runtime.v1";
const source = "vscode.extension";
const sessionId = crypto.randomUUID().replaceAll("-", "");
const maximumResponseCharacters = 65_536;

interface RuntimeResponse {
  schemaVersion: number;
  requestId: string;
  success: boolean;
  type: string;
  error?: string;
  payload?: unknown;
}

interface ContextField {
  value: string;
  sensitivity?: "public" | "low" | "medium" | "high" | "restricted";
}

interface GitChange {
  readonly indexStatus?: number;
  readonly workingTreeStatus?: number;
}

interface GitRepository {
  readonly state: {
    readonly HEAD?: { readonly name?: string };
    readonly indexChanges: readonly GitChange[];
    readonly workingTreeChanges: readonly GitChange[];
    readonly mergeChanges: readonly GitChange[];
    readonly onDidChange: vscode.Event<void>;
  };
}

interface GitApi {
  readonly repositories: readonly GitRepository[];
  readonly onDidOpenRepository: vscode.Event<GitRepository>;
}

interface GitExtensionExports {
  getAPI(version: 1): GitApi;
}

let publishQueue = Promise.resolve();
let lastConnectionNoticeAt = 0;

export function activate(context: vscode.ExtensionContext): void {
  const output = vscode.window.createOutputChannel("Piko");
  context.subscriptions.push(output);
  context.subscriptions.push(vscode.commands.registerCommand("piko.showRuntimeStatus", async () => {
    try {
      const response = await sendRequest("health.get");
      const health = response.payload as { health?: string; version?: string; situation?: string } | undefined;
      await vscode.window.showInformationMessage(
        `Piko Runtime: ${health?.health ?? "unknown"}, ${health?.version ?? "unknown"}, ${health?.situation ?? "Unknown"}`);
    } catch (error) {
      await vscode.window.showWarningMessage(`Piko Runtime is unavailable: ${messageOf(error)}`);
    }
  }));

  if (!isEnabled()) {
    return;
  }

  registerTaskEvents(context, output);
  registerDiagnosticEvents(context, output);
  void registerGitEvents(context, output);
}

export function deactivate(): void {
  // All listeners and timers are owned by ExtensionContext subscriptions.
}

function registerTaskEvents(context: vscode.ExtensionContext, output: vscode.OutputChannel): void {
  const starts = new Map<vscode.TaskExecution, number>();
  context.subscriptions.push(vscode.tasks.onDidStartTaskProcess(event => {
    starts.set(event.execution, Date.now());
    if (event.execution.task.group !== vscode.TaskGroup.Test) {
      enqueueContext(output, "development.build.started", "DevelopmentActivity", {});
    }
  }));
  context.subscriptions.push(vscode.tasks.onDidEndTaskProcess(event => {
    const startedAt = starts.get(event.execution) ?? Date.now();
    starts.delete(event.execution);
    const durationMs = Math.max(0, Date.now() - startedAt);
    const success = event.exitCode === 0;
    if (event.execution.task.group === vscode.TaskGroup.Test) {
      enqueueContext(output, "development.tests.completed", "DevelopmentActivity", {
        success: field(success),
        failed: field(success ? 0 : 1),
        durationMs: field(durationMs)
      });
    } else {
      enqueueContext(output, "development.build.completed", "DevelopmentActivity", {
        success: field(success),
        durationMs: field(durationMs)
      });
    }
  }));
}

function registerDiagnosticEvents(context: vscode.ExtensionContext, output: vscode.OutputChannel): void {
  let timer: NodeJS.Timeout | undefined;
  const publish = (): void => {
    if (timer) {
      clearTimeout(timer);
    }
    timer = setTimeout(() => {
      let errors = 0;
      let warnings = 0;
      for (const [, diagnostics] of vscode.languages.getDiagnostics()) {
        for (const diagnostic of diagnostics) {
          if (diagnostic.severity === vscode.DiagnosticSeverity.Error) {
            errors++;
          } else if (diagnostic.severity === vscode.DiagnosticSeverity.Warning) {
            warnings++;
          }
        }
      }
      enqueueContext(output, "development.diagnostics.changed", "DiagnosticsSummary", {
        errors: field(errors),
        warnings: field(warnings)
      });
    }, 750);
  };
  context.subscriptions.push(vscode.languages.onDidChangeDiagnostics(publish));
  context.subscriptions.push({ dispose: () => timer && clearTimeout(timer) });
  publish();
}

async function registerGitEvents(
  context: vscode.ExtensionContext,
  output: vscode.OutputChannel
): Promise<void> {
  const extension = vscode.extensions.getExtension<GitExtensionExports>("vscode.git");
  if (!extension) {
    return;
  }

  const exports = extension.isActive ? extension.exports : await extension.activate();
  const api = exports.getAPI(1);
  const registered = new WeakSet<GitRepository>();
  const timers = new WeakMap<GitRepository, NodeJS.Timeout>();
  const register = (repository: GitRepository): void => {
    if (registered.has(repository)) {
      return;
    }
    registered.add(repository);
    const publish = (): void => {
      const existing = timers.get(repository);
      if (existing) {
        clearTimeout(existing);
      }
      timers.set(repository, setTimeout(() => {
        const branch = repository.state.HEAD?.name;
        const data: Record<string, ContextField> = {
          staged: field(repository.state.indexChanges.length),
          changed: field(repository.state.workingTreeChanges.length),
          conflicts: field(repository.state.mergeChanges.length)
        };
        if (branch) {
          data.branch = field(branch, "medium");
        }
        enqueueContext(output, "development.git.activity", "GitMetadata", data);
      }, 800));
    };
    context.subscriptions.push(repository.state.onDidChange(publish));
    publish();
  };

  api.repositories.forEach(register);
  context.subscriptions.push(api.onDidOpenRepository(register));
}

function enqueueContext(
  output: vscode.OutputChannel,
  type: string,
  capability: string,
  data: Record<string, ContextField>
): void {
  publishQueue = publishQueue
    .then(async () => {
      if (!isEnabled()) {
        return;
      }
      const response = await sendRequest("context.publish", {
        schemaVersion: 1,
        type,
        source,
        timestamp: new Date().toISOString(),
        sessionId,
        capability,
        sensitivity: "low",
        retention: "session",
        confidence: 1,
        data
      });
      if (!response.success) {
        throw new Error(response.error ?? "context_publish_failed");
      }
    })
    .catch(error => {
      const now = Date.now();
      if (now - lastConnectionNoticeAt > 60_000) {
        output.appendLine(`[${new Date().toISOString()}] Runtime unavailable or event denied: ${messageOf(error)}`);
        lastConnectionNoticeAt = now;
      }
    });
}

async function sendRequest(type: string, payload?: unknown): Promise<RuntimeResponse> {
  const requestId = crypto.randomUUID().replaceAll("-", "");
  const request = JSON.stringify({ schemaVersion: 1, requestId, type, payload });
  return await new Promise<RuntimeResponse>((resolve, reject) => {
    const socket = net.createConnection(pipePath);
    let response = "";
    const timeout = setTimeout(() => socket.destroy(new Error("timeout")), 2_000);
    socket.setEncoding("utf8");
    socket.on("connect", () => socket.write(`${request}\n`));
    socket.on("data", chunk => {
      response += chunk;
      if (response.length > maximumResponseCharacters) {
        socket.destroy(new Error("response_too_large"));
        return;
      }
      const newline = response.indexOf("\n");
      if (newline < 0) {
        return;
      }
      clearTimeout(timeout);
      socket.end();
      try {
        const parsed = JSON.parse(response.slice(0, newline)) as RuntimeResponse;
        if (parsed.schemaVersion !== 1 || parsed.requestId !== requestId) {
          reject(new Error("response_mismatch"));
        } else if (!parsed.success) {
          reject(new Error(parsed.error ?? "runtime_error"));
        } else {
          resolve(parsed);
        }
      } catch (error) {
        reject(error);
      }
    });
    socket.on("error", error => {
      clearTimeout(timeout);
      reject(error);
    });
  });
}

function field(
  value: string | number | boolean,
  sensitivity: ContextField["sensitivity"] = "low"
): ContextField {
  return { value: String(value), sensitivity };
}

function isEnabled(): boolean {
  return vscode.workspace.getConfiguration("piko").get<boolean>("contextBridge.enabled", true);
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
