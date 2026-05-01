import process from "node:process";
import type { Readable, Writable } from "node:stream";
import type { Transport, TransportSendOptions } from "@modelcontextprotocol/sdk/shared/transport.js";
import { ErrorCode, JSONRPCMessageSchema, type JSONRPCMessage } from "@modelcontextprotocol/sdk/types.js";

type JsonRpcId = string | number;

export class StrictStdioServerTransport implements Transport {
  onclose?: () => void;
  onerror?: (error: Error) => void;
  onmessage?: <T extends JSONRPCMessage>(message: T) => void;
  sessionId?: string;

  private buffer = "";
  private started = false;

  constructor(
    private readonly stdin: Readable = process.stdin,
    private readonly stdout: Writable = process.stdout,
  ) {}

  async start(): Promise<void> {
    if (this.started) {
      throw new Error("StrictStdioServerTransport already started.");
    }

    this.started = true;
    this.stdin.on("data", this.handleData);
    this.stdin.on("error", this.handleError);
    this.stdin.on("close", this.handleClose);
  }

  async send(message: JSONRPCMessage, _options?: TransportSendOptions): Promise<void> {
    await this.writeMessage(message);
  }

  async close(): Promise<void> {
    this.stdin.off("data", this.handleData);
    this.stdin.off("error", this.handleError);
    this.stdin.off("close", this.handleClose);
    this.started = false;
    this.buffer = "";
    this.onclose?.();
  }

  private readonly handleData = (chunk: Buffer | string): void => {
    this.buffer += chunk.toString();

    while (true) {
      const newlineIndex = this.buffer.indexOf("\n");
      if (newlineIndex < 0) {
        return;
      }

      const line = this.buffer.slice(0, newlineIndex).replace(/\r$/, "");
      this.buffer = this.buffer.slice(newlineIndex + 1);
      this.processLine(line);
    }
  };

  private readonly handleError = (error: Error): void => {
    this.onerror?.(error);
  };

  private readonly handleClose = (): void => {
    this.onclose?.();
  };

  private processLine(line: string): void {
    let parsed: unknown;

    try {
      parsed = JSON.parse(line);
    } catch {
      void this.writeError(ErrorCode.ParseError, "Parse error", undefined);
      return;
    }

    const message = JSONRPCMessageSchema.safeParse(parsed);
    if (!message.success) {
      void this.writeError(ErrorCode.InvalidRequest, "Invalid Request", getRequestId(parsed));
      return;
    }

    this.onmessage?.(message.data);
  }

  private async writeError(code: ErrorCode, message: string, id: JsonRpcId | undefined): Promise<void> {
    await this.writeMessage({
      jsonrpc: "2.0",
      ...(id !== undefined ? { id } : {}),
      error: { code, message },
    });
  }

  private async writeMessage(message: unknown): Promise<void> {
    const line = `${JSON.stringify(message)}\n`;
    await new Promise<void>((resolve) => {
      if (this.stdout.write(line)) {
        resolve();
        return;
      }

      this.stdout.once("drain", resolve);
    });
  }
}

function getRequestId(value: unknown): JsonRpcId | undefined {
  if (!value || typeof value !== "object") {
    return undefined;
  }

  const id = (value as { id?: unknown }).id;
  return typeof id === "string" || typeof id === "number" ? id : undefined;
}