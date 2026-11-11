using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using UnityEngine;

namespace LemonadeWars.Unity
{
    public sealed partial class LemonadeWarsApp
    {
        /// <summary>
        /// The engine lists one free-play move per card x target. Collapsed to one option per CARD:
        /// picking it submits (one variant), arms table aiming (live table) or opens a target page.
        /// </summary>
        private List<Prompt.Option> GroupedPlayOptions(IEnumerable<GameAction> moves, bool tableLive)
        {
            var allMoves = moves.ToList();
            var options = new List<Prompt.Option>();
            var seenDefs = new HashSet<string>();
            foreach (var move in allMoves)
            {
                if (!(move is PlayLemonCard play))
                {
                    options.Add(new Prompt.Option(ModalOptionLabel(move),
                        () => Submit(move), ArtForMove(move)));
                    continue;
                }
                string defId = HandCardDefId(play.CardInstanceId);
                if (defId == null || !seenDefs.Add(defId))
                {
                    continue; // copies of one card are one choice
                }
                var variants = allMoves.OfType<PlayLemonCard>()
                    .Where(variant => HandCardDefId(variant.CardInstanceId) == defId)
                    .ToList();
                options.Add(PlayCardOption(play, defId, variants, tableLive));
            }
            return options;
        }

        private Prompt.Option PlayCardOption(PlayLemonCard play, string defId,
            List<PlayLemonCard> variants, bool tableLive)
        {
            var art = _art.Lemon(defId);
            if (variants.Count == 1)
            {
                var only = variants[0];
                return new Prompt.Option(_session.LabelFor(only), () => Submit(only), art);
            }
            if (tableLive)
            {
                int instanceId = play.CardInstanceId;
                return new Prompt.Option($"Play {LemonName(defId)}...",
                    () => _table.ActivateHandCard(instanceId), art);
            }
            string name = LemonName(defId);
            return new Prompt.Option($"Play {name}...",
                () => ShowVariantPage(name, art, variants), art);
        }

        private string HandCardDefId(int instanceId) =>
            View.Hand.FirstOrDefault(c => c.InstanceId == instanceId)?.DefId;

        /// <summary>
        /// LabelFor, with a theft window's numbers on the buttons: Profit Share says what it
        /// claws back, Pass says what it forfeits.
        /// </summary>
        private string ModalOptionLabel(GameAction move)
        {
            if (View.TheftOnMe && View.StackTop == null)
            {
                if (move is RespondToWindow respond && View.TheftRecoverable is int back &&
                    HandCardDefId(respond.CardInstanceId) == LemonCardIds.ProfitShare)
                {
                    return $"Play Profit Share — take ${back} back";
                }
                if (move is PassWindow && View.TheftAmount is int lost)
                {
                    string thief = View.TheftAttackerId is int robber ? NameOf(robber) : "the thief";
                    return $"Pass — {thief} keeps the ${lost}";
                }
            }
            return _session.LabelFor(move);
        }

        /// <summary>Second page of a blur-modal free play: this card's targets, with a way back to the card list.</summary>
        private void ShowVariantPage(string name, Texture2D art, List<PlayLemonCard> variants)
        {
            System.Action back = ReopenModalNextTick;
            // The hand menu's guided flows: Reduce and Reuse must reach the visual pile picker
            // whether it was played from hand or off a modal.
            var guided = GuidedOptions(name, art, variants.Cast<GameAction>(), back,
                out var direct);
            if (direct.Count == 0 && guided.Count == 1)
            {
                _prompt.Hide();
                guided[0].OnPick(); // a lone guided flow — skip the one-button page
                return;
            }
            var options = new List<Prompt.Option> { new Prompt.Option("Back", back) };
            options.AddRange(guided);
            options.AddRange(direct.Select(variant => new Prompt.Option(
                _session.LabelFor(variant),
                () => Submit(variant),
                variant is PlayLemonCard play && play.TargetEquippedInstanceId is int equippedId
                    ? EquippedArt(equippedId) : null)));
            _prompt.Show($"Play {name}", new[] { art }, options, showCancel: false);
        }
    }
}
