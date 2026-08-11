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
- targeting a process other than the exact verified desktop Shell instance;
- bypassing OS, file, mapped-image, source or artifact identities;
- unloading code while a callback, hook, COM object or thread may still enter;
- restarting Explorer before quiescing the module and re-arming the kill switch;
- using a global injector or creating an unattended Explorer restart loop;
- CI or build scripts downloading or executing unreviewed installers.

## Supported state

Only the latest `main` source and its exact committed receipts are maintained.
Offline tests and builds do not constitute live Explorer validation.

Standing authorization permits bounded live validation on the dedicated
Windows 10 VM only after the automated preflight in `AGENTS.md` succeeds. Each
session must bind the current source and artifact hash to one exact Explorer
PID, nonzero Shell TID and owned window; begin with the kill switch armed and no
stale permit; select one module; and keep a recovery helper active.

Every session must end with the module quiesced, the kill switch armed and the
one-shot permit absent. The emergency flag is a load interlock and runtime
quiesce request, not a process termination or physical unload mechanism.
