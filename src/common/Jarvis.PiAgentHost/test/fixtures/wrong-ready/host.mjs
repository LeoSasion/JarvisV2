process.stdout.write(
  `${JSON.stringify({
    type: "ready",
    protocol: "untrusted-protocol",
    package: "@earendil-works/pi-coding-agent",
    version: "0.82.1",
    credentialEnvironmentClean: true,
    sessionCreationEnabled: false,
  })}\n`,
);
setInterval(() => {}, 1_000);
