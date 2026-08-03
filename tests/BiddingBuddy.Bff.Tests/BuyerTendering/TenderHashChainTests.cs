using BiddingBuddy.Bff.Core.Compliance;
using Xunit;

namespace BiddingBuddy.Bff.Tests.BuyerTendering;

/// <summary>
/// The hash chain is the audit-evidence primitive: an inspector replays it from the downloadable
/// audit file to check that no published version was altered after the fact.
///
/// <para>Two failure modes matter and they pull in opposite directions. If canonicalisation is too
/// weak, a document that only changed key order reports as tampered — a false alarm in an audit tool
/// is worse than no tool, because it teaches people to ignore it. If it is too strong, a real edit
/// hashes identically and the chain certifies a forgery. Both directions are pinned here.</para>
/// </summary>
public sealed class TenderHashChainTests
{
    // ── Canonicalisation ────────────────────────────────────────────────────

    [Fact]
    public void Key_order_does_not_change_the_hash()
    {
        var a = """{"title":"Supply of laptops","state":"Kerala","value":250000}""";
        var b = """{"value":250000,"title":"Supply of laptops","state":"Kerala"}""";

        Assert.Equal(
            TenderHashChain.ComputeContentHash(a),
            TenderHashChain.ComputeContentHash(b));
    }

    [Fact]
    public void Whitespace_and_indentation_do_not_change_the_hash()
    {
        var compact = """{"title":"Road works","items":[{"qty":3}]}""";
        var pretty = """
            {
                "title" : "Road works",
                "items" : [ { "qty" : 3 } ]
            }
            """;

        Assert.Equal(
            TenderHashChain.ComputeContentHash(compact),
            TenderHashChain.ComputeContentHash(pretty));
    }

    [Fact]
    public void Nested_object_key_order_does_not_change_the_hash()
    {
        var a = """{"outer":{"b":2,"a":1},"z":{"y":{"x":9,"w":8}}}""";
        var b = """{"z":{"y":{"w":8,"x":9}},"outer":{"a":1,"b":2}}""";

        Assert.Equal(
            TenderHashChain.ComputeContentHash(a),
            TenderHashChain.ComputeContentHash(b));
    }

    [Fact]
    public void Array_order_DOES_change_the_hash()
    {
        // Array order is data, not formatting. Line items 1,2,3 are not the same bill of quantities
        // as 3,2,1 — sorting them would hide a reordering that changes what was tendered.
        var a = """{"items":[{"n":"cement"},{"n":"steel"}]}""";
        var b = """{"items":[{"n":"steel"},{"n":"cement"}]}""";

        Assert.NotEqual(
            TenderHashChain.ComputeContentHash(a),
            TenderHashChain.ComputeContentHash(b));
    }

    [Fact]
    public void A_changed_value_changes_the_hash()
    {
        var before = """{"emdAmount":50000}""";
        var after = """{"emdAmount":5000}""";

        Assert.NotEqual(
            TenderHashChain.ComputeContentHash(before),
            TenderHashChain.ComputeContentHash(after));
    }

    [Fact]
    public void Unparseable_json_still_hashes_stably_instead_of_throwing()
    {
        // A publish must not fail because a snapshot did not round-trip. The hash loses
        // order-insensitivity for such input, which is acceptable; throwing here would surface bad
        // JSON at publish time rather than at validation time, which is the wrong place to find it.
        const string junk = "{not json at all";

        Assert.Equal(
            TenderHashChain.ComputeContentHash(junk),
            TenderHashChain.ComputeContentHash(junk));
    }

    // ── Chaining ────────────────────────────────────────────────────────────

    [Fact]
    public void Genesis_links_from_the_empty_previous_hash()
    {
        var content = TenderHashChain.ComputeContentHash("""{"v":1}""");
        var chain = TenderHashChain.ComputeChainHash(string.Empty, content);

        Assert.NotEqual(content, chain);
        Assert.Equal(64, chain.Length);   // sha256 hex
    }

    [Fact]
    public void The_same_content_at_a_different_chain_position_hashes_differently()
    {
        // This is the property that makes the chain a chain rather than a list of hashes: an
        // identical snapshot published twice must not produce interchangeable links, or a version
        // could be moved within the history undetected.
        var content = TenderHashChain.ComputeContentHash("""{"v":1}""");

        var atGenesis = TenderHashChain.ComputeChainHash(string.Empty, content);
        var laterOn = TenderHashChain.ComputeChainHash("deadbeef", content);

        Assert.NotEqual(atGenesis, laterOn);
    }

    // ── Verification ────────────────────────────────────────────────────────

    [Fact]
    public void An_untouched_chain_verifies()
    {
        var versions = BuildChain("""{"v":1}""", """{"v":2}""", """{"v":3}""");

        var (intact, broken) = TenderHashChain.Verify(versions);

        Assert.True(intact);
        Assert.Null(broken);
    }

    [Fact]
    public void Verification_survives_versions_arriving_out_of_order()
    {
        // The audit file is assembled from a query; a caller must not be able to cause a false
        // tampering report by handing them over unsorted.
        var versions = BuildChain("""{"v":1}""", """{"v":2}""", """{"v":3}""");

        var (intact, _) = TenderHashChain.Verify(versions.OrderByDescending(v => v.Version));

        Assert.True(intact);
    }

    [Fact]
    public void Editing_a_historical_snapshot_is_detected_at_that_version()
    {
        var versions = BuildChain("""{"v":1}""", """{"v":2}""", """{"v":3}""");

        // Someone rewrites version 2's content but cannot recompute the rest of the chain.
        var v2 = versions[1];
        versions[1] = (v2.Version, """{"v":"tampered"}""", v2.ContentHash, v2.PrevChainHash, v2.ChainHash);

        var (intact, broken) = TenderHashChain.Verify(versions);

        Assert.False(intact);
        Assert.Equal(2, broken);
    }

    [Fact]
    public void Rewriting_a_snapshot_AND_its_content_hash_together_is_still_detected()
    {
        // The competent tamper: change the content and its hash so the row is self-consistent. It
        // is caught because the chain hash still commits to the old content — which is why Verify
        // recomputes from the snapshot rather than trusting the stored content hash.
        var versions = BuildChain("""{"v":1}""", """{"v":2}""", """{"v":3}""");

        const string forged = """{"v":"forged"}""";
        var v2 = versions[1];
        versions[1] = (
            v2.Version, forged, TenderHashChain.ComputeContentHash(forged), v2.PrevChainHash, v2.ChainHash);

        var (intact, broken) = TenderHashChain.Verify(versions);

        Assert.False(intact);
        Assert.Equal(2, broken);
    }

    [Fact]
    public void Deleting_a_version_from_the_middle_is_detected()
    {
        var versions = BuildChain("""{"v":1}""", """{"v":2}""", """{"v":3}""");
        versions.RemoveAt(1);

        var (intact, broken) = TenderHashChain.Verify(versions);

        Assert.False(intact);
        Assert.Equal(3, broken);   // v3's recorded previous hash no longer matches v1's chain hash
    }

    [Fact]
    public void An_empty_history_verifies_vacuously()
    {
        // A draft that was never published has no versions. That is not a broken chain, and
        // reporting it as one would light up the audit viewer for every unpublished tender.
        var (intact, broken) = TenderHashChain.Verify([]);

        Assert.True(intact);
        Assert.Null(broken);
    }

    /// <summary>Builds a correctly-linked chain over the given snapshots.</summary>
    private static List<(int Version, string SnapshotJson, string ContentHash, string PrevChainHash, string ChainHash)>
        BuildChain(params string[] snapshots)
    {
        var rows = new List<(int, string, string, string, string)>();
        var prev = string.Empty;

        for (var i = 0; i < snapshots.Length; i++)
        {
            var content = TenderHashChain.ComputeContentHash(snapshots[i]);
            var chain = TenderHashChain.ComputeChainHash(prev, content);
            rows.Add((i + 1, snapshots[i], content, prev, chain));
            prev = chain;
        }

        return rows;
    }
}
