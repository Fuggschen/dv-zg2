using System;
using DV.ServicePenalty;
using DV.ThingTypes;
using DV.Utils;

namespace ZG2
{
    /// <summary>
    /// A custom <see cref="DisplayableDebt"/> that represents a flat-amount
    /// ZG2 fine for completing a job without a Zugschlusstafel.
    ///
    /// This integrates with the game's career terminal fee system so the
    /// player sees the fine and can pay it off like any other fee.
    /// </summary>
    public class ZG2FineDebt : DisplayableDebt
    {
        private readonly string jobId;
        private readonly float fineAmount;

        public override string ID => $"ZG2: {jobId}";

        public override bool IsStaged => true;

        public override bool IsPayable => true;

        public override bool IsTaxable => false;

        /// <param name="jobId">The completed job's ID (used for display).</param>
        /// <param name="fineAmount">The flat fine amount in credits.</param>
        public ZG2FineDebt(string jobId, float fineAmount)
        {
            this.jobId = jobId;
            this.fineAmount = fineAmount;
            ActivationTimeToCurrentTime();
        }

        public override DebtType GetDebtType()
        {
            // Use StagedJob so the booklet system uses the job fee template.
            return DebtType.StagedJob;
        }

        public override float GetTotalPrice()
        {
            return fineAmount;
        }

        public override CarDebtData[] GetCarDebts()
        {
            // No per-car damage breakdown — this is a flat regulatory fine.
            return Array.Empty<CarDebtData>();
        }

        public override void Pay()
        {
            base.Pay();
            SingletonBehaviour<CareerManagerDebtController>.Instance.UnregisterDebt(this);
            ZG2PenaltyHandler.OnFineDebtPaid(this);
        }
    }
}
