# Windows 10 backend

The first Win10 vertical slice is implemented in
`Jarvis.Win10.NativeStyleProbe`. It reads exact host and DWM state, verifies
the reviewed dark-caption attribute on an owned HWND, and provides an
interactive own-process visual surface.

See `WINDOWS10-HANDOFF.md` for the migration boundary and
`docs/WINDOWS10-NATIVE-STYLE-PROBE.md` for commands and receipts. The exact
profile is `win10-22h2-19045.6466-x64`.

The probe does not discover or modify Explorer and does not use Windhawk.
Future projects continue to use `Jarvis.Win10.<Feature>` and must not reuse
Win11 private symbols, selectors, DWM backdrops or module IDs.
