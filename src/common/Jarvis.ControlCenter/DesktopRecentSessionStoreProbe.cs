using System.IO;
using System.Text;
using System.Text.Json;

namespace Jarvis.ControlCenter;

public sealed record DesktopRecentSessionStoreProbeReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    bool CurrentUserRoundTripPassed,
    bool ProviderAndRecencyPassed,
    bool DuplicateWorkspaceCollapsed,
    bool PlaintextWorkspaceAbsent,
    bool CiphertextTamperRejected,
    bool TemporaryStorageRemoved,
    bool MutationPerformed,
    IReadOnlyList<string> Failures);

public static class DesktopRecentSessionStoreProbe
{
    public static async Task<DesktopRecentSessionStoreProbeReceipt> RunAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        string workspace = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        string testRoot = Path.Combine(
            Path.GetTempPath(),
            $"jarvis2-recent-session-probe-{Guid.NewGuid():N}");
        List<string> failures = [];
        bool currentUserRoundTripPassed = false;
        bool providerAndRecencyPassed = false;
        bool duplicateWorkspaceCollapsed = false;
        bool plaintextWorkspaceAbsent = false;
        bool ciphertextTamperRejected = false;
        bool temporaryStorageRemoved = false;
        try
        {
            DesktopRecentSessionStore store = new(testRoot);
            DesktopRecentSessionStoreReceipt first = await store.RememberAsync(
                workspace,
                ConversationProviderKind.LocalDiagnostic,
                cancellationToken);
            DesktopRecentSessionCatalog firstLoad = await store.LoadAsync(
                cancellationToken);
            currentUserRoundTripPassed =
                first.Revision == 1 &&
                first.EntryCount == 1 &&
                firstLoad.Revision == 1 &&
                firstLoad.Entries.Count == 1 &&
                firstLoad.Entries[0].WorkspaceRoot == workspace &&
                firstLoad.Entries[0].Provider ==
                    ConversationProviderKind.LocalDiagnostic;

            DesktopRecentSessionStoreReceipt second = await store.RememberAsync(
                workspace,
                ConversationProviderKind.OpenAiResponses,
                cancellationToken);
            DesktopRecentSessionCatalog secondLoad = await store.LoadAsync(
                cancellationToken);
            providerAndRecencyPassed =
                second.Revision == 2 &&
                secondLoad.Revision == 2 &&
                secondLoad.Entries[0].Provider ==
                    ConversationProviderKind.OpenAiResponses &&
                secondLoad.Entries[0].LastOpenedAtUtc >=
                    firstLoad.Entries[0].LastOpenedAtUtc;
            duplicateWorkspaceCollapsed =
                second.EntryCount == 1 &&
                secondLoad.Entries.Count == 1;

            byte[] envelope = await File.ReadAllBytesAsync(
                store.CatalogPath,
                cancellationToken);
            string envelopeText = Encoding.UTF8.GetString(envelope);
            string encodedWorkspace = JsonEncodedText.Encode(workspace)
                .ToString();
            plaintextWorkspaceAbsent =
                !envelopeText.Contains(
                    workspace,
                    StringComparison.OrdinalIgnoreCase) &&
                !envelopeText.Contains(
                    encodedWorkspace,
                    StringComparison.OrdinalIgnoreCase);
            const string marker = "\"protectedPayload\":\"";
            int payloadStart = envelopeText.IndexOf(
                marker,
                StringComparison.Ordinal);
            if (payloadStart >= 0)
            {
                payloadStart += marker.Length;
                char original = envelopeText[payloadStart];
                char replacement = original == 'A' ? 'B' : 'A';
                string tampered =
                    envelopeText[..payloadStart] +
                    replacement +
                    envelopeText[(payloadStart + 1)..];
                await File.WriteAllTextAsync(
                    store.CatalogPath,
                    tampered,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken);
                try
                {
                    await store.LoadAsync(cancellationToken);
                }
                catch (InvalidDataException)
                {
                    ciphertextTamperRejected = true;
                }
            }
        }
        catch (Exception exception)
        {
            failures.Add(
                $"Recent-session probe failed unexpectedly: {exception.Message}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(testRoot))
                {
                    Directory.Delete(testRoot, recursive: true);
                }
                temporaryStorageRemoved = !Directory.Exists(testRoot);
            }
            catch (Exception exception)
            {
                failures.Add(
                    $"Temporary recent-session storage cleanup failed: {exception.Message}");
            }
        }

        AddFailure(
            failures,
            currentUserRoundTripPassed,
            "The CurrentUser-DPAPI catalog did not round-trip.");
        AddFailure(
            failures,
            providerAndRecencyPassed,
            "The latest provider or ordering was not retained.");
        AddFailure(
            failures,
            duplicateWorkspaceCollapsed,
            "Remembering one workspace twice created duplicate entries.");
        AddFailure(
            failures,
            plaintextWorkspaceAbsent,
            "The encrypted envelope exposed the plaintext workspace path.");
        AddFailure(
            failures,
            ciphertextTamperRejected,
            "A tampered encrypted catalog was not rejected.");
        AddFailure(
            failures,
            temporaryStorageRemoved,
            "The temporary probe storage remained on disk.");
        return new DesktopRecentSessionStoreProbeReceipt(
            1,
            "jarvisv2-desktop-recent-session-store-probe",
            failures.Count == 0 ? "passed" : "failed",
            currentUserRoundTripPassed,
            providerAndRecencyPassed,
            duplicateWorkspaceCollapsed,
            plaintextWorkspaceAbsent,
            ciphertextTamperRejected,
            temporaryStorageRemoved,
            false,
            failures);
    }

    private static void AddFailure(
        ICollection<string> failures,
        bool passed,
        string failure)
    {
        if (!passed)
        {
            failures.Add(failure);
        }
    }
}
