using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Unity
{
    public sealed partial class LemonadeWarsApp
    {
        private const int ActionLogMaxLines = 40;

        private readonly List<string> _actionLog = new List<string>();

        // A run of draws (turn-start, a Draw action, a card handing over 3 at once) grows
        // ONE line. The run belongs to the last logged line: any other line ends it.
        private int _drawRunPlayer = -1;
        private DrawReason _drawRunReason;
        private int _drawRunCount;
        private string _drawRunText;

        private void AddLogLine(string line)
        {
            _actionLog.Add(line);
            if (_actionLog.Count > ActionLogMaxLines)
            {
                _actionLog.RemoveAt(0);
            }
            _renderedRevision = -1; // the log panel only redraws with the table
        }

        /// <summary>
        /// Null for events too noisy to log (individual money ticks, window bookkeeping)
        /// or already folded into the line above (a run of draws).
        /// </summary>
        private string LogLine(GameEvent gameEvent)
        {
            switch (gameEvent)
            {
                case TurnStarted turn: return $"— {NameOf(turn.PlayerId)}'s turn —";
                case ActionTimerExpired expired: return $"{NameOf(expired.PlayerId)} ran out of time";
                case CardDrawn drawn: return DrawLine(drawn);
                case LemonCardPlayed played: return PlayedLine(played);
                case PlayCancelled cancelled: return CancelledLine(cancelled);
                case AttackRedirected redirect:
                    return $"{NameOf(redirect.ByPlayerId)} tags the attack to {NameOf(redirect.NewTargetId)}";
                case AttackReflected reflect:
                    return $"{NameOf(reflect.ByPlayerId)} reflects the attack onto {NameOf(reflect.NewTargetId)}";
                case AttackFizzled fizzle:
                    return $"{NameOf(fizzle.OwnerId)}'s {LemonName(fizzle.DefId)} fizzles";
                case StandPurchased standBuy:
                    return $"{NameOf(standBuy.PlayerId)} buys a {_db.StandType(standBuy.StandTypeId).Name}";
                case BlackMarketPurchased marketBuy:
                    return $"{NameOf(marketBuy.PlayerId)} buys {BlackMarketName(marketBuy.DefId)}";
                case BraggingRightsPurchased brag:
                    return $"{NameOf(brag.PlayerId)} buys Bragging Rights (${brag.Price})";
                // Only the paid sweep is worth a line; the refill after a purchase is not.
                case MarketRefilled refill when refill.RefreshedByPlayerId is int refresher:
                    return $"{NameOf(refresher)} refreshes the Black Market";
                case PowerPourTriggered pour:
                    return $"{NameOf(pour.PlayerId)} hits a Power Pour!";
                case AbilityTriggered ability:
                    return $"{NameOf(ability.PlayerId)}'s {BlackMarketName(ability.DefId)} kicks in";
                case AbilityDeclined declined:
                    return $"{NameOf(declined.PlayerId)} passes on {BlackMarketName(declined.DefId)}";
                case TitleClaimed title:
                    return $"{NameOf(title.PlayerId)} claims {_db.Title(title.TitleId).Name}!";
                case SaleRolled rolled: return SaleRolledLine(rolled);
                case DieRerolled reroll:
                    return $"{NameOf(reroll.ByPlayerId)} plays Out of Stock — reroll: {reroll.NewValue}";
                case RollModified modified:
                    return $"{NameOf(modified.PlayerId)}'s {BlackMarketName(modified.SourceDefId)} " +
                           $"changes the roll to {modified.NewValue}";
                case MoneyStolen theft:
                    return $"{NameOf(theft.ToPlayerId)} steals ${theft.Amount} from {NameOf(theft.FromPlayerId)}";
                case CardsStolen cards:
                    return $"{NameOf(cards.ToPlayerId)} steals {cards.Count} card(s) from {NameOf(cards.FromPlayerId)}";
                case HandsTraded traded:
                    return $"{NameOf(traded.PlayerA)} and {NameOf(traded.PlayerB)} trade hands";
                case TimeoutDrawn timeout:
                    return $"{NameOf(timeout.PlayerId)} draws a Timeout!";
                case WhiniestBabyMoved baby when baby.ToPlayerId is int newBabyId:
                    return $"{NameOf(newBabyId)} is now the Whiniest Baby";
                case SpoiledRottenMoved rotten when rotten.ToPlayerId is int newRottenId:
                    return $"{NameOf(newRottenId)} is now Spoiled Rotten";
                case TrapPlaced trap:
                    return $"{NameOf(trap.OwnerId)} sets a trap on {NameOf(trap.OnPlayerId)}'s turf";
                case StageChanged stage when stage.Stage == GameStage.FinalRound:
                    return "— Final round! —";
                case GameEnded ended:
                    return $"Game over — {string.Join(", ", ended.Winners.Select(NameOf))} wins!";
                default:
                    return null;
            }
        }

        private string PlayedLine(LemonCardPlayed played)
        {
            return $"{NameOf(played.PlayerId)} plays {LemonName(played.DefId)}" +
                   (played.TargetPlayerId is int targetId ? $" on {NameOf(targetId)}"
                    : played.RespondingToDefId != null && played.RespondingToOwnerId is int answeredOwnerId
                        ? $" on {NameOf(answeredOwnerId)}'s {CardName(played.RespondingToDefId)}"
                        : "");
        }

        private string CancelledLine(PlayCancelled cancelled)
        {
            return $"{NameOf(cancelled.OwnerId)}'s {CardName(cancelled.DefId)} is cancelled" +
                (cancelled.ByPlayerId is int by && cancelled.WithDefId != null
                    ? $" by {NameOf(by)}'s {CardName(cancelled.WithDefId)}"
                    : "");
        }

        private string SaleRolledLine(SaleRolled rolled)
        {
            return $"{NameOf(rolled.PlayerId)} rolls a {rolled.Value}" +
                   (RollScopeName(rolled.Purpose, rolled.StandInstanceId) is string why
                       ? $" ({why})" : "");
        }

        /// <summary>
        /// Logs a draw without leaking what was drawn. Returns the new line, or null when
        /// the draw was folded into the line already on the end of the log.
        /// </summary>
        private string DrawLine(CardDrawn drawn)
        {
            bool continues = _drawRunText != null &&
                             _drawRunPlayer == drawn.PlayerId &&
                             _drawRunReason == drawn.Reason &&
                             _actionLog.Count > 0 &&
                             _actionLog[_actionLog.Count - 1] == _drawRunText;

            _drawRunPlayer = drawn.PlayerId;
            _drawRunReason = drawn.Reason;
            _drawRunCount = continues ? _drawRunCount + 1 : 1;
            _drawRunText = DrawRunText(drawn.PlayerId, drawn.Reason, _drawRunCount);

            if (!continues)
            {
                return _drawRunText;
            }
            _actionLog[_actionLog.Count - 1] = _drawRunText;
            return null;
        }

        private string DrawRunText(int playerId, DrawReason reason, int count)
        {
            string cards = count == 1 ? "a card" : $"{count} cards";
            switch (reason)
            {
                case DrawReason.Action:
                    // The one draw kind that costs an action: say so in full.
                    return count == 1
                        ? $"{NameOf(playerId)} draws a Lemon card"
                        : $"{NameOf(playerId)} draws {count} Lemon cards";
                case DrawReason.TurnStart:
                    return $"{NameOf(playerId)} draws {cards} to start the turn";
                default:
                    return $"{NameOf(playerId)} draws {cards}";
            }
        }
    }
}
