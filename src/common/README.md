# Common Windows source

This directory contains projects intended to be shared by Windows 10 and
Windows 11 after platform-specific admission.

Do not move code here merely because it compiles on both systems. A component
belongs here only when it has no private Win11 symbol, Win11-only selector or
Win11-only DWM assumption. Existing assembly names and namespaces remain
stable.
