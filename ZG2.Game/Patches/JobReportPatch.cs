using System;
using System.Collections.Generic;
using System.Reflection;
using DV.Booklets;
using DV.Logic.Job;
using DV.RenderTextureSystem.BookletRender;
using DV.ThingTypes;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ZG2.Patches
{
    using EntryState = JobReportTasksTemplatePaperData.EntryState;

    /// <summary>
    /// MP clients build the job report from their own job copy → apply the reduction there too.
    /// </summary>
    [HarmonyPatch(typeof(BookletCreator), nameof(BookletCreator.CreateJobReport))]
    public static class CreateJobReportPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Job job)
        {
            if (!Main.Enabled) return;
            ZG2PenaltyHandler.BeforeCreateReport(job);
        }
    }

    /// <summary>
    /// Hands the Zg2 deduction of a job to the payment page of its report (4th line, see
    /// <see cref="PaymentRowPatch"/>). There is no separate Zg2 page – the line on the payment page is enough.
    /// </summary>
    [HarmonyPatch(typeof(BookletCreator_JobReport), nameof(BookletCreator_JobReport.GetReportTemplateData))]
    public static class JobReportPagePatch
    {
        [HarmonyPostfix]
        public static void Postfix(List<TemplatePaperData> __result, Job_data __0)
        {
            if (!Main.Enabled || __result == null || __0 == null) return;

            try
            {
                var log = ZG2JobTracker.GetLog(__0.ID);
                if (log != null) PaymentRowPatch.PrepareRow(__result, __0, log);
            }
            catch (Exception ex)
            {
                Main.LogError($"Zg2: failed to prepare report line: {ex}");
            }
        }
    }

    /// <summary>
    /// Payment page of the job report: adds a 4th line below the game's penalty line
    /// ("Fahren ohne Zugschluss (3x)  -15%"). Base payment and bonus are shown unreduced again,
    /// the total stays the amount actually paid. Without failed checkpoints the page is untouched.
    ///
    /// The line is a copy of the penalty line's label and value texts; everything below it
    /// (total, separator) is moved down by one row and moved back when the page template is
    /// reused for a report without Zg2 penalty.
    /// </summary>
    [HarmonyPatch(typeof(JobReportPaymentTemplatePaper), nameof(JobReportPaymentTemplatePaper.FillInData))]
    public static class PaymentRowPatch
    {
        private class Row
        {
            public string Label;
            public string Value;
        }

        private class Extra
        {
            public TextMeshProUGUI Label;
            public TextMeshProUGUI Value;
            public readonly List<Transform> Moved = new List<Transform>();
            public readonly List<Vector3> Original = new List<Vector3>();
            public Vector3 Step;
            public bool Shifted;
        }

        private static readonly Dictionary<JobReportPaymentTemplatePaperData, Row> pending = new Dictionary<JobReportPaymentTemplatePaperData, Row>();
        private static readonly Dictionary<JobReportPaymentTemplatePaper, Extra> extras = new Dictionary<JobReportPaymentTemplatePaper, Extra>();

        /// <summary>Called from the report data postfix: remembers the line for the payment page.</summary>
        // NOT named "Prepare": Harmony calls a static method with that name in a patch class itself.
        public static void PrepareRow(List<TemplatePaperData> pages, Job_data data, ZG2JobLog log)
        {
            if (log.Failed <= 0) return;

            float per = log.PenaltyPerCheckpoint >= 0f ? log.PenaltyPerCheckpoint : Main.Settings.PenaltyPerCheckpoint;
            float max = log.MaxPenalty >= 0f ? log.MaxPenalty : Main.Settings.MaxPenalty;
            float pct = log.AppliedPenaltyPercent >= 0f ? log.AppliedPenaltyPercent : ZG2JobLog.ComputePenaltyPercent(log.Failed, per, max);
            if (pct <= 0f) return;

            foreach (var page in pages)
            {
                if (!(page is JobReportPaymentTemplatePaperData pay)) continue;

                // The wage was reduced before payment → show base and bonus unreduced again.
                float factor = 1f - pct / 100f;
                if (log.DeductedAmount > 0f && factor > 0.0001f)
                {
                    pay.basePayment = "+$" + Money(Mathf.Round(data.basePayment / factor));
                    pay.bonusPayment = "+$" + Money(Mathf.Round(data.bonusPayment / factor));
                }

                float deducted = log.DeductedAmount;
                if (deducted <= 0f && factor > 0.0001f)
                    deducted = Mathf.Round((data.basePayment + data.bonusPayment) / factor * (1f - factor));

                if (pending.Count > 64) pending.Clear();
                pending[pay] = new Row
                {
                    Label = ZG2Loc.T($"Fahren ohne Zugschluss (-{pct:0.#} %)", $"Running without train end (-{pct:0.#} %)"),
                    Value = "-$" + Money(deducted)
                };
            }
        }

        private static string Money(float v)
        {
            try { return v.ToString("N0", DV.Localization.LocalizationAPI.CC); }
            catch { return v.ToString("N0"); }
        }

        [HarmonyPostfix]
        public static void Postfix(JobReportPaymentTemplatePaper __instance)
        {
            try
            {
                var data = __instance.data;
                extras.TryGetValue(__instance, out var extra);

                if (data == null || !pending.TryGetValue(data, out var row))
                {
                    if (extra != null) Hide(extra);
                    return;
                }
                // Not removed: FillInData can run several times for the same page.

                if (extra == null)
                {
                    extra = Create(__instance);
                    if (extra == null) return;
                    extras[__instance] = extra;
                }

                extra.Label.text = row.Label;
                extra.Value.text = row.Value;
                extra.Value.color = JobReportPaymentTemplatePaperData.EXPIRATION_PENALTY_COLOR;
                extra.Label.gameObject.SetActive(true);
                extra.Value.gameObject.SetActive(true);

                if (!extra.Shifted)
                {
                    for (int i = 0; i < extra.Moved.Count; i++)
                        if (extra.Moved[i] != null) extra.Moved[i].position = extra.Original[i] + extra.Step;
                    extra.Shifted = true;
                }
            }
            catch (Exception ex)
            {
                Main.LogError($"Zg2: payment line failed: {ex.Message}");
            }
        }

        private static void Hide(Extra extra)
        {
            if (extra.Label != null) extra.Label.gameObject.SetActive(false);
            if (extra.Value != null) extra.Value.gameObject.SetActive(false);
            if (!extra.Shifted) return;
            for (int i = 0; i < extra.Moved.Count; i++)
                if (extra.Moved[i] != null) extra.Moved[i].position = extra.Original[i];
            extra.Shifted = false;
        }

        private static Extra Create(JobReportPaymentTemplatePaper page)
        {
            var value = page.expirationPenalty;
            var bonus = page.bonusPayment;
            if (value == null || bonus == null) return null;

            var label = FindLabel(page, value);
            if (label == null)
            {
                Main.LogWarning("Zg2: label of the penalty line not found – payment line skipped.");
                return null;
            }

            // One row = distance between the bonus line and the penalty line
            Vector3 step = value.transform.position - bonus.transform.position;

            var extra = new Extra { Step = step };
            extra.Label = Clone(label, step);
            extra.Value = Clone(value, step);

            // Nothing is moved: the payment page has free space between the penalty line and the
            // total, and shifting the total pushed it out of its grey box.
            var candidates = new HashSet<Transform>();
            foreach (var t in candidates)
            {
                extra.Moved.Add(t);
                extra.Original.Add(t.position);
            }
            return extra;
        }

        private static void AddBelow(HashSet<Transform> set, Transform parent, float y, Vector3 step)
        {
            if (parent == null) return;
            bool down = step.y < 0f;
            foreach (Transform child in parent)
            {
                float cy = child.position.y;
                if (down ? cy < y - Mathf.Abs(step.y) * 0.3f : cy > y + Mathf.Abs(step.y) * 0.3f)
                    set.Add(child);
            }
        }

        /// <summary>Label = the non-data text left of the value, on the same height.</summary>
        private static TextMeshProUGUI FindLabel(JobReportPaymentTemplatePaper page, TextMeshProUGUI value)
        {
            var dataFields = new HashSet<TextMeshProUGUI> { page.basePayment, page.bonusPayment, page.expirationPenalty, page.totalPayment, page.totalPaymentText };
            TextMeshProUGUI best = null;
            float bestDy = float.MaxValue;
            float vy = value.transform.position.y, vx = value.transform.position.x;
            foreach (var t in page.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (t == null || dataFields.Contains(t)) continue;
                if (t.transform.position.x >= vx) continue;
                float dy = Mathf.Abs(t.transform.position.y - vy);
                if (dy < bestDy) { bestDy = dy; best = t; }
            }
            return best;
        }

        private static TextMeshProUGUI Clone(TextMeshProUGUI source, Vector3 step)
        {
            var go = UnityEngine.Object.Instantiate(source.gameObject, source.transform.parent);
            go.name = source.gameObject.name + "_Zg2";
            // Remove localisation and other behaviours that could overwrite the text
            foreach (var mb in go.GetComponents<MonoBehaviour>())
                if (!(mb is TMP_Text)) UnityEngine.Object.DestroyImmediate(mb);
            go.transform.position = source.transform.position + step;
            return go.GetComponent<TextMeshProUGUI>();
        }
    }
}
