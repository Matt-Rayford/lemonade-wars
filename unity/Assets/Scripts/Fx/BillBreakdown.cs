using System.Collections.Generic;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// The fewest paper bills that make an amount, largest first. The game's
    /// denominations ($10, $5, $2, $1) are a canonical set, so greedy is exact.
    /// </summary>
    public static class BillBreakdown
    {
        private static readonly int[] Denominations = { 10, 5, 2, 1 };

        public static IReadOnlyList<int> Bills(int amount)
        {
            var bills = new List<int>();
            int left = amount;
            foreach (int denomination in Denominations)
            {
                while (left >= denomination)
                {
                    bills.Add(denomination);
                    left -= denomination;
                }
            }
            return bills;
        }
    }
}
