using System.Collections.Generic;
using UnityEngine;

namespace LemonadeWars.Unity
{
    public sealed partial class EffectsPlayer
    {
        /// <summary>The market cell's own size: the chip IS the card leaving or arriving.</summary>
        private const float ShelfCardWidth = 154f;
        private const float ShelfCardHeight = 216f;
        private const float SweepSeconds = 0.175f;
        /// <summary>Swept one at a time: the next card leaves as the last one slips under the deck.</summary>
        private const float SweepStaggerSeconds = SweepSeconds;
        /// <summary>A swept card fades through the last of its slide, as if passing under the deck.</summary>
        private const float SweepFadeOutFrom = 0.55f;
        private const float DealSeconds = 0.2f;
        /// <summary>A dealt copy outlives its landing by a few frames, until the real card has been drawn under it.</summary>
        private const float DealLingerSeconds = 0.06f;
        /// <summary>Dealt one at a time: the next card leaves the deck as the last one lands.</summary>
        private const float DealStaggerSeconds = DealSeconds;
        /// <summary>After the refresh dollar lands, this long before the shelf starts moving.</summary>
        private const float RefreshMoneyBeatSeconds = 0.25f;

        private void StartMarketDeal(MarketDealFx deal)
        {
            var unlanded = new List<ShelfCardFlight>(deal.Deals);
            void Land(ShelfCardFlight card)
            {
                if (unlanded.Remove(card))
                {
                    card.OnLanded?.Invoke();
                }
            }
            void LandAll()
            {
                foreach (var card in unlanded.ToArray())
                {
                    Land(card);
                }
            }

            var deckWorld = deal.DeckWorld?.Invoke();
            if (deckWorld == null)
            {
                LandAll(); // no shelf on screen: nothing to animate
                return;
            }
            Vector2 deck = _layer.InverseTransformPoint(deckWorld.Value);
            var chips = new List<RectTransform>();

            var refreshFan = BuildRefreshPaymentPile(deal);
            float clockSeconds = refreshFan != null ? BillFan.ArriveSeconds + RefreshMoneyBeatSeconds : 0f;
            clockSeconds += SweepShelfCardsUnderDeck(deal.Sweep, deck, clockSeconds, chips);
            deal.OnStarted?.Invoke(); // the sliding copies are up: the shelf may drop its stand-ins
            clockSeconds += DealShelfCardsFromDeck(deal.Deals, deck, clockSeconds, chips, Land);

            _busyUntil = Time.time + clockSeconds + 0.1f;
            _endActive = () =>
            {
                foreach (var chip in chips)
                {
                    if (chip != null)
                    {
                        Object.Destroy(chip.gameObject); // skip: nothing left mid-air
                    }
                }
                if (refreshFan != null)
                {
                    Object.Destroy(refreshFan.gameObject);
                }
                LandAll(); // a skip still lands: a hidden slot must never stay hidden
            };
        }

        /// <summary>The refresher's dollar, slapped down before the shelf moves. Null for an unpaid deal.</summary>
        private BillFan BuildRefreshPaymentPile(MarketDealFx deal)
        {
            if (deal.PayerId < 0 || deal.Price <= 0)
            {
                return null;
            }
            var fan = BuildPurchasePile(deal.PayerId, deal.Price, deal.BillArt, ShowcaseSpot(deal.PayerId));
            if (fan != null)
            {
                fan.AutoFadeAfterSeconds = OwnMoneyHoldSeconds;
            }
            return fan;
        }

        /// <returns>Seconds the sweep takes, from <paramref name="startSeconds"/>.</returns>
        private float SweepShelfCardsUnderDeck(List<ShelfCardFlight> sweep, Vector2 deck, float startSeconds,
            List<RectTransform> chips)
        {
            int swept = 0;
            foreach (var card in sweep)
            {
                var art = card.Art?.Invoke();
                var slot = card.SlotWorld?.Invoke();
                if (art == null || slot == null)
                {
                    continue;
                }
                var chip = BuildShelfChip(art);
                chips.Add(chip);
                var motion = chip.gameObject.AddComponent<FlightMotion>();
                motion.From = _layer.InverseTransformPoint(slot.Value);
                motion.To = deck;
                motion.Delay = startSeconds + swept * SweepStaggerSeconds;
                motion.Duration = SweepSeconds;
                motion.ArcHeight = 0f;
                motion.VisibleWhileWaiting = true;
                motion.FadeOutFrom = SweepFadeOutFrom;
                swept++;
            }
            return swept > 0 ? SweepSeconds + SweepStaggerSeconds * (swept - 1) : 0f;
        }

        /// <returns>Seconds the deal takes, from <paramref name="startSeconds"/>.</returns>
        private float DealShelfCardsFromDeck(List<ShelfCardFlight> deals, Vector2 deck, float startSeconds,
            List<RectTransform> chips, System.Action<ShelfCardFlight> land)
        {
            int dealt = 0;
            foreach (var card in deals)
            {
                var art = card.Art?.Invoke();
                var slot = card.SlotWorld?.Invoke();
                if (art == null || slot == null)
                {
                    land(card); // nothing to fly: the slot just shows what it has
                    continue;
                }
                var chip = BuildShelfChip(art);
                chips.Add(chip);
                var motion = chip.gameObject.AddComponent<FlightMotion>();
                motion.From = deck;
                motion.To = _layer.InverseTransformPoint(slot.Value);
                motion.Delay = startSeconds + dealt * DealStaggerSeconds;
                motion.Duration = DealSeconds;
                motion.ArcHeight = 0f;
                motion.LingerAfterArrivalSeconds = DealLingerSeconds;
                var landing = card;
                motion.OnArrive = () => land(landing);
                dealt++;
            }
            return dealt > 0 ? DealSeconds + DealStaggerSeconds * (dealt - 1) : 0f;
        }

        private RectTransform BuildShelfChip(Texture2D art) =>
            BuildFlightChip(art, ShelfCardWidth, ShelfCardHeight);
    }
}
