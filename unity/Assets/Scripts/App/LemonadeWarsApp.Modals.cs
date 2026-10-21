using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>Decision moments: which surface answers each engine decision (blur
    /// prompt, live reaction panel, card picker, board aiming) and the words and card
    /// art on it.</summary>
    public sealed partial class LemonadeWarsApp
    {
        // Revision whose forced play was already auto-submitted — online, the revision
        // does not advance until the server answers, and a second submit would be a bug.
        private int _autoForcedPlayRevision = -1;

        /// <summary>
        /// True while a stack moment needs the viewer AND the table should stay live:
        /// a response window, or a retarget of the viewer's own attack (both are
        /// decided by reading the other players' boards, which a blur modal hides).
        /// </summary>
        private bool ReactionWindowDue(MoveGroups groups)
        {
            if (groups == null || !groups.IsModal || groups.ModalMoves.Count == 0)
            {
                return false;
            }
            var decision = View.MyDecisions.FirstOrDefault();
            if (decision == null)
            {
                // A stack play to react to, or a settled die you may still tweak
                // (Out of Stock, Downsell, Sugared Up, Take Two).
                return (View.StackTop != null || View.PendingRollValue != null) &&
                       View.AwaitingResponse.Contains(View.ViewerId);
            }
            // Bouncer resolves AFTER its attack leaves the stack, so it has no stack
            // top — but picking who to hit back needs the table just as much.
            return decision.Kind == DecisionKind.AttackRetarget ||
                   decision.Kind == DecisionKind.BouncerAttack;
        }

        private void MaybeShowModal(MoveGroups groups, int revision)
        {
            if (groups == null || !groups.IsModal || groups.ModalMoves.Count == 0)
            {
                return;
            }
            // Online, harmless updates (a friend passing their window, bot pacing) bump
            // the revision while a modal is open. Rebuilding identical buttons under the
            // cursor eats the player's click — so if the same choice is already on
            // screen, leave it alone.
            string signature = ModalTitle() + "|" +
                string.Join("|", groups.ModalMoves.Select(m => _session.LabelFor(m)));
            bool surfaceOpen = _prompt.IsOpen || _picker.IsOpen || _reaction.IsOpen ||
                _table.DiscardPickerOpen || _table.EquipTargetingActive;
            if (surfaceOpen && signature == _modalSignature)
            {
                _modalRevision = revision;
                return;
            }
            if (_modalRevision == revision && surfaceOpen)
            {
                return;
            }
            _modalRevision = revision;
            _modalSignature = signature;
            if (_table.DiscardPickerOpen || _table.EquipTargetingActive)
            {
                // The choice this grid/aim was serving changed under it — drop the
                // stale surface (silently: its cancel callback would reopen it)
                // before the fresh modal replaces it.
                _table.DismissModalOverlays();
            }
            if (TryAutoForcedPlay(groups, revision))
            {
                return;
            }
            _autoModalOpen = true;
            if (TryShowPlayerPick(groups))
            {
                return;
            }
            if (TryShowStandPick(groups))
            {
                return;
            }
            if (TryShowPicker())
            {
                return;
            }
            bool IsPassMove(GameAction m) => m is SkipFreePlay || m is PassWindow;
            if (ReactionWindowDue(groups))
            {
                // A stack-top window: react from the log column with the table live.
                // The list is short here, so Pass sits at the BOTTOM — reactions read
                // first, "do nothing" is the fallback under them.
                var reactionMoves = groups.ModalMoves
                    .OrderBy(m => IsPassMove(m) ? 1 : 0)
                    .ToList();
                var kind = View.MyDecisions.FirstOrDefault()?.Kind;
                _prompt.Hide();
                _reactionBanner = ModalTitle();
                _reaction.Show(ModalCards(), GroupedPlayOptions(reactionMoves, tableLive: true),
                    kind == DecisionKind.AttackRetarget ? "PICK A NEW TARGET"
                    : kind == DecisionKind.BouncerAttack ? "STRIKE BACK"
                    : View.StackTop == null && View.PendingRollValue != null
                        ? "USE AN ABILITY?"
                        : "YOUR RESPONSE");
                return;
            }
            // Blur modals group play variants by card (Smear Campaign's free play
            // once listed dozens of rows): the escape hatch leads, so it never
            // scrolls off-screen.
            var orderedMoves = groups.ModalMoves
                .OrderBy(m => IsPassMove(m) ? 0 : 1)
                .ToList();
            _reaction.Hide();
            _prompt.Show(ModalTitle(), ModalCards(),
                GroupedPlayOptions(orderedMoves, tableLive: false), showCancel: false);
        }

        /// <summary>
        /// Options for a free-play window (Bouncer strike-back, Smear Campaign's
        /// offer): the engine enumerates one move per card x target, which reads as a
        /// wall of near-identical rows. Collapse to one option per CARD — picking it
        /// submits directly (single variant), arms the live-table aiming (reaction
        /// panel), or opens a target page (blur modal). Non-play moves pass through.
        /// </summary>
        private List<Prompt.Option> GroupedPlayOptions(IEnumerable<GameAction> moves, bool tableLive)
        {
            var all = moves.ToList();
            var options = new List<Prompt.Option>();
            var seenDefs = new HashSet<string>();
            foreach (var move in all)
            {
                if (!(move is PlayLemonCard play))
                {
                    options.Add(new Prompt.Option(ModalOptionLabel(move),
                        () => Submit(move), ArtForMove(move)));
                    continue;
                }
                string defId = View.Hand.FirstOrDefault(c => c.InstanceId == play.CardInstanceId)?.DefId;
                if (defId == null || !seenDefs.Add(defId))
                {
                    continue; // copies of one card are one choice
                }
                var variants = all.OfType<PlayLemonCard>()
                    .Where(v => View.Hand.FirstOrDefault(
                        c => c.InstanceId == v.CardInstanceId)?.DefId == defId)
                    .ToList();
                var art = _art.Lemon(defId);
                if (variants.Count == 1)
                {
                    var only = variants[0];
                    options.Add(new Prompt.Option(_session.LabelFor(only), () => Submit(only), art));
                }
                else if (tableLive)
                {
                    int instanceId = play.CardInstanceId;
                    options.Add(new Prompt.Option($"Play {LemonName(defId)}...",
                        () => _table.ActivateHandCard(instanceId), art));
                }
                else
                {
                    string name = LemonName(defId);
                    options.Add(new Prompt.Option($"Play {name}...",
                        () => ShowVariantPage(name, art, variants), art));
                }
            }
            return options;
        }

        /// <summary>
        /// LabelFor, with the theft window's numbers baked into the buttons: the
        /// Profit Share option says what it claws back, Pass says what it forfeits —
        /// the choice reads off the buttons alone. Everything else passes through.
        /// </summary>
        private string ModalOptionLabel(GameAction move)
        {
            if (View.TheftOnMe && View.StackTop == null)
            {
                if (move is RespondToWindow respond && View.TheftRecoverable is int back &&
                    View.Hand.FirstOrDefault(c => c.InstanceId == respond.CardInstanceId)?.DefId
                        == "profit-share")
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

        /// <summary>Second page of a blur-modal free play: this card's targets, with
        /// a way back to reconsider the card.</summary>
        private void ShowVariantPage(string name, Texture2D art, List<PlayLemonCard> variants)
        {
            // Rewind the modal gate: the next tick re-renders and reopens the
            // grouped first page (same trick as the turn banner's dismiss).
            System.Action back = () =>
            {
                _modalRevision = -1;
                _modalSignature = "";
                _renderedRevision = -1;
            };
            // Same guided flows the hand menu uses — Reduce and Reuse must reach the
            // visual pile picker whether it was played from hand or off a modal.
            var guided = GuidedOptions(name, art, variants.Cast<GameAction>(), back,
                out var direct);
            if (direct.Count == 0 && guided.Count == 1)
            {
                _prompt.Hide();
                guided[0].OnPick(); // a lone guided flow — skip the one-button page
                return;
            }
            var options = new List<Prompt.Option> { new Prompt.Option("< Back", back) };
            options.AddRange(guided);
            options.AddRange(direct.Select(v => new Prompt.Option(
                _session.LabelFor(v),
                () => Submit(v),
                v is PlayLemonCard p && p.TargetEquippedInstanceId is int eq
                    ? EquippedArt(eq) : null)));
            _prompt.Show($"Play {name}", new[] { art }, options, showCancel: false);
        }

        /// <summary>
        /// Reverse Engineer's recovered card MUST be played, so when there is exactly
        /// one way to play it the modal is a dead click on a page with nothing to
        /// decide — submit it and let the play speak for itself. Anything with a real
        /// choice (targets, destinations, or a skip because the card has no legal
        /// parameterization) falls through to the normal flows.
        /// </summary>
        private bool TryAutoForcedPlay(MoveGroups groups, int revision)
        {
            var decision = View.MyDecisions.FirstOrDefault();
            if (decision == null || decision.Kind != DecisionKind.ForcedPlay ||
                _autoForcedPlayRevision == revision)
            {
                return false;
            }
            // One move, and it must be the play itself: a lone SkipFreePlay means the
            // card cannot be played at all, and that is the player's beat to see.
            if (groups.ModalMoves.Count != 1 || !(groups.ModalMoves[0] is PlayLemonCard only))
            {
                return false;
            }
            _autoForcedPlayRevision = revision;
            _prompt.Hide();
            _reaction.Hide();
            Submit(only);
            return true;
        }

        /// <summary>
        /// "Choose who to rob" aims at the player bars instead of opening a modal:
        /// the ability card floats mid-screen, the bars stay readable (money, hand
        /// size, VP are exactly the stats you pick a victim by), and the attack
        /// arrow language does the rest.
        /// </summary>
        private bool TryShowPlayerPick(MoveGroups groups)
        {
            var decision = View.MyDecisions.FirstOrDefault();
            if (decision == null || decision.Kind != DecisionKind.AbilityVictim)
            {
                return false;
            }
            if (_table.PlayerPickActive)
            {
                return true; // already aiming this decision
            }
            var byVictim = new Dictionary<int, GameAction>();
            foreach (var move in groups.ModalMoves)
            {
                if (move is SubmitAbilityChoice choice && choice.TargetPlayerId is int victim)
                {
                    byVictim[victim] = move;
                }
            }
            if (byVictim.Count == 0)
            {
                return false;
            }
            var art = decision.SourceInstanceId is int src ? EquippedArt(src) : null;
            _table.BeginPlayerPick(art, new HashSet<int>(byVictim.Keys),
                picked => Submit(byVictim[picked]));
            return true;
        }

        /// <summary>
        /// Word of Mouth's "which stand sells?" aims at the real stands on the
        /// board — sale numbers and tucked upgrades in full view — instead of a
        /// text list over a blurred table.
        /// </summary>
        private bool TryShowStandPick(MoveGroups groups)
        {
            var decision = View.MyDecisions.FirstOrDefault();
            if (decision == null || decision.Kind != DecisionKind.WordOfMouthStand)
            {
                return false;
            }
            var byStand = new Dictionary<int?, List<GameAction>>();
            foreach (var move in groups.ModalMoves)
            {
                if (move is SubmitAbilityChoice choice && choice.StandInstanceId is int stand)
                {
                    if (!byStand.TryGetValue(stand, out var list))
                    {
                        byStand[stand] = list = new List<GameAction>();
                    }
                    list.Add(move);
                }
            }
            if (byStand.Count == 0)
            {
                return false;
            }
            var art = decision.SourceInstanceId is int src ? EquippedArt(src) : null;
            // The choice is mandatory, so a click on empty felt re-arms on the next
            // tick (the variant page's Back rewind) rather than cancelling.
            BeginOwnStandPick(art, "Word of Mouth — choose a stand to sell", byStand,
                onCancel: () =>
                {
                    _modalRevision = -1;
                    _modalSignature = "";
                    _renderedRevision = -1;
                });
            return true;
        }

        /// <summary>Choose-N-cards moments route to the lift-and-glow picker.</summary>
        private bool TryShowPicker()
        {
            if (View.Stage == GameStage.ChoosingLemonLords)
            {
                var dealt = View.LemonLordDealt.ToList();
                _picker.Show($"Keep {_db.Config.LemonLordKept} secret Lemon Lord titles",
                    dealt.Select(id => _art.Title(id)).ToList(),
                    _db.Config.LemonLordKept,
                    picked => Submit(new ChooseLemonLords
                    {
                        KeepTitleIds = picked.Select(i => dealt[i]).ToList(),
                    }),
                    preview: false, // three near-full-size cards need no magnifier
                    // The First Dibs on offer steer which lords are worth keeping.
                    context: View.FirstDibsRow.Select(id => _art.Title(id)).ToList(),
                    contextLabel: "FIRST DIBS CARDS");
                return true;
            }

            var decision = View.MyDecisions.FirstOrDefault();
            if (decision == null)
            {
                return false;
            }

            List<PlayerView.CardInfo> pool;
            int required;
            string title;
            System.Action<List<int>> accept;
            switch (decision.Kind)
            {
                case DecisionKind.DiscardToHandLimit:
                    pool = View.Hand.ToList();
                    required = decision.RequiredCount;
                    title = $"Timeout! Discard {required} card(s)";
                    accept = ids => Submit(new SubmitDiscard { InstanceIds = ids });
                    break;

                case DecisionKind.WhiniestBabyDiscard:
                {
                    // Restricted to the cards just drawn — and phrased positively:
                    // pick what you KEEP, the rest goes to the discard.
                    pool = decision.EligibleCardIds != null
                        ? View.Hand.Where(c => decision.EligibleCardIds.Contains(c.InstanceId)).ToList()
                        : View.Hand.ToList();
                    int keepCount = pool.Count - decision.RequiredCount;
                    if (keepCount > 0)
                    {
                        required = keepCount;
                        title = keepCount == 1
                            ? "Whiniest Baby: pick 1 new card to keep"
                            : $"Whiniest Baby: pick {keepCount} new cards to keep";
                        var drawnPool = pool;
                        accept = keptIds => Submit(new SubmitDiscard
                        {
                            InstanceIds = drawnPool.Select(c => c.InstanceId)
                                .Where(id => !keptIds.Contains(id)).ToList(),
                        });
                    }
                    else
                    {
                        // Thin deck: everything drawn must go — no keep choice exists.
                        required = decision.RequiredCount;
                        title = $"Whiniest Baby: discard {required} card(s)";
                        accept = ids => Submit(new SubmitDiscard { InstanceIds = ids });
                    }
                    break;
                }

                case DecisionKind.AbilityDiscard:
                {
                    // Whispers of Fate: "drew 2, discard 1 of them" — restricted to the
                    // fresh draws and phrased as the Whiniest Baby keep-pick.
                    pool = decision.EligibleCardIds != null
                        ? View.Hand.Where(c => decision.EligibleCardIds.Contains(c.InstanceId)).ToList()
                        : View.Hand.ToList();
                    int keepCount = pool.Count - decision.RequiredCount;
                    if (decision.EligibleCardIds != null && keepCount > 0)
                    {
                        required = keepCount;
                        title = WithSourcePrefix(decision, keepCount == 1
                            ? "pick 1 new card to keep"
                            : $"pick {keepCount} new cards to keep");
                        var drawnPool = pool;
                        accept = keptIds => Submit(new SubmitAbilityChoice
                        {
                            CardInstanceIds = drawnPool.Select(c => c.InstanceId)
                                .Where(id => !keptIds.Contains(id)).ToList(),
                        });
                    }
                    else
                    {
                        required = decision.RequiredCount;
                        title = WithSourcePrefix(decision, $"discard {required} card(s)");
                        accept = ids => Submit(new SubmitAbilityChoice { CardInstanceIds = ids });
                    }
                    break;
                }

                case DecisionKind.AbilityPickCard:
                    pool = View.RevealedHand ?? new List<PlayerView.CardInfo>();
                    required = 1;
                    title = WithSourcePrefix(decision, $"pick a card from {NameOf(decision.ChosenPlayerId ?? 0)}'s hand");
                    accept = ids => Submit(new SubmitAbilityChoice { CardInstanceIds = ids });
                    break;

                case DecisionKind.AbilityGiveBack:
                    pool = View.Hand.Where(c => c.InstanceId != decision.StolenCardId).ToList();
                    required = 1;
                    title = WithSourcePrefix(decision, "give back a different card");
                    accept = ids => Submit(new SubmitAbilityChoice { CardInstanceIds = ids });
                    break;

                default:
                    return false;
            }

            var capturedPool = pool;
            _picker.Show(title,
                capturedPool.Select(c => _art.Lemon(c.DefId)).ToList(),
                required,
                picked => accept(picked.Select(i => capturedPool[i].InstanceId).ToList()));
            return true;
        }

        private string ModalTitle()
        {
            var decision = View.MyDecisions.FirstOrDefault();
            if (decision != null)
            {
                // Name the equipped card behind an ability decision: "you're robbing
                // because of Juice Box Joey" beats a bare "choose who to rob".
                string source = SourceCardName(decision);
                switch (decision.Kind)
                {
                    case DecisionKind.TimeoutFine:
                        return $"Timeout! Pay the ${decision.RequiredMoney} fine " +
                               $"(you have ${View.Players[View.ViewerId].Money})";
                    case DecisionKind.AttackRetarget: return "Your attack was redirected — pick a new target";
                    case DecisionKind.FreePlayOffer: return "Smear Campaign: free play?";
                    case DecisionKind.ForcedPlay:
                        // Only reached when the play still needs a choice — name the
                        // card, so the page is about something.
                        return RecoveredCardDefId(decision) is string recovered
                            ? $"Reverse Engineer: play {LemonName(recovered)}"
                            : "Reverse Engineer: play the recovered card";
                    case DecisionKind.BouncerAttack:
                        // Name the provocation: "strike back?" alone reads as noise
                        // when the attack has already left the stack.
                        return decision.ContextDefId != null && decision.ContextPlayerId is int attacker
                            ? $"{NameOf(attacker)} attacked you with " +
                              $"{LemonName(decision.ContextDefId)} — strike back?"
                            : "Bouncer: strike back?";
                    case DecisionKind.AbilityVictim:
                        return source != null ? $"{source}: choose who to rob" : "Choose who to rob";
                    case DecisionKind.InnovationCopy: return "Innovation: copy which ability?";
                    case DecisionKind.WordOfMouthStand: return "Word of Mouth: which stand sells?";
                    default: return decision.Kind.ToString();
                }
            }
            if (View.StackTop != null)
            {
                var top = View.StackTop;
                string cardName = top.IsPurchase
                    ? _db.BlackMarket(top.DefId).Name
                    : _db.Lemon(top.DefId).Name;
                if (top.AttackTargetId is int t)
                {
                    // The single attack moment: who hits whom, reactions right below.
                    string victim = t == View.ViewerId ? "you" : NameOf(t);
                    string title = $"{NameOf(top.OwnerId)} attacks {victim} with {cardName}!";
                    if (!string.IsNullOrEmpty(top.StolenDefId))
                    {
                        // Finders Keepers / That's Not Fair: name the loot.
                        title += $"  Target: {_db.BlackMarket(top.StolenDefId).Name}.";
                    }
                    return title;
                }
                // Responses name WHAT they answer — "Dex tantrums" alone caused real
                // misreads at the table (which thing is being tantrummed?).
                string answered = null;
                if (!string.IsNullOrEmpty(top.RespondingToDefId))
                {
                    answered = top.RespondingToIsPurchase
                        ? $"the {_db.BlackMarket(top.RespondingToDefId).Name} purchase"
                        : _db.Lemon(top.RespondingToDefId).Name;
                    if (top.RespondingToOwnerId is int owner && !top.RespondingToIsPurchase)
                    {
                        answered = (owner == View.ViewerId ? "your " : NameOf(owner) + "'s ") + answered;
                    }
                }
                if (top.DefId == "tantrum" && answered != null)
                {
                    return $"{NameOf(top.OwnerId)} tantrums {answered}! — respond?";
                }
                if (top.DefId == "tag-youre-it" && top.RedirectTargetId is int redirect)
                {
                    // Who it's going to, with the intel that decides a counter-tantrum.
                    var target = View.Players[redirect];
                    string who = redirect == View.ViewerId ? "YOU" : NameOf(redirect);
                    return $"{NameOf(top.OwnerId)} tags {answered ?? "the attack"} to {who} " +
                           $"(${target.Money}, {target.HandCount} cards) — respond?";
                }
                if (top.DefId == "im-rubber-youre-glue" && answered != null)
                {
                    string backAt = top.RespondingToOwnerId is int attacker
                        ? (attacker == View.ViewerId ? "YOU" : NameOf(attacker))
                        : "the attacker";
                    return $"{NameOf(top.OwnerId)} reflects {answered} back at {backAt}! — respond?";
                }
                string what = top.IsPurchase
                    ? $"{NameOf(top.OwnerId)} is buying {cardName}"
                    : $"{NameOf(top.OwnerId)} played {cardName}";
                return $"{what} — respond?";
            }
            if (View.PendingRollValue is int roll)
            {
                // Name the roll unless it is the ordinary turn sale — the common case
                // stays short, the surprising ones say what they are.
                return RollScopeName(View.PendingRollPurpose, View.PendingRollStandInstanceId)
                        is string why
                    ? $"{why} roll: the die shows {roll} — respond?"
                    : $"The die shows {roll} — respond?";
            }
            if (View.TheftOnMe)
            {
                // Name the thief and the numbers: "$4 stolen, $2 back" IS the
                // decision, so it belongs in the headline.
                string thief = View.TheftAttackerId is int robber ? NameOf(robber) : "Someone";
                string stolen = View.TheftAmount is int amount ? $"${amount}" : "money";
                return View.TheftRecoverable is int back && back > 0
                    ? $"{thief} stole {stolen} from you — play Profit Share to take ${back} back?"
                    : $"{thief} stole {stolen} from you — Profit Share?";
            }
            return "Your choice";
        }

        /// <summary>The card a ForcedPlay decision is about (it sits in hand by then).</summary>
        private string RecoveredCardDefId(PlayerView.DecisionInfo decision) =>
            decision.CardInstanceId is int id
                ? View.Hand.FirstOrDefault(c => c.InstanceId == id)?.DefId
                : null;

        private List<Texture2D> ModalCards()
        {
            var cards = new List<Texture2D>();
            var decision = View.MyDecisions.FirstOrDefault();
            if (decision != null && decision.Kind == DecisionKind.ForcedPlay &&
                RecoveredCardDefId(decision) is string recovered)
            {
                // The recovered card IS the subject of this page — without it the
                // modal reads blank.
                cards.Add(_art.Lemon(recovered));
            }
            if (decision?.ContextDefId != null)
            {
                // The play that provoked the decision (Bouncer: the attack that just
                // hit you) — leftmost, so the story reads cause then answer.
                cards.Add(_art.Lemon(decision.ContextDefId));
            }
            if (decision?.SourceInstanceId is int src)
            {
                // The equipped card whose ability is asking — the "why" of this modal.
                var sourceArt = EquippedArt(src);
                if (sourceArt != null)
                {
                    cards.Add(sourceArt);
                }
            }
            if (View.StackTop != null)
            {
                var top = View.StackTop;
                cards.Add(top.IsPurchase
                    ? _art.BlackMarket(top.DefId, top.Shape ?? Shape.Square)
                    : _art.Lemon(top.DefId));
                if (!string.IsNullOrEmpty(top.StolenDefId))
                {
                    // Show what the steal is aimed at, right next to the attack.
                    cards.Add(_art.BlackMarket(top.StolenDefId, top.StolenShape ?? Shape.Square));
                }
                if (!string.IsNullOrEmpty(top.RespondingToDefId))
                {
                    // The card being tantrummed / tagged / reflected, beside the response.
                    cards.Add(top.RespondingToIsPurchase
                        ? _art.BlackMarket(top.RespondingToDefId, top.RespondingToShape ?? Shape.Square)
                        : _art.Lemon(top.RespondingToDefId));
                }
            }
            return cards;
        }

        /// <summary>"Juice Box Joey: do the thing" when the ability's source card is known.</summary>
        private string WithSourcePrefix(PlayerView.DecisionInfo decision, string action)
        {
            string source = SourceCardName(decision);
            return source != null
                ? $"{source}: {action}"
                : char.ToUpperInvariant(action[0]) + action.Substring(1);
        }

        /// <summary>"Juice Box Joey" for a decision raised by an equipped ability; null otherwise.</summary>
        private string SourceCardName(PlayerView.DecisionInfo decision)
        {
            if (!(decision?.SourceInstanceId is int src))
            {
                return null;
            }
            var info = EquippedInfoOf(src);
            return info == null ? null : BlackMarketName(info.DefId);
        }
    }
}
