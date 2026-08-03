using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BiddingBuddy.Bff.Core.Compliance;

/// <summary>
/// The tamper-evidence primitive behind <c>tender_versions</c>.
///
/// <para><c>ContentHash = sha256(canonical(snapshot))</c>,
/// <c>ChainHash = sha256(prevChainHash + contentHash)</c>, genesis takes an empty previous hash.
/// Altering any historical snapshot changes its content hash, which changes every chain hash after
/// it — so a single edit anywhere invalidates the whole tail, and an inspector can detect it by
/// replaying the chain from the audit file without having to trust us.</para>
///
/// <para><b>Canonicalisation is the load-bearing part.</b> Two JSON documents that differ only in
/// key order or whitespace are the same document, and if the hash disagrees the chain reports
/// tampering that did not happen — a false alarm in an audit tool is worse than no tool, because it
/// trains people to ignore it. Keys are therefore sorted recursively and the output written with no
/// indentation before hashing.</para>
///
/// <para><b>What this is not.</b> Tamper-EVIDENT, not tamper-PROOF: whoever can write these rows can
/// also recompute the chain over forged content. Closing that needs an anchor outside our control —
/// RFC 3161 timestamping from a licensed CA, which the STQC quality guidelines require and which is
/// Phase 3 work. <see cref="Method"/> says exactly this, and travels in the audit file so nobody
/// reading one has to infer the guarantee.</para>
/// </summary>
public static class TenderHashChain
{
    /// <summary>Describes the guarantee, verbatim, inside every audit file.</summary>
    public const string Method =
        "sha256(prev_chain_hash || sha256(canonical_json(snapshot))); keys sorted recursively, no whitespace. "
        + "Tamper-evident by replay, not notarised — no external time-stamping authority is involved.";

    /// <summary>SHA-256 of the canonical form of <paramref name="snapshotJson"/>, lowercase hex.</summary>
    public static string ComputeContentHash(string snapshotJson)
        => Sha256Hex(Canonicalize(snapshotJson));

    /// <summary>Links a content hash to its predecessor. <paramref name="prevChainHash"/> is empty at genesis.</summary>
    public static string ComputeChainHash(string prevChainHash, string contentHash)
        => Sha256Hex(prevChainHash + contentHash);

    /// <summary>
    /// Replays the chain over versions supplied in ascending version order and reports the first
    /// version that disagrees with its stored hashes.
    /// </summary>
    /// <returns>(intact, firstBrokenVersion) — <c>firstBrokenVersion</c> is null when intact.</returns>
    public static (bool Intact, int? FirstBrokenVersion) Verify(
        IEnumerable<(int Version, string SnapshotJson, string ContentHash, string PrevChainHash, string ChainHash)> versions)
    {
        var prev = string.Empty;

        foreach (var v in versions.OrderBy(x => x.Version))
        {
            // Recompute from the snapshot rather than trusting the stored content hash: checking a
            // stored hash against itself would pass on any row where both the content and its hash
            // were rewritten together, which is exactly what a competent tamperer would do.
            if (ComputeContentHash(v.SnapshotJson) != v.ContentHash) return (false, v.Version);
            if (v.PrevChainHash != prev) return (false, v.Version);
            if (ComputeChainHash(prev, v.ContentHash) != v.ChainHash) return (false, v.Version);

            prev = v.ChainHash;
        }

        return (true, null);
    }

    /// <summary>
    /// Sorts every object's keys recursively and re-serializes without whitespace, so that documents
    /// differing only in key order or formatting hash identically.
    /// </summary>
    /// <remarks>
    /// Falls back to the raw string if the input will not parse. A hash over unparseable input is
    /// still a stable hash — it just loses order-insensitivity — and throwing here would fail a
    /// publish rather than a validation, which is the wrong place to discover bad JSON.
    /// </remarks>
    public static string Canonicalize(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            var sorted = Sort(node);
            return sorted?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? "null";
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static JsonNode? Sort(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                // Ordinal, not culture-aware: a culture-sensitive sort would hash differently on a
                // machine with a different locale, and the chain would "break" on a server move.
                var result = new JsonObject();
                foreach (var kv in obj.OrderBy(k => k.Key, StringComparer.Ordinal))
                    result[kv.Key] = Sort(kv.Value?.DeepClone());
                return result;
            }
            case JsonArray arr:
            {
                // Array ORDER IS PRESERVED — it is data. Line items 1,2,3 are not the same document
                // as 3,2,1, and sorting them would hide a reordering that changes what was tendered.
                var result = new JsonArray();
                foreach (var item in arr)
                    result.Add(Sort(item?.DeepClone()));
                return result;
            }
            default:
                return node?.DeepClone();
        }
    }

    private static string Sha256Hex(string input)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}
