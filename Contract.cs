namespace HitmanMod;

public enum ContractDifficulty
{
    VeryEasy,  // Unbewaffnet
    Easy,      // Zerbrochene Flasche
    Medium,    // Messer
    Hard,      // Pistole (M1911)
    VeryHard   // Schrotflinte
}

public enum ContractType
{
    Kill,      // Eliminieren (Deadly Force)
    Knockout   // Bewusstlos schlagen
}

public enum ContractStatus
{
    Offered,
    Active,
    Completed,
    Failed
}

public class Contract
{
    public string Id { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public string TargetDescription { get; set; } = string.Empty;
    public ContractType Type { get; set; }
    public ContractDifficulty Difficulty { get; set; }
    public float Reward { get; set; }
    public ContractStatus Status { get; set; }

    public string DifficultyLabel => Difficulty switch
    {
        ContractDifficulty.VeryEasy => "Very Easy",
        ContractDifficulty.Easy => "Easy",
        ContractDifficulty.Medium => "Medium",
        ContractDifficulty.Hard => "Hard",
        ContractDifficulty.VeryHard => "Very Hard",
        _ => "Unknown"
    };

    public string TypeLabel => Type switch
    {
        ContractType.Kill => "Eliminate",
        ContractType.Knockout => "Knock Out",
        _ => "Unknown"
    };
}
