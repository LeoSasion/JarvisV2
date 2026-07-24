# Security policy

## Reporting

Please use GitHub's private security advisory flow for vulnerabilities that
could crash Explorer, bypass the kill switch or one-shot permit, corrupt
resource ownership, expose local data, or execute unreviewed code.

Do not include credentials, personal data, memory dumps or proprietary Windows
binaries in a public issue.

## Safety-sensitive findings

Treat the following as security issues:

- initialization continuing while `disabled.flag` is present or unknown;
- accepting a stale, malformed or wrong-module permit;
- targeting a process other than the verified desktop Shell;
- bypassing exact OS, file, mapped-image or source/build identities;
- unloading code while a callback, hook, COM object or thread may still enter;
- restarting Explorer automatically or in an unattended loop;
- CI or build scripts downloading or executing Windhawk installers.

## Supported state

Only the latest `main` source and its exact committed receipts are maintained.
Offline tests and builds do not constitute live Explorer validation.

Until a module completes a separately authorized live-validation task:

- `releaseReady=false`;
- `activationPermitted=false`;
- `liveExplorer=not-run`.

The emergency flag is a load interlock and runtime quiesce request, not a
process termination or physical unload mechanism.
