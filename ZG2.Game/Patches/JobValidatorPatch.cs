using HarmonyLib;

namespace ZG2.Patches
{
    /// <summary>
    /// Prefix/postfix on <see cref="JobValidator.ValidateJob"/>: applies the Zg2 payout reduction
    /// silently before the game pays and prints the report, and undoes it if the job was not completed.
    /// </summary>
    [HarmonyPatch(typeof(JobValidator), nameof(JobValidator.ValidateJob))]
    public static class JobValidatorPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Low)] // after other mods (e.g. Multiplayer) decided to run the original
        public static void Prefix(JobBooklet jobBooklet)
        {
            if (!Main.Enabled || jobBooklet == null || jobBooklet.job == null) return;
            ZG2PenaltyHandler.BeforeValidate(jobBooklet.job);
        }

        [HarmonyPostfix]
        public static void Postfix(JobBooklet jobBooklet)
        {
            if (!Main.Enabled || jobBooklet == null || jobBooklet.job == null) return;
            ZG2PenaltyHandler.AfterValidate(jobBooklet.job);
        }
    }
}
