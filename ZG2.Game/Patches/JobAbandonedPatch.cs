using DV.Logic.Job;
using HarmonyLib;

namespace ZG2.Patches
{
    /// <summary>
    /// Harmony postfix on <see cref="JobsManager.AbandonJob"/>.
    /// Stops ZG2 tracking when the player abandons a job.
    /// </summary>
    [HarmonyPatch(typeof(JobsManager), nameof(JobsManager.AbandonJob))]
    public static class JobAbandonedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Job job)
        {
            if (!Main.Enabled || job == null) return;
            if (!ZG2MPUtil.IsSinglePlayerOrHost) return;

            ZG2JobTracker.StopTracking(job.ID);
        }
    }
}
