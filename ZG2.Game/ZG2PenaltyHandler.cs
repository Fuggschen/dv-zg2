using System;
using System.Collections.Generic;
using DV.ServicePenalty;
using DV.UI;
using DV.UIFramework;
using DV.Utils;

namespace ZG2
{
    /// <summary>
    /// Handles the post-job fine when a job is completed without ZG2 boards.
    /// Two-phase evaluation:
    ///   1. <see cref="PreEvaluate"/> (Prefix) — calculates fine, stores as pending
    ///      so <see cref="Patches.BookletPaymentPatch"/> can inject it into the booklet.
    ///   2. <see cref="CommitPendingFine"/> (Postfix) — registers the
    ///      <see cref="ZG2FineDebt"/> with the career terminal if the job completed.
    /// </summary>
    public static class ZG2PenaltyHandler
    {
        private static readonly List<ZG2FineDebt> activeDebts = new List<ZG2FineDebt>();

        /// <summary>Job ID for the pending fine (set by <see cref="PreEvaluate"/>,
        /// consumed by <see cref="CommitPendingFine"/>).</summary>
        internal static string PendingFineJobId { get; private set; }

        /// <summary>Fine amount for the pending fine.</summary>
        internal static float PendingFineAmount { get; private set; }

        public static void Initialise()
        {
            activeDebts.Clear();
            PendingFineJobId = null;
            PendingFineAmount = 0f;
        }

        /// <summary>
        /// Phase 1: Pre-evaluate whether a fine is due. Called from
        /// <see cref="Patches.JobValidatorPatch"/> Prefix <b>before</b> the booklet is
        /// created, so the fine amount is available for the payment page patch.
        /// Does NOT register the debt or modify tracker state.
        /// </summary>
        public static void PreEvaluate(string jobId, float totalWage)
        {
            PendingFineJobId = null;
            PendingFineAmount = 0f;

            if (!ZG2MPUtil.IsSinglePlayerOrHost) return;
            if (string.IsNullOrEmpty(jobId)) return;

            var trackData = ZG2JobTracker.GetJobData(jobId);
            if (trackData == null)
            {
                Main.Log($"Job {jobId} not tracked — no penalty to pre-evaluate.");
                return;
            }

            if (trackData.EverHadBoard)
            {
                Main.Log($"Job {jobId} had ZG2 board — no fine.");
                return;
            }

            float penaltyPercent = Main.Settings.PenaltyPercent;
            if (penaltyPercent <= 0f) return;

            float fineAmount = (float)Math.Round(totalWage * (penaltyPercent / 100f), 2);
            if (fineAmount <= 0f) return;

            PendingFineJobId = jobId;
            PendingFineAmount = fineAmount;
            Main.Log($"Job {jobId} pre-evaluated: fine will be {fineAmount:F0} ({penaltyPercent:F0}%)");
        }

        /// <summary>
        /// Phase 2: If a pending fine exists and the job actually completed,
        /// register the <see cref="ZG2FineDebt"/> with the career terminal and
        /// notify the player. Always cleans up tracker + pending state.
        /// </summary>
        public static void CommitPendingFine(string jobId, bool jobCompleted)
        {
            if (!ZG2MPUtil.IsSinglePlayerOrHost) return;

            try
            {
                if (!jobCompleted)
                {
                    if (PendingFineJobId != null)
                        Main.Log($"Job {jobId} did not complete — discarding pending fine.");
                    return;
                }

                // Clean up tracker now that the job is done
                ZG2JobTracker.StopTracking(jobId);

                if (PendingFineJobId != jobId || PendingFineAmount <= 0f)
                {
                    if (PendingFineJobId == null)
                        Main.Log($"Job {jobId} completed — no fine pending.");
                    return;
                }

                float fineAmount = PendingFineAmount;
                float penaltyPercent = Main.Settings.PenaltyPercent;

                var debt = new ZG2FineDebt(jobId, fineAmount);
                SingletonBehaviour<CareerManagerDebtController>.Instance.RegisterDebt(debt);
                activeDebts.Add(debt);

                Main.Log($"Job {jobId} completed WITHOUT ZG2 board. " +
                         $"Fine registered: {fineAmount:F0} ({penaltyPercent:F0}%)");

                ShowFineNotification(fineAmount, penaltyPercent);
            }
            finally
            {
                PendingFineJobId = null;
                PendingFineAmount = 0f;
            }
        }

        /// <summary>
        /// Called by <see cref="ZG2FineDebt.Pay"/> when the player pays off a fine.
        /// </summary>
        internal static void OnFineDebtPaid(ZG2FineDebt debt)
        {
            activeDebts.Remove(debt);
            Main.Log($"ZG2 fine paid: {debt.ID}");
        }

        /// <summary>
        /// Unregisters all active ZG2 debts (called during mod teardown / world unload).
        /// </summary>
        public static void Shutdown()
        {
            CareerManagerDebtController controller = null;

            try { controller = SingletonBehaviour<CareerManagerDebtController>.Instance; }
            catch { /* Instance may not exist during teardown */ }

            if (controller != null)
            {
                foreach (var debt in activeDebts)
                {
                    try { controller.UnregisterDebt(debt); }
                    catch (Exception ex) { Main.LogError($"Failed to unregister debt: {ex}"); }
                }
            }

            activeDebts.Clear();
            PendingFineJobId = null;
            PendingFineAmount = 0f;
        }

        private static void ShowFineNotification(float fineAmount, float penaltyPercent)
        {
            string message = $"ZG2 Fine: {fineAmount:F0} ({penaltyPercent:F0}% — missing Zugschlusstafel)";

            try
            {
                var canvasController = SingletonBehaviour<ACanvasController<CanvasController.ElementType>>.Instance;
                if (canvasController != null)
                {
                    canvasController.NotificationManager.ShowNotification(
                        message,
                        null,
                        8f,
                        false,
                        null,
                        false);
                }
                else
                {
                    Main.LogWarning($"Cannot show notification (no CanvasController). Fine: {message}");
                }
            }
            catch (Exception ex)
            {
                Main.LogError($"Failed to display fine notification: {ex}");
            }
        }
    }
}
