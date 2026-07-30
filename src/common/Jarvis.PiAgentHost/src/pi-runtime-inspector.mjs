import { readFile } from "node:fs/promises";
import { dirname, join, parse } from "node:path";
import { fileURLToPath } from "node:url";

const requiredExports = [
  "createAgentSession",
  "createAgentSessionRuntime",
  "ModelRuntime",
  "SessionManager",
];

function isCredentialEnvironmentKey(key) {
  const normalized = key.replaceAll(/[-_]/g, "").toLowerCase();
  return [
    "accesskey",
    "apikey",
    "credential",
    "password",
    "secret",
    "token",
  ].some((shape) => normalized.includes(shape));
}

async function findPackageManifest(entryPath, packageName) {
  let current = dirname(entryPath);
  const root = parse(current).root;
  while (current !== root) {
    const candidate = join(current, "package.json");
    try {
      const manifest = JSON.parse(await readFile(candidate, "utf8"));
      if (manifest.name === packageName) {
        return manifest;
      }
    } catch {
      // Continue upward until the matching package root is found.
    }
    current = dirname(current);
  }
  throw new Error(`Could not locate ${packageName} package metadata.`);
}

export async function inspectPiRuntime(contract) {
  process.env.PI_OFFLINE = "1";
  const credentialEnvironmentKeys = Object.keys(process.env)
    .filter(isCredentialEnvironmentKey)
    .sort();
  const packageName = contract.upstream.package;
  const entryPath = fileURLToPath(import.meta.resolve(packageName));
  const manifest = await findPackageManifest(entryPath, packageName);
  const pi = await import(packageName);
  const missingExports = requiredExports.filter(
    (name) => typeof pi[name] === "undefined",
  );
  const passed =
    manifest.version === contract.upstream.exactVersion &&
    missingExports.length === 0;

  return {
    schemaVersion: 1,
    receiptType: "jarvisv2-pi-agent-runtime-inspection",
    result: passed ? "passed-embedded-dependency" : "failed",
    package: packageName,
    expectedVersion: contract.upstream.exactVersion,
    installedVersion: manifest.version,
    requiredExports,
    missingExports,
    piOffline: process.env.PI_OFFLINE === "1",
    integrationMode: contract.runtime.integrationMode,
    transportReady: passed,
    credentialEnvironmentClean:
      credentialEnvironmentKeys.length === 0,
    credentialEnvironmentKeyCount: credentialEnvironmentKeys.length,
    sessionCreationEnabled: true,
    promptingEnabled: false,
    sessionPersistence: "in-memory",
    modelNetworkAllowed: false,
    resourceDiscoveryEnabled: false,
    desktopLaunchImplemented: true,
    credentialTransportAllowed: false,
    initialTools: [...contract.tools.initialAllowlist],
    shellMutationSupported: false,
    explorerMutationSupported: false,
    systemMutationSupported: false,
    activationPermitted: false,
    liveExplorer: "not-run",
  };
}
