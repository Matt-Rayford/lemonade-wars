using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Selling assets off your own board, on the table. A card stages itself beside your
    /// bar (the Rummage Sale being played, or the Timeout being paid for); your tucked
    /// Black Market cards, and stands when a fine allows, become pickable; each pick joins
    /// the staged card's pile; the strip totals the sale. Nothing reaches the engine until Sell.
    /// </summary>
    public sealed partial class LemonadeWarsApp
    {
        private const string RummageSaleDefId = "rummage-sale";
        private const string TimeoutDefId = "timeout";
        private const int RummageSaleMaxCards = 3;

        private sealed class AssetSaleFlow
        {
            /// <summary>The card staged beside the bar; also what the theatre matches on.</summary>
            public string StagedDefId;
            /// <summary>Rummage Sale: the hand card that left the fan to be staged.</summary>
            public int? HandCardId;
            public int MaxPicks;
            /// <summary>A Timeout fine: cash plus the sale must reach this before Sell is offered.</summary>
            public int FineOwed;
            public bool Cancelable;
            public readonly List<int> Picked = new List<int>();
            public System.Func<int, int> PriceOf;
            public System.Action<List<int>> Confirm;

            public bool IsTimeoutFine => StagedDefId == TimeoutDefId;
        }

        private AssetSaleFlow _sale;
        /// <summary>Staged on the table BEFORE its play was submitted: the LemonCardPlayed
        /// that follows must not stage it a second time.</summary>
        private string _preStagedDefId;

        private bool IsRummageSale(PlayLemonCard play) =>
            View.Hand.FirstOrDefault(c => c.InstanceId == play.CardInstanceId)?.DefId == RummageSaleDefId;

        private bool TimeoutSaleActive => _sale != null && _sale.IsTimeoutFine;

        private void BeginRummageSale(int cardInstanceId)
        {
            var me = View.Players[View.ViewerId];
            var sellable = MyEquippedIds(me);
            if (sellable.Count == 0)
            {
                return;
            }
            _prompt.Hide();
            BeginAssetSale(new AssetSaleFlow
            {
                StagedDefId = RummageSaleDefId,
                HandCardId = cardInstanceId,
                MaxPicks = RummageSaleMaxCards,
                Cancelable = true,
                PriceOf = key => (_db.BlackMarket(EquippedInfoOf(key).DefId).Cost + 1) / 2,
                Confirm = picked =>
                {
                    _preStagedDefId = RummageSaleDefId; // the staged card is already on the table
                    Submit(new PlayLemonCard
                    {
                        CardInstanceId = cardInstanceId,
                        SelectedInstanceIds = picked.ToList(),
                    });
                },
            }, sellable, null);
            _table.StagedHandCardId = cardInstanceId; // it is on the table now, not in the fan
        }

        /// <summary>The Timeout fine exceeds your cash and you own something to sell for it.</summary>
        private bool TimeoutNeedsSale()
        {
            var me = View.Players[View.ViewerId];
            return View.TimeoutFinePlayerId == View.ViewerId && me.Money < View.TimeoutFineAmount &&
                   (MyEquippedIds(me).Count > 0 || me.Stands.Count > 0);
        }

        /// <summary>Full base price back for each card or stand sold to cover the fine; the engine prices the same way.</summary>
        private void BeginTimeoutSale()
        {
            var me = View.Players[View.ViewerId];
            BeginAssetSale(new AssetSaleFlow
            {
                StagedDefId = TimeoutDefId,
                MaxPicks = int.MaxValue,
                FineOwed = View.TimeoutFineAmount,
                Cancelable = false,
                PriceOf = key => TableView.IsStandKey(key)
                    ? _db.StandType(StandOf(key - TableView.StandKeyOffset).StandTypeId).BaseCost
                    : _db.BlackMarket(EquippedInfoOf(key).DefId).Cost,
                Confirm = picked =>
                {
                    _playTheatre.Cancel(); // the fine is paid: the sale clears off the table
                    Submit(new SubmitTimeoutPayment
                    {
                        SellBmInstanceIds = picked.Where(k => !TableView.IsStandKey(k)).ToList(),
                        SellStandInstanceIds = picked.Where(TableView.IsStandKey)
                            .Select(k => k - TableView.StandKeyOffset).ToList(),
                    });
                },
            }, MyEquippedIds(me), new HashSet<int>(me.Stands.Select(s => s.InstanceId)));
        }

        private static HashSet<int> MyEquippedIds(PlayerView.PlayerPanel me) =>
            new HashSet<int>(me.TurfEquipped.Select(c => c.InstanceId)
                .Concat(me.Stands.SelectMany(s => s.Equipped).Select(c => c.InstanceId)));

        private PlayerView.StandPanel StandOf(int standInstanceId) =>
            View.Players[View.ViewerId].Stands.First(s => s.InstanceId == standInstanceId);

        private void BeginAssetSale(AssetSaleFlow flow, ISet<int> equippedIds, ISet<int> standIds)
        {
            _sale = flow; // must precede ShowOwnBoard(): its render has to find the sale already on
            ShowOwnBoard();
            _stagedAttacker = _session.Seat;
            _playTheatre.Begin(_art.Lemon(flow.StagedDefId), flow.StagedDefId, _session.Seat, -1);
            _playTheatre.OnPileCardClicked = ToggleSalePick;
            _table.OnRummagePick = ToggleSalePick;
            _table.OnRummageCancel = CancelAssetSale;
            _table.RummagePointerCovered = _playTheatre.PointerOverPile;
            _table.BeginRummagePick(equippedIds, standIds);
            _renderedRevision = -1;
        }

        private void ToggleSalePick(int key)
        {
            if (_sale == null)
            {
                return;
            }
            if (_sale.Picked.Remove(key))
            {
                _playTheatre.ReturnPileCard(key, _table.RummagePickWorld(key));
            }
            else if (_sale.Picked.Count < _sale.MaxPicks)
            {
                var from = _table.RummagePickWorld(key);
                if (from == null || !_playTheatre.AddPileCard(SaleArt(key), key, from.Value))
                {
                    return;
                }
                _sale.Picked.Add(key);
            }
            else
            {
                return;
            }
            Sfx.Play(Sfx.ButtonClick);
            _table.SetRummagePicked(_sale.Picked);
            _renderedRevision = -1;
        }

        private Texture2D SaleArt(int key)
        {
            if (!TableView.IsStandKey(key))
            {
                return EquippedArt(key);
            }
            var stand = StandOf(key - TableView.StandKeyOffset);
            return _art.Stand(stand.StandTypeId, stand.Shape);
        }

        private int SaleTotal() => _sale.Picked.Sum(_sale.PriceOf);

        /// <summary>A Timeout sale must reach the fine with cash; a Rummage Sale just needs a pick.</summary>
        private bool SaleCoversItsPurpose() =>
            _sale.Picked.Count > 0 &&
            View.Players[View.ViewerId].Money + SaleTotal() >= _sale.FineOwed;

        private void ConfirmAssetSale()
        {
            if (_sale == null || !SaleCoversItsPurpose())
            {
                return;
            }
            var sale = _sale;
            EndAssetSale();
            sale.Confirm(sale.Picked);
        }

        private void CancelAssetSale()
        {
            if (_sale == null || !_sale.Cancelable)
            {
                return;
            }
            DropAssetSale();
        }

        /// <summary>The sale is over without a play: the pile flies home and the staged card fades.</summary>
        private void DropAssetSale()
        {
            _playTheatre.ReturnAllPileCards(key => _table.RummagePickWorld(key));
            _playTheatre.Cancel();
            EndAssetSale();
            _renderedRevision = -1;
        }

        private void EndAssetSale()
        {
            _table.EndRummagePick();
            _table.StagedHandCardId = null;
            _sale = null;
        }

        /// <summary>A sale cannot outlive its turn, its card, its fine, or a modal that lands on top of it.</summary>
        private void DropStaleAssetSale(MoveGroups groups)
        {
            if (_sale == null)
            {
                return;
            }
            bool stale = _sale.IsTimeoutFine
                ? !TimeoutFineDue()
                : groups == null || groups.IsModal ||
                  !groups.HandMoves.ContainsKey(_sale.HandCardId ?? -1);
            if (stale)
            {
                DropAssetSale();
            }
        }

        /// <summary>An instruction until something is picked, then the Sell button with count and
        /// total (held dark while a fine is still short), plus Cancel when the sale has a way out.</summary>
        private void RenderSaleStrip()
        {
            _tableMessage.text = "";
            int fontSize = Mathf.RoundToInt(_tableMessage.fontSize);
            int count = _sale.Picked.Count;
            int money = View.Players[View.ViewerId].Money;
            if (count == 0)
            {
                string lead = _sale.IsTimeoutFine
                    ? $"Timeout! Sell assets to cover the ${_sale.FineOwed} fine (you have ${money})"
                    : $"Sell up to {RummageSaleMaxCards} cards";
                var text = UiKit.CreateText(_tableActionHost, lead, fontSize, TextAnchor.MiddleCenter, UiKit.ButtonColor);
                text.raycastTarget = false;
                UiKit.AddTextShadow(text);
            }
            else
            {
                string cards = count == 1 ? "card" : "cards";
                int total = SaleTotal();
                bool enough = SaleCoversItsPurpose();
                string label = enough
                    ? $"Sell {count} {cards} for ${total}"
                    : $"Sell {count} {cards} for ${total} — ${_sale.FineOwed - money - total} short";
                var sell = UiKit.CreateButton(_tableActionHost, label, fontSize,
                    enough ? ConfirmAssetSale : () => { }, dark: !enough, highlighted: enough);
                var sellLayout = sell.GetComponent<LayoutElement>();
                sellLayout.minWidth = 300f;
                sellLayout.minHeight = EndTurnButtonHeightPx;
            }
            if (_sale.Cancelable)
            {
                var cancel = UiKit.CreateButton(_tableActionHost, "Cancel", fontSize, CancelAssetSale, dark: true);
                var cancelLayout = cancel.GetComponent<LayoutElement>();
                cancelLayout.minWidth = 150f;
                cancelLayout.minHeight = EndTurnButtonHeightPx;
            }
        }
    }
}
