namespace LemonadeWars.Engine.Core
{
    public enum GameStage
    {
        /// <summary>Players are choosing which 2 of 3 Lemon Lord titles to keep.</summary>
        ChoosingLemonLords,
        /// <summary>Pre-game snake-draft purchases (must buy a Stand, may buy a Black Market card), twice around.</summary>
        InitialBuys,
        Playing,
        /// <summary>A player ended their turn at the VP target; finishing the round so all get equal turns.</summary>
        FinalRound,
        Finished,
    }
}
