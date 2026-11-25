using System.Collections.Generic;
using UnityEngine;

namespace ZG2
{
    public enum ZG2Fail : byte
    {
        None = 0,
        NoTrainEnd = 1,
        NoBoards = 2,
        TooFew = 3,
        TooMany = 4,
        WrongPosition = 5,
        WrongDirection = 6,
        LocoNoRedLights = 7
    }

    public struct ZG2CheckResult
    {
        public bool Ok;
        public ZG2Fail Reason;
        public int Valid;
        public int Required;
        /// <summary>Extra info: distance to the coupler (WrongPosition) or worst angle (WrongDirection).</summary>
        public float Value;
        public string EndCarId;
    }

    /// <summary>
    /// Validates the Zg2 equipment at the actual end of a trainset:
    ///   • the end is the free coupler at the rear in direction of travel
    ///     (standing still: the end without locomotive; if unclear both ends are tried),
    ///   • boards count only on the car that owns that coupler,
    ///   • position: at most <see cref="ZG2Settings.MaxCouplerDistance"/> m in front of / behind the coupler
    ///     (measured along the car axis),
    ///   • direction: board face points away from the train within the allowed angle
    ///     (the gadget's Z+ points INTO the mounting surface, so the face is -Z),
    ///   • locomotive (or tender) at the train end: valid, or – strict mode – its red tail lights
    ///     on that side must be lit (Zg2 boards on the loco are accepted as well),
    ///   • exactly <see cref="ZG2Settings.RequiredBoards"/> valid boards.
    /// </summary>
    public static class ZG2TrainEndValidator
    {
        private const float MOVING_SPEED = 0.5f;      // m/s
        private const float MAX_LATERAL = 2.5f;       // m – boards further to the side are not "at the end"

        private static readonly List<ZG2BoardPose> poses = new List<ZG2BoardPose>(8);
        private static readonly List<Coupler> freeCouplers = new List<Coupler>(4);
        private static readonly List<Coupler> candidates = new List<Coupler>(2);

        public static ZG2CheckResult Check(Trainset trainset)
        {
            var result = new ZG2CheckResult { Reason = ZG2Fail.NoTrainEnd, Required = Main.Settings.RequiredBoards };
            if (trainset == null || trainset.cars == null || trainset.cars.Count == 0) return result;

            CollectFreeCouplers(trainset);
            if (freeCouplers.Count == 0) return result;

            SelectRearCandidates(trainset);

            bool first = true;
            foreach (var coupler in candidates)
            {
                var r = Evaluate(coupler);
                if (first || IsBetter(r, result)) result = r;
                first = false;
                if (result.Ok) break;
            }

            return result;
        }

        private static bool IsBetter(ZG2CheckResult a, ZG2CheckResult b)
        {
            if (a.Ok != b.Ok) return a.Ok;
            return a.Valid > b.Valid;
        }

        private static void CollectFreeCouplers(Trainset trainset)
        {
            freeCouplers.Clear();
            AddFree(trainset.firstCar);
            if (trainset.lastCar != trainset.firstCar)
                AddFree(trainset.lastCar);
        }

        private static void AddFree(TrainCar car)
        {
            if (car == null) return;
            if (car.frontCoupler != null && !car.frontCoupler.IsCoupled()) freeCouplers.Add(car.frontCoupler);
            if (car.rearCoupler != null && !car.rearCoupler.IsCoupled()) freeCouplers.Add(car.rearCoupler);
        }

        private static void SelectRearCandidates(Trainset trainset)
        {
            candidates.Clear();
            if (freeCouplers.Count == 1)
            {
                candidates.Add(freeCouplers[0]);
                return;
            }

            // 1) Direction of travel
            var reference = trainset.firstCar;
            float speed = reference != null ? reference.GetForwardSpeed() : 0f;
            if (Mathf.Abs(speed) >= MOVING_SPEED)
            {
                Vector3 v = reference.transform.forward * speed;
                Coupler rear = null;
                float best = float.MaxValue;
                foreach (var c in freeCouplers)
                {
                    float d = Vector3.Dot(c.transform.position, v);
                    if (d < best) { best = d; rear = c; }
                }
                candidates.Add(rear);
                return;
            }

            // 2) Standing: the end that is not a locomotive
            Coupler nonLoco = null;
            int locoEnds = 0;
            foreach (var c in freeCouplers)
            {
                if (c.train != null && c.train.IsLoco) locoEnds++;
                else nonLoco = c;
            }
            if (locoEnds > 0 && nonLoco != null && locoEnds == freeCouplers.Count - 1)
            {
                candidates.Add(nonLoco);
                return;
            }

            // 3) Unclear: try both ends, the better one counts
            candidates.AddRange(freeCouplers);
        }

        public static bool IsLocoOrTender(TrainCar car)
        {
            if (car == null) return false;
            if (car.IsLoco) return true;
            string id = car.carLivery != null ? car.carLivery.id : null;
            return id != null && id.IndexOf("tender", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static ZG2CheckResult Evaluate(Coupler coupler)
        {
            var settings = Main.Settings;
            var car = coupler.train;
            var result = new ZG2CheckResult
            {
                Required = settings.RequiredBoards,
                EndCarId = car != null && car.logicCar != null ? car.logicCar.ID : "?"
            };

            if (car == null)
            {
                result.Reason = ZG2Fail.NoTrainEnd;
                return result;
            }

            Vector3 p = coupler.transform.position;
            Vector3 outward = car.transform.forward * (coupler.isFrontCoupler ? 1f : -1f);

            // Locomotive (or tender) at the train end, e.g. when pushing back
            if (IsLocoOrTender(car))
            {
                if (settings.LocoEndMode == 0)
                {
                    result.Ok = true;
                    result.Valid = result.Required;
                    return result;
                }

                if (ZG2LocoLights.HasRedLightLit(car, outward, out _))
                {
                    result.Ok = true;
                    result.Valid = result.Required;
                    return result;
                }
                // Lights off → Zg2 boards on the loco still count (evaluated below)
            }

            ZG2GadgetDetector.GetBoardPoses(car, poses);
            if (poses.Count == 0)
            {
                result.Reason = IsLocoOrTender(car) ? ZG2Fail.LocoNoRedLights : ZG2Fail.NoBoards;
                return result;
            }

            int inZone = 0, wrongDir = 0;
            float nearest = float.MaxValue, worstAngle = 0f;

            foreach (var pose in poses)
            {
                Vector3 rel = pose.Position - p;
                float along = Vector3.Dot(rel, outward);
                float lateral = (rel - outward * along).magnitude;

                if (lateral > MAX_LATERAL || Mathf.Abs(along) > settings.MaxCouplerDistance)
                {
                    if (lateral <= MAX_LATERAL) nearest = Mathf.Min(nearest, Mathf.Abs(along));
                    continue;
                }

                inZone++;
                // Gadget Z+ is the surface normal pointing into the car → the board face is -Z.
                float angle = Vector3.Angle(-pose.Forward, outward);
                if (angle <= settings.MaxBoardAngle) result.Valid++;
                else
                {
                    wrongDir++;
                    worstAngle = Mathf.Max(worstAngle, angle);
                }

                if (settings.DebugLogging)
                    Main.Log($"  Zg2 board on {result.EndCarId}: along={along:F2} m, lateral={lateral:F2} m, angle={angle:F0}°");
            }

            if (result.Valid == result.Required)
            {
                result.Ok = true;
                result.Reason = ZG2Fail.None;
            }
            else if (inZone == 0)
            {
                result.Reason = ZG2Fail.WrongPosition;
                result.Value = nearest == float.MaxValue ? 0f : nearest;
            }
            else if (result.Valid > result.Required)
            {
                result.Reason = ZG2Fail.TooMany;
            }
            else if (wrongDir > 0)
            {
                result.Reason = ZG2Fail.WrongDirection;
                result.Value = worstAngle;
            }
            else
            {
                result.Reason = ZG2Fail.TooFew;
            }

            return result;
        }
    }
}
