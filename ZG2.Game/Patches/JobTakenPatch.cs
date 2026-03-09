using DV.Logic.Job;
using DV.ThingTypes;
using HarmonyLib;

namespace ZG2.Patches
{
    /// <summary>
    /// Harmony postfix on <see cref="JobsManager.TakeJob"/>.
    /// Starts ZG2 board tracking when the player accepts a job.
    /// </summary>
    [HarmonyPatch(typeof(JobsManager), nameof(JobsManager.TakeJob))]
    public static class JobTakenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Job job, bool takenViaLoadGame)
        {
            if (!Main.Enabled || job == null) return;
            if (!ZG2MPUtil.IsSinglePlayerOrHost) return;

            // Only haul jobs require ZG2 boards — shunting and mod jobs are exempt
            if (job.jobType != JobType.Transport && job.jobType != JobType.EmptyHaul) return;

            // Don't re-start tracking for jobs restored from a save file
            // (those are restored by ZG2SaveManager)
            if (takenViaLoadGame) return;

            ZG2JobTracker.StartTracking(job.ID);
        }
    }
}
