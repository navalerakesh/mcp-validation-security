import assert from "node:assert/strict";
import { PassThrough } from "node:stream";
import test from "node:test";
import { StrictStdioServerTransport } from "../src/strict-stdio-server-transport.js";

test("strict stdio transport decodes UTF-8 split across chunks", async () => {
  const input = new PassThrough();
  const output = new PassThrough();
  const transport = new StrictStdioServerTransport(input, output);
  const received = new Promise<unknown>((resolve) => { transport.onmessage = resolve; });
  await transport.start();
  const frame = Buffer.from('{"jsonrpc":"2.0","method":"ping-€"}\n', "utf8");
  const euroIndex = frame.indexOf(Buffer.from("€", "utf8"));

  input.write(frame.subarray(0, euroIndex + 1));
  input.write(frame.subarray(euroIndex + 1));

  assert.deepEqual(await received, { jsonrpc: "2.0", method: "ping-€" });
  await transport.close();
});

test("strict stdio transport rejects and closes oversized unterminated frame", async () => {
  const input = new PassThrough();
  const output = new PassThrough();
  const transport = new StrictStdioServerTransport(input, output);
  const error = new Promise<Error>((resolve) => { transport.onerror = resolve; });
  const closed = new Promise<void>((resolve) => { transport.onclose = resolve; });
  await transport.start();

  input.write(Buffer.alloc((1024 * 1024) + 1, 0x61));

  assert.match((await error).message, /exceeds/);
  await closed;
});