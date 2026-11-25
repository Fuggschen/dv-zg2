using DV.Logic.Job;
using HarmonyLib;

namespace ZG2.Patches
{
    /// <summary>
    /// Postfix on <see cref="JobsManager.TakeJob"/>: starts the Zg2 protocol for configured job types.
    /// Jobs restored from a save are picked up by the tracker's reconcile step.
    /// </summary>
    [HarmonyPatch(typeof(JobsManager), nameof(JobsManager.TakeJob))]
    public static class JobTakenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Job job)
        {
            if (!Main.Enabled || job == null) return;
            ZG2JobTypes.RecordSeen(job);
            ZG2JobTracker.StartTracking(job);
        }
    }
}
