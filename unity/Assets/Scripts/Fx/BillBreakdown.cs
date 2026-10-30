using System.Collections.Generic;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// The bills a payment is made with, largest first. Not the fewest: real wallets
    /// hold change, so the pile size varies with the amount — one or two bills up to
    /// $2, one to three up to $10, one to four beyond — and among the ways to make the
    /// amount with that many bills, one is picked at random. The game's denominations
    /// are $10, $5, $2 and $1.
    /// </summary>
    public static class BillBreakdown
    {
        private static readonly int[] Denominations = { 10, 5, 2, 1 };

        public static IReadOnlyList<int> Bills(int amount)
        {
            if (amount <= 0)
            {
                return new int[0];
            }
            var counts = PreferredCounts(amount);
            Shuffle(counts);
            foreach (int count in counts)
            {
                var ways = WaysWithExactly(count, amount);
                if (ways.Count > 0)
                {
                    return ways[Random.Range(0, ways.Count)];
                }
            }
            return Fewest(amount);
        }

        private static List<int> PreferredCounts(int amount) =>
            amount <= 2 ? new List<int> { 1, 2 }
            : amount <= 10 ? new List<int> { 1, 2, 3 }
            : new List<int> { 1, 2, 3, 4 };

        /// <summary>Every non-increasing run of exactly <paramref name="count"/> bills summing to the amount.</summary>
        private static List<int[]> WaysWithExactly(int count, int amount)
        {
            var ways = new List<int[]>();
            Collect(new List<int>(), 0, count, amount, ways);
            return ways;
        }

        private static void Collect(List<int> chosen, int fromIndex, int count, int remaining, List<int[]> ways)
        {
            if (chosen.Count == count)
            {
                if (remaining == 0)
                {
                    ways.Add(chosen.ToArray());
                }
                return;
            }
            for (int i = fromIndex; i < Denominations.Length; i++)
            {
                int bill = Denominations[i];
                if (bill > remaining)
                {
                    continue;
                }
                chosen.Add(bill);
                Collect(chosen, i, count, remaining - bill, ways);
                chosen.RemoveAt(chosen.Count - 1);
            }
        }

        /// <summary>Greedy is exact for this canonical set; the fallback when no preferred count fits.</summary>
        private static List<int> Fewest(int amount)
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

        private static void Shuffle(List<int> items)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }
    }
}
