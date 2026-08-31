using System.Collections.Generic;
using System.Text;

public class PerkInfo
{
    public int RequiredLevel;
    public string Name;
    public string Description;
}

public static class PerkDescriptionBuilder
{
    public static string Build(string intro, List<PerkInfo> perks, string outro, int currentLevel)
    {
        var sb = new StringBuilder();
        sb.Append(intro).Append("\n\n");
        sb.Append("Passive Perks by Level:\n");
        foreach (var perk in perks)
        {
            sb.Append(currentLevel >= perk.RequiredLevel
                ? $"• Lv{perk.RequiredLevel} – {perk.Name}: {perk.Description}\n"
                : $"• Lv{perk.RequiredLevel} – ???\n");
        }
        sb.Append("\n").Append(outro);
        return sb.ToString();
    }
}
