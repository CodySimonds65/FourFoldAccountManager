namespace FourFoldAccountManager.Core.Calculation;

public static class CharacterStatLabels
{
    public static string Short(CharacterStat stat) => stat switch
    {
        CharacterStat.Hp => "HP",
        CharacterStat.Sp => "SP",
        CharacterStat.Attack => "ATT",
        CharacterStat.Magic => "MAG",
        CharacterStat.Skill => "SKL",
        CharacterStat.Speed => "SPD",
        CharacterStat.Luck => "LCK",
        CharacterStat.Defense => "DEF",
        CharacterStat.Resistance => "RES",
        _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, "Unknown character stat.")
    };
}
