# Security Policy

JitRealm compiles and executes C# source files at runtime.

If you expose this over the network, treat world code as **untrusted** and implement sandboxing
(e.g., separate process + restricted API surface).
