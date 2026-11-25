using DV.Logic.Job;
using DV.ThingTypes;
using HarmonyLib;

namespace ZG2.Patches
{
    /// <summary>
    /// Postfix on <see cref="IdGenerator.GenerateJobID"/>, running after all other mods' patches:
    /// every newly generated job ID tells ZG2 which job types and job ID codes exist. This catches
    /// mods that only rename the ID of vanilla jobs (e.g. TrainServiceRework "SH", "MH", "SO"),
    /// event-based instead of scanning stations.
    /// </summary>
    [HarmonyPatch(typeof(IdGenerator), nameof(IdGenerator.GenerateJobID))]
    public static class JobIdPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(JobType jobType, string __result)
        {
            if (!Main.Enabled || string.IsNullOrEmpty(__result)) return;
            try { ZG2JobTypes.RecordSeenId(jobType, __result); }
            catch { /* never break job generation */ }
        }
    }
}