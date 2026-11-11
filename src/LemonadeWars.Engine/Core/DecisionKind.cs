namespace LemonadeWars.Engine.Core
{
    public enum DecisionKind
    {
        /// <summary>Timeout: discard down to the hand limit. Payload: RequiredCount.</summary>
        DiscardToHandLimit,
        /// <summary>Whiniest Baby turn start: drew 2, discard 1. Payload: RequiredCount = 1.</summary>
        WhiniestBabyDiscard,
        /// <summary>Timeout: pay $3 per played tantrum, selling assets if needed. Payload: RequiredMoney.</summary>
        TimeoutFine,
        /// <summary>Attack moved to a new player needs fresh specifics from its owner. Payload: StackItemId.</summary>
        AttackRetarget,
        /// <summary>Smear Campaign: may play one plan/attack from hand for free (or skip).</summary>
        FreePlayOffer,
        /// <summary>Reverse Engineer: must play the recovered card now.</summary>
        ForcedPlay,
        /// <summary>A steal ability triggered: its owner picks the victim. Payload: SourceInstanceId.</summary>
        AbilityVictim,
        /// <summary>Don's Blessings: pick a card from the victim's revealed hand. Payload: SourceInstanceId, ChosenPlayerId.</summary>
        AbilityPickCard,
        /// <summary>Give the victim a different Lemon card back. Payload: SourceInstanceId, ChosenPlayerId, StolenCardId.</summary>
        AbilityGiveBack,
        /// <summary>Whispers of Fate: discard RequiredCount card(s) after drawing.</summary>
        AbilityDiscard,
        /// <summary>Innovation: choose one of your other Power Pour cards to copy. Payload: SourceInstanceId.</summary>
        InnovationCopy,
        /// <summary>Word of Mouth: choose one of your Stands to sell immediately.</summary>
        WordOfMouthStand,
        /// <summary>Bouncer: you were attacked and may play an attack for free (or skip).</summary>
        BouncerAttack,
    }
}
