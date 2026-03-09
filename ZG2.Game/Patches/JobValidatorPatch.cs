using DV.ThingTypes;
using HarmonyLib;

namespace ZG2.Patches
{
    /// <summary>
    /// Harmony prefix + postfix on <see cref="JobValidator.ValidateJob"/>.
    /// <list type="bullet">
    ///   <item><b>Prefix</b> — calls <see cref="ZG2PenaltyHandler.PreEvaluate"/>
    ///   so the fine amount is available before the booklet is created.</item>
    ///   <item><b>Postfix</b> — calls <see cref="ZG2PenaltyHandler.CommitPendingFine"/>
    ///   to register the <see cref="ZG2FineDebt"/> if the job completed.</item>
    /// </list>
    /// </summary>
    [HarmonyPatch(typeof(JobValidator), nameof(JobValidator.ValidateJob))]
    public static class JobValidatorPatch
    {
        [HarmonyPrefix]
        public static void Prefix(JobBooklet jobBooklet)
        {
            if (!Main.Enabled) return;
            if (!ZG2MPUtil.IsSinglePlayerOrHost) return;
            if (jobBooklet == null || jobBooklet.job == null) return;

            var job = jobBooklet.job;
            if (job.State != JobState.InProgress) return;

            // Only haul jobs require ZG2 boards
            if (job.jobType != JobType.Transport && job.jobType != JobType.EmptyHaul) return;

            float totalWage = job.GetWageForTheJob();
            ZG2PenaltyHandler.PreEvaluate(job.ID, totalWage);
        }

        [HarmonyPostfix]
        public static void Postfix(JobBooklet jobBooklet)
        {
            if (!Main.Enabled) return;
            if (!ZG2MPUtil.IsSinglePlayerOrHost) return;
            if (jobBooklet == null || jobBooklet.job == null) return;

            var job = jobBooklet.job;
            bool completed = job.State == JobState.Completed;
            ZG2PenaltyHandler.CommitPendingFine(job.ID, completed);
        }
    }
}
