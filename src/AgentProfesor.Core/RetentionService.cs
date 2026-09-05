namespace AgentProfesor.Core;

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
    public static void Run(VersionStore store, RetentionConfig config, DateTimeOffset now)
    {
        if (!config.Enabled)
            return;

        var keepAllCutoff = now.AddDays(-config.KeepAllDays);
        var hourlyCutoff = now.AddDays(-config.ThinToHourlyDays);

        foreach (var doc in store.ListDocuments())
        {
            var versions = store.ListVersions(doc.Id);
            if (versions.Count <= 1)
                continue;

            var hourlyZone = versions.Where(v => v.CapturedAt < keepAllCutoff && v.CapturedAt >= hourlyCutoff);
            ThinByBucket(store, hourlyZone, v => v.CapturedAt.UtcDateTime.ToString("yyyy-MM-dd-HH"));

            var beyondZone = versions.Where(v => v.CapturedAt < hourlyCutoff);
            ThinByBucket(store, beyondZone, config.KeepDailyBeyond
                ? v => v.CapturedAt.UtcDateTime.ToString("yyyy-MM-dd")
                : _ => "all-time");
        }
    }

    private static void ThinByBucket(VersionStore store, IEnumerable<VersionSummary> versions, Func<VersionSummary, string> bucketKey)
    {
        foreach (var group in versions.GroupBy(bucketKey))
        {
            var ordered = group.OrderBy(v => v.Id).ToList();
            if (ordered.Count <= 1)
                continue;

            var keep = ordered[^1];
            store.RebaseToKeyframe(keep.Id);

            foreach (var v in ordered.Take(ordered.Count - 1))
                store.DeleteVersion(v.Id);
        }
    }
}
