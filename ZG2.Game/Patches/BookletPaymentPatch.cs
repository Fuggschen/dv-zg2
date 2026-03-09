/*
using System;
using System.Collections.Generic;
using DV.Booklets;
using DV.RenderTextureSystem.BookletRender;
using DV.ThingTypes;
using HarmonyLib;

namespace ZG2.Patches
{
    /// <summary>
    /// Harmony postfix on <see cref="BookletCreator_JobReport.GetReportTemplateData"/>.
    /// Injects the ZG2 fine into the payment summary page of the validated job booklet
    /// by writing the <c>expirationPenalty</c> field (rendered in red) and adjusting
    /// the displayed total payment accordingly.
    /// </summary>
    [HarmonyPatch(typeof(BookletCreator_JobReport), "GetReportTemplateData")]
    public static class BookletPaymentPatch
    {
        [HarmonyPostfix]
        public static void Postfix(List<TemplatePaperData> __result, Job_data data)
        {
            if (!Main.Enabled) return;
            if (!ZG2MPUtil.IsSinglePlayerOrHost) return;
            if (data == null || data.state != JobState.Completed) return;

            string pendingJobId = ZG2PenaltyHandler.PendingFineJobId;
            float fineAmount = ZG2PenaltyHandler.PendingFineAmount;

            if (pendingJobId == null || pendingJobId != data.ID || fineAmount <= 0f) return;

            for (int i = 0; i < __result.Count; i++)
            {
                if (__result[i] is JobReportPaymentTemplatePaperData paymentData)
                {
                    // Show the fine on the penalty line (auto-coloured red by the renderer)
                    paymentData.expirationPenalty = "-$" + fineAmount.ToString("N0");

                    // Adjust the displayed total to reflect the deduction
                    float adjustedTotal = data.totalPayment - fineAmount;
                    paymentData.totalPayment = (adjustedTotal >= 0f ? "+$" : "-$") +
                        Math.Abs(adjustedTotal).ToString("N0");

                    Main.Log($"Booklet payment page updated: ZG2 fine -${fineAmount:F0} on job {data.ID}");
                    break;
                }
            }
        }
    }
}
*/