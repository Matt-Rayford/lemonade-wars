using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Rummage Sale, played on the table: the card stages itself beside your bar as
    /// if played, your tucked Black Market cards become pickable, each pick slaps onto
    /// the card's pile (click a piled card to send it back), and the message strip
    /// totals the sale until you press Sell. The engine play only happens then.
    /// </summary>
    public sealed partial class LemonadeWarsApp
    {
        private const string RummageSaleDefId = "rummage-sale";
        private const int RummageSaleMaxCards = 3;

        private sealed class RummageSaleFlow
        {
            public int CardInstanceId;
            public readonly List<int> Picked = new List<int>();
        }

        private RummageSaleFlow _rummage;
        /// <summary>A play the viewer staged on the table BEFORE submitting it: the
        /// LemonCardPlayed that follows must not stage it a second time.</summary>
        private string _preStagedDefId;

        private bool IsRummageSale(PlayLemonCard play) =>
            View.Hand.FirstOrDefault(c => c.InstanceId == play.CardInstanceId)?.DefId == RummageSaleDefId;

        private void BeginRummageSale(int cardInstanceId)
        {
            var me = View.Players[View.ViewerId];
            var sellable = new HashSet<int>(me.TurfEquipped.Select(c => c.InstanceId)
                .Concat(me.Stands.SelectMany(s => s.Equipped).Select(c => c.InstanceId)));
            if (sellable.Count == 0)
            {
                return;
            }
            _prompt.Hide();
            ShowOwnBoard();
            _rummage = new RummageSaleFlow { CardInstanceId = cardInstanceId };
            _stagedAttacker = _session.Seat;
            _playTheatre.Begin(_art.Lemon(RummageSaleDefId), RummageSaleDefId, _session.Seat, -1);
            _playTheatre.OnPileCardClicked = ToggleRummagePick;
            _table.OnRummagePick = ToggleRummagePick;
            _table.OnRummageCancel = CancelRummageSale;
            _table.RummagePointerCovered = _playTheatre.PointerOverPile;
            _table.BeginRummagePick(sellable);
            _table.StagedHandCardId = cardInstanceId; // it is on the table now, not in the fan
            _renderedRevision = -1;
        }

        private void ToggleRummagePick(int instanceId)
        {
            if (_rummage == null)
            {
                return;
            }
            if (_rummage.Picked.Remove(instanceId))
            {
                _playTheatre.ReturnPileCard(instanceId, _table.EquipStripWorld(instanceId));
            }
            else if (_rummage.Picked.Count < RummageSaleMaxCards)
            {
                var from = _table.EquipStripWorld(instanceId);
                if (from == null || !_playTheatre.AddPileCard(EquippedArt(instanceId), instanceId, from.Value))
                {
                    return;
                }
                _rummage.Picked.Add(instanceId);
            }
            else
            {
                return;
            }
            Sfx.Play(Sfx.ButtonClick);
            _table.SetRummagePicked(_rummage.Picked);
            _renderedRevision = -1;
        }

        private void ConfirmRummageSale()
        {
            if (_rummage == null || _rummage.Picked.Count == 0)
            {
                return;
            }
            var play = new PlayLemonCard
            {
                CardInstanceId = _rummage.CardInstanceId,
                SelectedInstanceIds = _rummage.Picked.ToList(),
            };
            _preStagedDefId = RummageSaleDefId; // the staged card is already on the table
            _table.EndRummagePick();
            _table.StagedHandCardId = null;
            _rummage = null;
            Submit(play);
        }

        private void CancelRummageSale()
        {
            if (_rummage == null)
            {
                return;
            }
            _playTheatre.ReturnAllPileCards(id => _table.EquipStripWorld(id));
            _playTheatre.Cancel();
            _table.EndRummagePick();
            _table.StagedHandCardId = null;
            _rummage = null;
            _renderedRevision = -1;
        }

        /// <summary>The sale cannot outlive its turn, its card or a modal that lands on top of it.</summary>
        private void DropStaleRummageSale(MoveGroups groups)
        {
            if (_rummage != null &&
                (groups == null || groups.IsModal || !groups.HandMoves.ContainsKey(_rummage.CardInstanceId)))
            {
                CancelRummageSale();
            }
        }

        private int RummageSaleTotal() =>
            _rummage.Picked.Sum(id => (_db.BlackMarket(EquippedInfoOf(id).DefId).Cost + 1) / 2);

        /// <summary>"Sell up to 3 cards" until something is picked; then the Sell button and the way out.</summary>
        private void RenderRummageStrip()
        {
            _tableMessage.text = "";
            int count = _rummage.Picked.Count;
            if (count == 0)
            {
                var lead = UiKit.CreateText(_tableActionHost, $"Sell up to {RummageSaleMaxCards} cards",
                    Mathf.RoundToInt(_tableMessage.fontSize), TextAnchor.MiddleCenter, UiKit.ButtonColor);
                lead.raycastTarget = false;
                UiKit.AddTextShadow(lead);
            }
            else
            {
                string cards = count == 1 ? "card" : "cards";
                var sell = UiKit.CreateButton(_tableActionHost, $"Sell {count} {cards} for ${RummageSaleTotal()}",
                    Mathf.RoundToInt(_tableMessage.fontSize), ConfirmRummageSale, highlighted: true);
                var sellLayout = sell.GetComponent<LayoutElement>();
                sellLayout.minWidth = 300f;
                sellLayout.minHeight = EndTurnButtonHeightPx;
            }
            var cancel = UiKit.CreateButton(_tableActionHost, "Cancel",
                Mathf.RoundToInt(_tableMessage.fontSize), CancelRummageSale, dark: true);
            var cancelLayout = cancel.GetComponent<LayoutElement>();
            cancelLayout.minWidth = 150f;
            cancelLayout.minHeight = EndTurnButtonHeightPx;
        }
    }
}
