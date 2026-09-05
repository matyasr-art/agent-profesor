namespace AgentProfesor.Core;

public sealed record RetentionResult(int Rebased, int Deleted)
{
    public static readonly RetentionResult None = new(0, 0);
    public bool DidAnything => Rebased > 0 || Deleted > 0;
}

/// <summary>
/// Thins old version history down to save space, per <see cref="RetentionConfig"/>:
/// everything within KeepAllDays stays untouched; between KeepAllDays and ThinToHourlyDays
/// only one version per hour survives; beyond ThinToHourlyDays it's one per day (or the whole
/// range collapses to a single version if KeepDailyBeyond is off).
///
/// Every version's base_version_id points to exactly the version immediately before it in the
/// same document (that's how <see cref="VersionStore.Capture"/> always builds the chain), so a
/// version only ever has one possible dependent: the version right after it. Rebasing the
/// survivor of a bucket to a full keyframe before deleting its bucket-mates therefore can never
/// leave a dangling reference — nothing outside the bucket could have pointed at anything but
/// the survivor anyway.
/// </summary>
public static class RetentionService
{
    public static RetentionResult Run(VersionStore store, RetentionConfig config, DateTimeOffset now)
    {
        if (!config.Enabled)
            return RetentionResult.None;

        var keepAllCutoff = now.AddDays(-config.KeepAllDays);
        var hourlyCutoff = now.AddDays(-config.ThinToHourlyDays);
        var rebased = 0;
        var deleted = 0;

        foreach (var doc in store.ListDocuments())
        {
            var versions = store.ListVersions(doc.Id);
            if (versions.Count <= 1)
                continue;

            var hourlyZone = versions.Where(v => v.CapturedAt < keepAllCutoff && v.CapturedAt >= hourlyCutoff);
            ThinByBucket(store, hourlyZone, v => v.CapturedAt.UtcDateTime.ToString("yyyy-MM-dd-HH"), ref rebased, ref deleted);

            var beyondZone = versions.Where(v => v.CapturedAt < hourlyCutoff);
            ThinByBucket(store, beyondZone, config.KeepDailyBeyond
                ? v => v.CapturedAt.UtcDateTime.ToString("yyyy-MM-dd")
                : _ => "all-time", ref rebased, ref deleted);
        }

        return new RetentionResult(rebased, deleted);
    }

    private static void ThinByBucket(VersionStore store, IEnumerable<VersionSummary> versions, Func<VersionSummary, string> bucketKey, ref int rebased, ref int deleted)
    {
        foreach (var group in versions.GroupBy(bucketKey))
        {
            var ordered = group.OrderBy(v => v.Id).ToList();
            if (ordered.Count <= 1)
                continue;

            var keep = ordered[^1];
            store.RebaseToKeyframe(keep.Id);
            rebased++;

            foreach (var v in ordered.Take(ordered.Count - 1))
            {
                store.DeleteVersion(v.Id);
                deleted++;
            }
        }
    }
}
