using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>Shared lookups over the view and the catalogs: card/roll display names,
    /// art for a move, and where an equipped instance lives.</summary>
    public sealed partial class LemonadeWarsApp
    {
        private string LemonName(string defId)
        {
            try
            {
                return _db.Lemon(defId).Name;
            }
            catch
            {
                return TitleCased(defId);
            }
        }

        private string BlackMarketName(string defId)
        {
            try
            {
                return _db.BlackMarket(defId).Name;
            }
            catch
            {
                return TitleCased(defId);
            }
        }

        /// <summary>
        /// A card name when the catalog isn't known up front: PlayCancelled carries a
        /// Black Market id when a PURCHASE is tantrummed and a Lemon id otherwise.
        /// </summary>
        private string CardName(string defId)
        {
            try
            {
                return _db.Lemon(defId).Name;
            }
            catch
            {
                return BlackMarketName(defId);
            }
        }

        /// <summary>Last-resort display for an id with no catalog entry: "early-worm" -> "Early Worm".</summary>
        private static string TitleCased(string defId)
        {
            var words = defId.Replace("-", " ").Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                {
                    words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
                }
            }
            return string.Join(" ", words);
        }

        private string StandName(int standInstanceId)
        {
            foreach (var panel in View.Players)
            {
                foreach (var stand in panel.Stands)
                {
                    if (stand.InstanceId == standInstanceId)
                    {
                        return _db.StandType(stand.StandTypeId).Name;
                    }
                }
            }
            return "Stand";
        }

        /// <summary>
        /// What a die is being thrown for, in plain words — null for the ordinary end-of-turn
        /// sale, which needs no label. Callers append it only when there is something to say.
        /// </summary>
        private static string RollPurposeName(RollPurpose? purpose)
        {
            switch (purpose)
            {
                case RollPurpose.NightShifts:
                    return "Night Shifts";
                case RollPurpose.SpoiledRotten:
                    return "Spoiled Rotten";
                case RollPurpose.ExtraSale:
                    return "Extra sale";
                case RollPurpose.TradeWinds:
                    return "Trade Winds";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Like <see cref="RollPurposeName"/>, but names the one stand a stand-scoped
        /// roll pays out — a bare "Trade Winds roll" reads as if every stand sells.
        /// </summary>
        private string RollScopeName(RollPurpose? purpose, int? standInstanceId) =>
            purpose == RollPurpose.TradeWinds && standInstanceId is int id
                ? $"Trade Winds — {StandName(id)} only"
                : RollPurposeName(purpose);

        private List<Prompt.Option> ToOptions(IEnumerable<GameAction> moves)
        {
            return moves
                .Select(m => new Prompt.Option(_session.LabelFor(m), () => Submit(m), ArtForMove(m)))
                .ToList();
        }

        /// <summary>
        /// The card an option would play, so response buttons ("Respond with Tantrum")
        /// can show what the card actually does. Null for card-less moves (Pass, buys).
        /// </summary>
        private Texture2D ArtForMove(GameAction move)
        {
            switch (move)
            {
                case RespondToWindow respond when respond.EquippedInstanceId is int eq:
                    return EquippedArt(eq);
                case RespondToWindow respond:
                    return HandArt(respond.CardInstanceId);
                case UseTurnAbility ability:
                    return EquippedArt(ability.EquippedInstanceId);
                case PlayLemonCard play when play.EquipReplaceInstanceId is int replaced:
                    // Replace variants differ only by which of YOUR cards gets
                    // trashed: show that card, not the (identical) incoming one.
                    return EquippedArt(replaced);
                case BuyBlackMarket buy when buy.ReplaceInstanceId is int replaced:
                    return EquippedArt(replaced);
                case PlayLemonCard play when play.TargetEquippedInstanceId is int loot:
                    // Steal variants: show the LOOT, not the (identical) attack card.
                    return EquippedArt(loot);
                case PlayLemonCard play:
                    return HandArt(play.CardInstanceId);
                case SubmitRetarget retarget when retarget.TargetEquippedInstanceId is int newLoot:
                    return EquippedArt(newLoot);
                case SubmitAbilityChoice choice when choice.EquippedInstanceId is int copy:
                    return EquippedArt(copy);
                default:
                    return null;
            }
        }

        private Texture2D HandArt(int instanceId)
        {
            var card = View.Hand.FirstOrDefault(c => c.InstanceId == instanceId);
            return card == null ? null : _art.Lemon(card.DefId);
        }

        private Texture2D EquippedArt(int instanceId)
        {
            var info = EquippedInfoOf(instanceId);
            return info == null ? null : _art.BlackMarket(info.DefId, info.Shape ?? Shape.Square);
        }

        private PlayerView.CardInfo EquippedInfoOf(int instanceId)
        {
            foreach (var panel in View.Players)
            {
                foreach (var card in panel.TurfEquipped)
                {
                    if (card.InstanceId == instanceId)
                    {
                        return card;
                    }
                }
                foreach (var stand in panel.Stands)
                {
                    foreach (var card in stand.Equipped)
                    {
                        if (card.InstanceId == instanceId)
                        {
                            return card;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>The seat whose turf/stand carries this equipped instance.</summary>
        private int FindEquipOwner(int equippedInstanceId)
        {
            foreach (var player in View.Players)
            {
                if (player.TurfEquipped.Any(c => c.InstanceId == equippedInstanceId) ||
                    player.Stands.Any(st => st.Equipped.Any(c => c.InstanceId == equippedInstanceId)))
                {
                    return player.PlayerId;
                }
            }
            return -1;
        }
    }
}
