using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace ZG2
{
    public enum ZG2CheckpointType : byte
    {
        Departure = 0,  // Ausfahrt
        Arrival = 1     // Einfahrt
    }

    public class ZG2Checkpoint
    {
        public ZG2CheckpointType Type;
        public string Station;
        public bool Ok;
        public ZG2Fail Reason;
        public int Valid;
        public int Required;
        public float Value;
        public string EndCarId;
    }

    /// <summary>
    /// Checkpoint protocol of one Zg2-relevant job.
    /// </summary>
    public class ZG2JobLog
    {
        public const int MAX_ENTRIES = 200;

        public string JobId;
        public readonly List<ZG2Checkpoint> Entries = new List<ZG2Checkpoint>();
        /// <summary>Failed departure checkpoints – only these reduce the payout.</summary>
        public int Failed;

        /// <summary>Penalty that was applied at validation (percent), -1 while not validated.</summary>
        public float AppliedPenaltyPercent = -1f;
        /// <summary>Money deducted at validation.</summary>
        public float DeductedAmount;
        /// <summary>Settings used for the penalty (stored so the booklet shows what was really applied).</summary>
        public float PenaltyPerCheckpoint = -1f;
        public float MaxPenalty = -1f;

        public ZG2JobLog(string jobId)
        {
            JobId = jobId;
        }

        public void Add(ZG2Checkpoint cp)
        {
            // One run without train end = one failed departure; the arrival at the end of the same run
            // is logged but not counted again.
            if (!cp.Ok && cp.Type == ZG2CheckpointType.Departure) Failed++;
            Entries.Add(cp);
            // Keep memory and packet size bounded; the failure counter stays exact.
            if (Entries.Count > MAX_ENTRIES) Entries.RemoveAt(0);
        }

        public static float ComputePenaltyPercent(int failed, float perCheckpoint, float max)
        {
            if (failed <= 0) return 0f;
            return Mathf.Clamp(failed * perCheckpoint, 0f, Mathf.Clamp(max, 0f, 100f));
        }

        public float CurrentPenaltyPercent()
        {
            if (AppliedPenaltyPercent >= 0f) return AppliedPenaltyPercent;
            var s = Main.Settings;
            return ComputePenaltyPercent(Failed, s.PenaltyPerCheckpoint, s.MaxPenalty);
        }

        #region Serialisation (MP packets and save game)

        private static readonly CultureInfo IC = CultureInfo.InvariantCulture;

        public string Serialize()
        {
            var sb = new StringBuilder(64 + Entries.Count * 24);
            sb.Append(Failed).Append('|')
              .Append(AppliedPenaltyPercent.ToString("0.##", IC)).Append('|')
              .Append(DeductedAmount.ToString("0.##", IC)).Append('|')
              .Append(PenaltyPerCheckpoint.ToString("0.##", IC)).Append('|')
              .Append(MaxPenalty.ToString("0.##", IC)).Append('|');

            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                if (i > 0) sb.Append(';');
                sb.Append((int)e.Type).Append(',')
                  .Append(Clean(e.Station)).Append(',')
                  .Append(e.Ok ? '1' : '0').Append(',')
                  .Append((int)e.Reason).Append(',')
                  .Append(e.Valid).Append(',')
                  .Append(e.Required).Append(',')
                  .Append(e.Value.ToString("0.##", IC)).Append(',')
                  .Append(Clean(e.EndCarId));
            }
            return sb.ToString();
        }

        public static ZG2JobLog Deserialize(string jobId, string data)
        {
            var log = new ZG2JobLog(jobId);
            if (string.IsNullOrEmpty(data)) return log;

            var parts = data.Split('|');
            if (parts.Length < 6) return log;

            int.TryParse(parts[0], NumberStyles.Integer, IC, out log.Failed);
            float.TryParse(parts[1], NumberStyles.Float, IC, out log.AppliedPenaltyPercent);
            float.TryParse(parts[2], NumberStyles.Float, IC, out log.DeductedAmount);
            float.TryParse(parts[3], NumberStyles.Float, IC, out log.PenaltyPerCheckpoint);
            float.TryParse(parts[4], NumberStyles.Float, IC, out log.MaxPenalty);

            if (parts[5].Length > 0)
            {
                foreach (var entry in parts[5].Split(';'))
                {
                    var f = entry.Split(',');
                    if (f.Length < 8) continue;
                    var cp = new ZG2Checkpoint();
                    int.TryParse(f[0], out int type);
                    cp.Type = (ZG2CheckpointType)type;
                    cp.Station = f[1];
                    cp.Ok = f[2] == "1";
                    int.TryParse(f[3], out int reason);
                    cp.Reason = (ZG2Fail)reason;
                    int.TryParse(f[4], out cp.Valid);
                    int.TryParse(f[5], out cp.Required);
                    float.TryParse(f[6], NumberStyles.Float, IC, out cp.Value);
                    cp.EndCarId = f[7];
                    log.Entries.Add(cp);
                }
            }
            // Older versions also counted failed arrivals: recount while the job is not validated yet.
            if (log.AppliedPenaltyPercent < 0f && log.Entries.Count > 0 && log.Entries.Count < MAX_ENTRIES)
            {
                int failed = 0;
                foreach (var e in log.Entries)
                    if (!e.Ok && e.Type == ZG2CheckpointType.Departure) failed++;
                log.Failed = failed;
            }

            return log;
        }

        private static string Clean(string s)
            => string.IsNullOrEmpty(s) ? "?" : s.Replace(",", "").Replace(";", "").Replace("|", "");

        #endregion

        #region Display

        public static string DescribeType(ZG2CheckpointType t)
            => t == ZG2CheckpointType.Departure ? ZG2Loc.T("Ausfahrt", "Departure") : ZG2Loc.T("Einfahrt", "Arrival");

        public static string DescribeReason(ZG2Checkpoint e)
        {
            switch (e.Reason)
            {
                case ZG2Fail.None: return "";
                case ZG2Fail.NoTrainEnd: return ZG2Loc.T("Zugschluss nicht ermittelbar", "train end not found");
                case ZG2Fail.NoBoards: return ZG2Loc.T($"keine Zg2 an {e.EndCarId}", $"no Zg2 on {e.EndCarId}");
                case ZG2Fail.TooFew: return ZG2Loc.T($"nur {e.Valid}/{e.Required} Zg2", $"only {e.Valid}/{e.Required} Zg2");
                case ZG2Fail.TooMany: return ZG2Loc.T($"{e.Valid} statt {e.Required} Zg2", $"{e.Valid} instead of {e.Required} Zg2");
                case ZG2Fail.WrongPosition:
                    return e.Value > 0f
                        ? ZG2Loc.T($"zu weit von Kupplung ({e.Value:0.0} m)", $"too far from coupler ({e.Value:0.0} m)")
                        : ZG2Loc.T("nicht an der Kupplung", "not at the coupler");
                case ZG2Fail.LocoNoRedLights: return ZG2Loc.T($"Lok {e.EndCarId} ohne rote Schlusslichter", $"loco {e.EndCarId} without red tail lights");
                case ZG2Fail.WrongDirection: return ZG2Loc.T($"falsche Richtung ({e.Value:0}°)", $"wrong direction ({e.Value:0}°)");
                default: return "?";
            }
        }

        #endregion
    }
}
