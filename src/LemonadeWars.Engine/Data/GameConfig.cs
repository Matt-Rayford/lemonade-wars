namespace LemonadeWars.Engine.Data
{
    /// <summary>Table-level constants from config.json.</summary>
    public sealed class GameConfig
    {
        public int MinPlayers { get; set; }
        public int MaxPlayers { get; set; }
        public int VictoryPointsToTriggerEnd { get; set; }
        public int SaleDieSides { get; set; }
        public int StartingMoney { get; set; }
        public int StartingHandSize { get; set; }
        public int LemonLordDealt { get; set; }
        public int LemonLordKept { get; set; }
        public int BlackMarketFaceUp { get; set; }
        public int BlackMarketFaceUp2Player { get; set; }
        public int TurnStartDraw { get; set; }
        public int ActionsPerTurn { get; set; }
        public int TimeoutHandLimit { get; set; }
        public int BlackMarketRefreshCost { get; set; }
        public int StandCostEscalationPerOwned { get; set; }
    }
}
