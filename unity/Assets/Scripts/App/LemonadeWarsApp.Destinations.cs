using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>Choosing which of YOUR stands a card lands on or targets, on the real board.</summary>
    public sealed partial class LemonadeWarsApp
    {
        /// <summary>Shelf purchases and own-stand picks land on YOUR board: snap the view home from a rival's.</summary>
        private void ShowOwnBoard()
        {
            if (_table.ViewedBoardPlayer >= 0)
            {
                _table.ViewedBoardPlayer = -1;
                _renderedRevision = -1;
                RenderIfChanged();
            }
        }

        /// <summary>Finish a play whose variants differ only by equip destination: one candidate submits;
        /// several aim at your board, with a replace prompt when the chosen slot is full.</summary>
        private void ResolveEquipDestination(List<GameAction> candidates, Texture2D texture) =>
            ResolveEquipDestination(candidates, texture,
                m => ((PlayLemonCard)m).EquipStandInstanceId);

        private void ResolveEquipDestination(List<GameAction> candidates, Texture2D texture,
            System.Func<GameAction, int?> destinationOf, System.Action onCancel = null)
        {
            if (candidates.Count == 1)
            {
                Submit(candidates[0]);
                return;
            }
            var byDestination = candidates
                .GroupBy(destinationOf)
                .ToDictionary(g => g.Key, g => g.ToList());
            System.Action<int?> pickDestination = destinationId =>
            {
                var atDestination = byDestination[destinationId];
                if (atDestination.Count == 1)
                {
                    Submit(atDestination[0]);
                }
                else
                {
                    _prompt.Show("That slot is full — replace which card?",
                        new[] { texture }, ToOptions(atDestination), showCancel: true);
                }
            };
            if (byDestination.Count == 1)
            {
                pickDestination(byDestination.Keys.First());
                return;
            }
            // The card rides the cursor over your own board until a click on a lit stand
            // places it (letting go drops the play).
            ShowOwnBoard();
            _table.BeginStickyPlace(texture, new HashSet<int?>(byDestination.Keys), pickDestination,
                onCancel ?? (() => { }));
        }

        /// <summary>Choose one of YOUR stands by clicking it on the real board (Word of Mouth, Doorbuster
        /// Sale, Rebrand). A stand with several variants (Rebrand's new types) gets a follow-up page.</summary>
        private void BeginOwnStandPick(Texture2D art, string caption,
            Dictionary<int?, List<GameAction>> byStand, System.Action onCancel)
        {
            void PickStand(int? standId)
            {
                var atStand = byStand[standId];
                if (atStand.Count == 1)
                {
                    Submit(atStand[0]);
                }
                else
                {
                    _prompt.Show(caption,
                        art != null ? new[] { art } : new Texture2D[0],
                        ToOptions(atStand), showCancel: true);
                }
            }
            if (byStand.Count == 1 && byStand.Values.First().Count == 1)
            {
                // One stand, one way: a board pick would be a dead click.
                Submit(byStand.Values.First()[0]);
                return;
            }
            // Arm first, THEN snap home: the retained aim rides through the synchronous
            // render and keeps MaybeShowModal stood down during it.
            _table.BeginEquipTargeting(art, new HashSet<int?>(byStand.Keys),
                PickStand, onCancel, caption);
            ShowOwnBoard();
        }
    }
}
