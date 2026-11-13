using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Unity
{
    public sealed partial class LemonadeWarsApp
    {
        /// <summary>Who currently owns the staged play (a reflection hands an attack over).</summary>
        private int _stagedAttacker = -1;

        private void HandleLemonCardPlayed(LemonCardPlayed played)
        {
            // Audible for both duelists, silent for bystanders; queued, so it waits out
            // dice theatre instead of stinging mid-roll.
            if (played.TargetPlayerId is int struck &&
                (played.PlayerId == _session.Seat || struck == _session.Seat))
            {
                _fx.QueueSound(Sfx.AttackCard);
            }
            // A reaction thrown at a staged play slaps down on top of it. Anything else is
            // staged beside its player's bar and held there through any reactions.
            bool onThePile = !IsStagedPlay(played.DefId) &&
                _playTheatre.AddResponse(_art.Lemon(played.DefId), played.DefId, played.PlayerId);
            if (onThePile)
            {
                ShowWhereAReactionSendsTheAttack(played);
            }
            bool preStaged = played.PlayerId == _session.Seat && _preStagedDefId == played.DefId &&
                _playTheatre.Matches(_session.Seat, played.DefId);
            _preStagedDefId = null;
            if (!onThePile && !preStaged)
            {
                _stagedAttacker = played.PlayerId;
                var loot = played.DefId == LemonCardIds.FindersKeepers && played.StolenDefId != null
                    ? _art.BlackMarket(played.StolenDefId, played.StolenShape ?? Shape.Square)
                    : TakenArt(played);
                _playTheatre.Begin(_art.Lemon(played.DefId), played.DefId, played.PlayerId,
                    played.TargetPlayerId ?? -1, loot);
            }
        }

        /// <summary>Tag names its new victim; Rubber-Glue sends the attack back at whoever played it.</summary>
        private void ShowWhereAReactionSendsTheAttack(LemonCardPlayed reaction)
        {
            int? proposedVictim =
                reaction.DefId == LemonCardIds.TagYoureIt ? reaction.RedirectTargetId
                : reaction.DefId == LemonCardIds.ImRubberYoureGlue ? reaction.RespondingToOwnerId
                : null;
            if (proposedVictim is int victimId)
            {
                _playTheatre.ProposeTarget(reaction.PlayerId, reaction.DefId, victimId);
            }
        }

        private void HandleAttackRedirected(AttackRedirected redirect)
        {
            _playTheatre.Retarget(_stagedAttacker, redirect.NewTargetId);
            _fx.QueueToast($"{NameOf(redirect.ByPlayerId).ToUpperInvariant()} TAGS IT TO " +
                $"{NameOf(redirect.NewTargetId).ToUpperInvariant()}!");
        }

        private void HandleAttackReflected(AttackReflected reflect)
        {
            // "As if you played it": the reflector owns the attack from here on.
            _stagedAttacker = reflect.ByPlayerId;
            _playTheatre.Retarget(reflect.ByPlayerId, reflect.NewTargetId);
            _fx.QueueToast($"{NameOf(reflect.ByPlayerId).ToUpperInvariant()} " +
                "REFLECTS IT BACK!");
        }

        private void HandleAttackFizzled(AttackFizzled fizzle)
        {
            if (_playTheatre.Matches(fizzle.OwnerId, fizzle.DefId))
            {
                _playTheatre.Cancel();
            }
            _fx.QueueToast($"{LemonName(fizzle.DefId).ToUpperInvariant()} FIZZLES!");
        }

        private void HandlePlayResolved(PlayResolved resolved)
        {
            if (_playTheatre.Matches(resolved.OwnerId, resolved.DefId))
            {
                _playTheatre.Resolve();
            }
            else
            {
                // A reaction on the pile did its work while the play under it stands (a
                // Tantrum that stopped a Tantrum): it clears off, leaving the card open again.
                _playTheatre.SettleResponse(resolved.OwnerId, resolved.DefId);
            }
        }

        private void HandlePlayCancelled(PlayCancelled cancelled)
        {
            if (_playTheatre.Matches(cancelled.OwnerId, cancelled.DefId))
            {
                _playTheatre.Cancel();
            }
            else
            {
                _playTheatre.CancelResponse(cancelled.OwnerId, cancelled.DefId);
            }
            // The Inflatable Decoy is an equipped reaction with no play event of its own:
            // without a reveal it would cancel invisibly.
            if (cancelled.WithDefId == BlackMarketCardIds.InflatableDecoy && cancelled.ByPlayerId is int blocker)
            {
                _fx.QueueReveal(_art.BlackMarket(BlackMarketCardIds.InflatableDecoy, Shape.Square),
                    $"{NameOf(blocker)} blocks with Inflatable Decoy!");
            }
            string byWhat = cancelled.ByPlayerId is int by && cancelled.WithDefId != null
                ? $" BY {NameOf(by).ToUpperInvariant()}'S {CardName(cancelled.WithDefId).ToUpperInvariant()}"
                : "";
            _fx.QueueToast($"{NameOf(cancelled.OwnerId).ToUpperInvariant()}'S " +
                $"{CardName(cancelled.DefId).ToUpperInvariant()} IS CANCELLED{byWhat}!");
        }

        /// <summary>
        /// Plans and attacks are staged on the table; instants (responses) slap onto
        /// them or get the reveal. False for a Black Market id (a cancelled purchase).
        /// </summary>
        private bool IsStagedPlay(string defId)
        {
            try
            {
                var type = _db.Lemon(defId).Type;
                return type == LemonCardType.Plan || type == LemonCardType.Attack;
            }
            catch
            {
                return false; // not a Lemon card
            }
        }

        /// <summary>
        /// Art of the discard card a take is lifting: Reduce and Reuse (a Black Market
        /// card) and Reverse Engineer (a Lemon card, unless it drew instead). Null otherwise.
        /// </summary>
        private UnityEngine.Texture2D TakenArt(LemonCardPlayed played)
        {
            if (played.TakenDefId == null ||
                (played.DefId != LemonCardIds.ReduceAndReuse && played.DefId != LemonCardIds.ReverseEngineer))
            {
                return null;
            }
            return played.TakenIsLemon
                ? _art.Lemon(played.TakenDefId)
                : _art.BlackMarket(played.TakenDefId, played.TakenShape ?? Shape.Square);
        }
    }
}
