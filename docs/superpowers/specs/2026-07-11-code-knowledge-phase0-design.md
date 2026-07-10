# Code Knowledge Phase 0 Design

## 1. Purpose

Phase 0 validates the technical assumptions that must hold before the Code Knowledge MVP is designed and implemented. It is part of the `ck` repository, but remains an isolated, rerunnable verification harness rather than production code.

The phase validates:

- the bundled SQLite version and availability of FTS5 with the trigram tokenizer;
- Japanese three-character FTS search and one-to-two-character LIKE fallback;
- WAL and busy-timeout behavior under multi-process reads and writes;
- MCP stdio Tool invocation from Cursor, GitHub Copilot in VS Code, and Claude Code;
- framework-dependent single-file publication on the target Windows 11 machine, including native SQLite dependencies.

Phase 0 does not define the production domain model, database schema, application architecture, or production MCP Tools. Successful findings and test cases inform Phase 1; Phase 0 source code is not promoted directly into production projects.

## 2. Chosen Approach

Use one probe executable with multiple execution modes, plus a separate automated test project. Keeping all probes in one executable ensures that SQLite, MCP, concurrency, and publication checks exercise the same dependency graph and publish configuration.

Rejected alternatives:

- Separate executables per technical concern make failures easy to isolate but can hide integration and packaging differences.
- Building the production `Core`, `Application`, `Infrastructure`, and `Mcp` projects first would prematurely fix Phase 1 architecture before its assumptions are validated.

## 3. Repository Layout

```text
ck/
├─ docs/
│  └─ superpowers/specs/
├─ spikes/
│  └─ phase0/
│     ├─ CodeKnowledge.Phase0/
│     │  ├─ Program.cs
│     │  ├─ SqliteProbe.cs
│     │  ├─ ConcurrencyProbe.cs
│     │  ├─ McpProbe.cs
│     │  └─ CodeKnowledge.Phase0.csproj
│     ├─ CodeKnowledge.Phase0.Tests/
│     │  ├─ SqliteProbeTests.cs
│     │  ├─ SearchProbeTests.cs
│     │  ├─ ConcurrencyProbeTests.cs
│     │  ├─ McpProbeTests.cs
│     │  └─ PublishSmokeTests.cs
│     └─ README.md
└─ CodeKnowledge.Phase0.slnx
```

`README.md` contains reproducible commands, client registration instructions, the manual verification checklist, and the final verification record.

## 4. Probe Executable

`CodeKnowledge.Phase0` is a .NET 10 console executable. It supports three modes.

### 4.1 `self-check`

`self-check` creates an isolated temporary SQLite database and performs the following checks in order:

1. Read the bundled SQLite version and require version 3.34.0 or later.
2. Create an FTS5 virtual table with `tokenize = "trigram"`.
3. Insert fixed Japanese knowledge records, including `注文完了メール仕様`.
4. Query `メール` through parameterized FTS `MATCH` and verify the expected record.
5. Query `仕様` and `確認` through parameterized, escaped LIKE patterns and verify expected records.
6. Merge FTS and LIKE result sets and verify the expected knowledge is retained.
7. Enable and verify `journal_mode = WAL`, `busy_timeout = 5000`, and `foreign_keys = ON`.

It prints one machine-readable JSON result to stdout. The result contains the executable version, SQLite version, individual check results, and an overall status.

### 4.2 `concurrency-worker`

This mode is intended to be launched by the automated test suite. Each worker opens the same test database and repeatedly performs parameterized reads and transactional writes.

The parent test launches multiple worker processes, waits for all of them to finish, and then verifies:

- no worker failed with a lock or timeout error;
- the final row count matches the expected successful writes;
- unique operation identifiers reveal neither missing nor duplicated writes;
- reads return well-formed records;
- WAL mode remains active.

The exact worker and iteration counts are test parameters. Defaults must create overlapping activity while remaining stable on a developer workstation. Tests must not depend on timing alone to prove overlap; workers use an explicit start barrier controlled by the parent test.

### 4.3 `mcp`

This mode runs a minimal MCP server over stdio and exposes one Tool named `phase0_probe`. The Tool returns:

- `status` with value `ok`;
- executable version;
- process ID;
- SQLite version;
- server timestamp in UTC.

It contains no production business Tools. With no command-line mode specified, the executable starts in `mcp` mode so the published EXE can be registered directly in each client.

In `mcp` mode, stdout is reserved exclusively for MCP protocol traffic. Diagnostics and logs are written to stderr.

## 5. Data and Resource Isolation

Every automated test receives a unique temporary directory and database path. Tests do not use `%LOCALAPPDATA%\CodeKnowledge\knowledge.db` and cannot affect future production data.

SQLite connections explicitly enable the required pragmas instead of relying on process-global state. Test data is deterministic and contains only synthetic records. Temporary databases, WAL files, shared-memory files, and publish directories are removed during test cleanup. Cleanup failures are reported without masking the original test result.

## 6. Search Validation

The search probe verifies the constraints established by the requirements document without implementing the full Phase 1 search service.

Required cases are:

- three-or-more Unicode code points use FTS5 trigram MATCH;
- one-to-two Unicode code points use LIKE;
- FTS terms are quoted so a term such as `sui-memory` is not interpreted as an operator expression;
- LIKE terms escape `\`, `%`, and `_` and use `ESCAPE '\'`;
- SQL values are passed as parameters;
- FTS and LIKE results can be merged without losing a record that matches either route.

The probe uses only the minimum columns and SQL necessary to validate these assumptions. It does not predefine the production schema or ranking algorithm.

## 7. Automated Verification

The automated suite must cover:

- bundled SQLite version;
- FTS5 and trigram virtual-table creation;
- Japanese three-character FTS search;
- Japanese two-character LIKE search;
- safe handling of hyphens and LIKE metacharacters;
- FTS and LIKE result merging;
- WAL, busy timeout, and foreign-key settings;
- concurrent multi-process reads and writes;
- MCP initialization and `phase0_probe` invocation using a protocol test client;
- framework-dependent single-file publication followed by execution of the published EXE in `self-check` mode;
- absence of ordinary logs on stdout while running in `mcp` mode.

Tests exercise the public probe operations. Concurrency and publish tests execute child processes rather than replacing operating-system behavior with mocks.

## 8. Manual MCP Client Verification

The published EXE is registered separately with:

1. Cursor;
2. GitHub Copilot in VS Code;
3. Claude Code.

For each client, a human invokes `phase0_probe` and records in `spikes/phase0/README.md`:

- client name and version;
- verification date;
- command or configuration used;
- returned executable and SQLite versions;
- pass or fail;
- observations or client-specific deviations.

All three successful invocations are required. A protocol-level automated MCP test does not replace these client checks.

## 9. Publication Verification

The probe is published for Windows as a framework-dependent, single-file .NET 10 executable. The publish smoke test runs the published EXE in `self-check` mode rather than the build output.

The verification record lists every produced file. If native SQLite components are emitted beside the EXE, they are treated as required distribution artifacts and documented explicitly. The phase succeeds when the documented artifact set runs on the target machine without administrator rights or an installer; an EXE-only output is not required if the SQLite runtime legitimately requires adjacent native files.

## 10. Error Handling and Output Contracts

Non-MCP modes return deterministic process exit codes:

| Exit code | Meaning |
|---:|---|
| 0 | Every requested check passed |
| 1 | One or more technical checks failed |
| 2 | Command-line arguments are invalid |
| 3 | An unexpected execution error occurred |

`self-check` and `concurrency-worker` emit a single JSON result to stdout and diagnostics to stderr. A failed check includes a stable check identifier and a concise failure reason. Secret values, source code, connection strings, and credentials are never logged.

The MCP server reports Tool failures through MCP-compatible error responses and preserves stdout for protocol traffic. An initialization failure is written to stderr and terminates the process with a nonzero exit code.

## 11. Phase Completion Gate

Phase 0 is complete only when all of the following are true:

1. All automated tests pass.
2. The published executable passes `self-check`.
3. Cursor invokes `phase0_probe` successfully.
4. GitHub Copilot in VS Code invokes `phase0_probe` successfully.
5. Claude Code invokes `phase0_probe` successfully.
6. The complete distribution artifact set is recorded.
7. No observed result contradicts a technical assumption in the requirements document.

If any assumption fails, Phase 1 is blocked. The implementation note records the failed assumption, evidence, and viable alternatives. The requirements document is revised and approved before Phase 1 planning continues. The spike must not hide a failed assumption with an unapproved fallback.

## 12. Non-Goals

Phase 0 does not include:

- the production project resolver;
- the production database schema or migrations;
- Knowledge, KnowledgeVersion, Evidence, or KnowledgeDiff models;
- production search ranking;
- freshness validation or Git integration;
- production MCP Tools;
- CLI product functionality;
- automatic client-specific installation or configuration;
- promotion of spike source files into Phase 1.
