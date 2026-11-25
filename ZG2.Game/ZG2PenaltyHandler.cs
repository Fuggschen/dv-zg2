using System;
using System.Collections.Generic;
using System.Reflection;
using DV.InventorySystem;
using DV.Logic.Job;
using DV.ThingTypes;

namespace ZG2
{
    /// <summary>
    /// Reduces the payout of a job according to its failed Zg2 checkpoints.
    ///
    ///   penalty = min(failed × PenaltyPerCheckpoint, MaxPenalty)
    ///   payout  = regular payout × (1 − penalty)
    ///
    /// The reduction is applied silently (no HUD, no notification, no debt): right before the
    /// game validates the job, the job's initial wage is scaled down. The game then pays and
    /// prints the job report with the reduced amount; the Zg2 page in the report explains why.
    /// If validation does not complete the job, the original wage is restored.
    /// If the wage cannot be changed (unexpected game version), the difference is taken from
    /// the wallet after completion instead.
    /// </summary>
    public static class ZG2PenaltyHandler
    {
        private class Pending
        {
            public float OriginalWage;
            public bool WageChanged;
            public float Deduction;
        }

        private static readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>();

        /// <summary>Jobs whose local wage was already reduced (host at validation, client at report).</summary>
        private static readonly HashSet<string> appliedJobs = new HashSet<string>();

        public static void Initialise()
        {
            pending.Clear();
            appliedJobs.Clear();
        }

        public static void Shutdown()
        {
            pending.Clear();
            appliedJobs.Clear();
        }

        /// <summary>Prefix of JobValidator.ValidateJob (host / single player).</summary>
        public static void BeforeValidate(Job job)
        {
            if (job == null || job.State != JobState.InProgress) return;
            if (!ZG2MPUtil.IsSinglePlayerOrHost) return;

            var log = ZG2JobTracker.GetLog(job.ID);
            if (log == null) return;

            var s = Main.Settings;
            float pct = ZG2JobLog.ComputePenaltyPercent(log.Failed, s.PenaltyPerCheckpoint, s.MaxPenalty);

            // Remember what will be applied so the report page can show it.
            log.PenaltyPerCheckpoint = s.PenaltyPerCheckpoint;
            log.MaxPenalty = s.MaxPenalty;
            log.AppliedPenaltyPercent = pct;
            log.DeductedAmount = 0f;

            if (pct <= 0f || appliedJobs.Contains(job.ID)) return;

            float wageBefore = job.GetWageForTheJob();
            float original = job.initialWage;
            var p = new Pending { OriginalWage = original, Deduction = (float)Math.Round(wageBefore * pct / 100f, 2) };

            if (JobWage.TrySet(job, original * (1f - pct / 100f)))
            {
                float wageAfter = job.GetWageForTheJob();
                if (wageAfter < wageBefore - 0.01f)
                {
                    p.WageChanged = true;
                    p.Deduction = wageBefore - wageAfter;
                }
                else
                {
                    JobWage.TrySet(job, original); // wage not derived from initialWage → use wallet fallback
                }
            }

            log.DeductedAmount = p.Deduction;
            pending[job.ID] = p;
            if (p.WageChanged) appliedJobs.Add(job.ID);

            // Clients print their own report → send the final protocol before the job state update.
            if (ZG2MPUtil.IsMultiplayerActive)
                ZG2MPUtil.BroadcastJobLog(job.ID, log.Serialize());

            Main.Log($"Zg2: job {job.ID} – {log.Failed} failed checkpoint(s) → -{pct:0.#}% (-${p.Deduction:N0}).");
        }

        /// <summary>Postfix of JobValidator.ValidateJob (host / single player).</summary>
        public static void AfterValidate(Job job)
        {
            if (job == null || !ZG2MPUtil.IsSinglePlayerOrHost) return;

            pending.TryGetValue(job.ID, out var p);
            pending.Remove(job.ID);

            var log = ZG2JobTracker.GetLog(job.ID);

            if (job.State != JobState.Completed)
            {
                // Not completed (tasks not done, printer cooldown, ...) – undo everything.
                if (p != null && p.WageChanged) JobWage.TrySet(job, p.OriginalWage);
                appliedJobs.Remove(job.ID);
                if (log != null)
                {
                    log.AppliedPenaltyPercent = -1f;
                    log.DeductedAmount = 0f;
                    if (ZG2MPUtil.IsMultiplayerActive) ZG2MPUtil.BroadcastJobLog(job.ID, log.Serialize());
                }
                return;
            }

            if (p != null && !p.WageChanged && p.Deduction > 0f)
            {
                try
                {
                    Inventory.Instance.RemoveMoney(p.Deduction);
                }
                catch (Exception ex)
                {
                    Main.LogError($"Zg2: could not deduct {p.Deduction:F0} for job {job.ID}: {ex.Message}");
                }
            }

            ZG2JobTracker.FinishJob(job.ID);
        }

        /// <summary>
        /// Prefix of BookletCreator.CreateJobReport. On MP clients the report is created from the
        /// client's own job copy, so the reduction is applied there too (display only – money is
        /// handled by the host).
        /// </summary>
        public static void BeforeCreateReport(Job job)
        {
            if (job == null || ZG2MPUtil.IsSinglePlayerOrHost) return;
            if (appliedJobs.Contains(job.ID)) return;

            var log = ZG2JobTracker.GetLog(job.ID);
            if (log == null || log.AppliedPenaltyPercent <= 0f) return;

            if (JobWage.TrySet(job, job.initialWage * (1f - log.AppliedPenaltyPercent / 100f)))
                appliedJobs.Add(job.ID);
        }

        /// <summary>Reflection access to Job.initialWage (property or field, setter may be private).</summary>
        private static class JobWage
        {
            private static bool resolved;
            private static MethodInfo setter;
            private static FieldInfo field;

            public static bool TrySet(Job job, float value)
            {
                try
                {
                    if (!resolved)
                    {
                        resolved = true;
                        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                        var t = typeof(Job);
                        setter = t.GetProperty("initialWage", all)?.GetSetMethod(true);
                        if (setter == null)
                            field = t.GetField("initialWage", all) ?? t.GetField("<initialWage>k__BackingField", all);
                        if (setter == null && field == null)
                            Main.LogWarning("Zg2: Job.initialWage is not writable – using wallet deduction instead.");
                    }

                    if (setter != null) { setter.Invoke(job, new object[] { value }); return true; }
                    if (field != null) { field.SetValue(job, value); return true; }
                }
                catch (Exception ex)
                {
                    Main.LogError($"Zg2: failed to set job wage: {ex.Message}");
                }
                return false;
            }
        }
    }
}
