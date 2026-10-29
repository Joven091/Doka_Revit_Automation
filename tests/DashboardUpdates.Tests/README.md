# Independent Dashboard update acceptance

This suite is owned by the independent QA agent. It links the production protocol
and package code, while its JSON, archive, SHA-256 and filesystem expectations are
constructed separately. It does not use a production package writer to derive
expected manifests or archive contents.

Approved acceptance scope:

- Desktop feed identity/version/protocol, separate from the existing Revit EXE feed.
- Exact archive and file sizes/hashes; complete manifest/file agreement.
- Archive path traversal, absolute paths, duplicate/colliding paths, unsupported
  entries, missing required payload and bounded extraction failures.
- Immutable version directories, staging cleanup, cancellation, verified reuse.
- Atomic active pointer and generation-guarded activation/rollback, including
  stale concurrency and preservation of the previous usable version.
- Bounded IPC, authentication/session/protocol/request correlation and malformed
  frame rejection after the parent finalizes the transport API.
- No Revit PID wait/kill in a Dashboard-only update; busy/unsafe working state
  refuses an update without changing the active release.

Windows production-EXE checks will be a separate `DashboardDesktop.Smoke` project
after the implementation contract is finalized. They must cover actual entry,
calculation tools, state checkpoint/restoration, authenticated bridge stand-ins,
and Dashboard-only restart while a simulated Revit process remains alive.

An actual Revit 2024 session, model interaction and deployment to the company
share remain separate manual acceptance. No pure test or API stand-in establishes
that acceptance. No installer/publication or source mutation is authorized to QA.

The current independent pure suite passes **351 checks on .NET10/Linux** against
the implementation APIs, with inert archive payloads and a simulated process
environment. It includes real filesystem staging/activation/rollback, independent
archive/metadata corruption, frame fragmentation, rejected concatenated JSON,
generation races, recovery-checkpoint validation and startup/receipt failures.
It also races a single reader against first publication of a 2 MiB JSON document
and checks create-new preservation plus failure cleanup. The reader never retries
sharing violations or partial content. It uses `Read | Delete` file sharing,
matching the protocol reader and permitting Windows' atomic rename handle to
finish. An isolated build of the original direct
writer fails this regression with a sharing violation; the atomic writer passes.
This does not establish actual process startup, Windows execution or Revit API
behavior. Native Windows and external production-EXE smoke evidence remain later
gates. The native smoke project is now implemented and cross-builds cleanly;
it has not been executed on Windows yet. Build/run logs are under
`/workspace/validation/dashboard-live-update/qa/`.
