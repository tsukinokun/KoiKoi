using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 配られた手札だけで成立する役（手役）
/// </summary>
public enum HandYaku
{
    None,
    Teshi,    // 手四：同じ月が4枚
    Kuttsuki  // くっつき：同じ月のペアが4組
}

/// <summary>
/// デバッグ用に、配る札を指定して珍しい状況を再現するためのプリセット
/// </summary>
public enum DebugDealPreset
{
    None,
    PlayerTeshi,
    PlayerKuttsuki,
    EnemyTeshi,
    EnemyKuttsuki,
    FieldFour,
    EnemyDealer
}

/// <summary>
/// 上がったときの点数計算の結果（基本点・倍付けの内訳・最終点）
/// </summary>
public sealed class Settlement
{
    public int BasePoints;
    public int Multiplier = 1;
    public List<string> Notes = new List<string>();
    public int Total => BasePoints * Multiplier;
}

/// <summary>
/// こいこいの局まわりのルール判定（倍付け・手役・配り直し）。Unityに依存しない純粋ロジック
/// </summary>
public static class KoiKoiRules
{
    public const int HandYakuPoints = 6;
    public const int DoubleThreshold = 7;

    public static HandYaku DetectHandYaku(IEnumerable<CardData> hand)
    {
        List<int> monthCounts = hand.GroupBy(c => c.month).Select(g => g.Count()).ToList();
        if (monthCounts.Any(n => n == 4)) return HandYaku.Teshi;
        if (monthCounts.Count == 4 && monthCounts.All(n => n == 2)) return HandYaku.Kuttsuki;
        return HandYaku.None;
    }

    public static string HandYakuName(HandYaku yaku)
    {
        switch (yaku)
        {
            case HandYaku.Teshi: return "手四";
            case HandYaku.Kuttsuki: return "くっつき";
            default: return "";
        }
    }

    /// <summary>
    /// 場札に同じ月が4枚そろっている（配り直しが必要な）場合は true
    /// </summary>
    public static bool HasFourOfAMonth(IEnumerable<CardData> field)
    {
        return field.GroupBy(c => c.month).Any(g => g.Count() >= 4);
    }

    /// <summary>
    /// 役の合計点に、7文以上の倍付けと、こいこい返しの倍付けをかける
    /// </summary>
    public static Settlement CalculateSettlement(int basePoints, bool opponentCalledKoiKoi)
    {
        var settlement = new Settlement { BasePoints = basePoints };

        if (basePoints >= DoubleThreshold)
        {
            settlement.Multiplier *= 2;
            settlement.Notes.Add($"{DoubleThreshold}文以上 ×2");
        }
        if (opponentCalledKoiKoi)
        {
            settlement.Multiplier *= 2;
            settlement.Notes.Add("こいこい返し ×2");
        }

        return settlement;
    }

    // ---------------------------------------------------------------
    // デバッグ用の配り順
    // ---------------------------------------------------------------

    /// <summary>
    /// 親決めで引く2枚（1枚目がプレイヤー、2枚目が敵）を、プリセットに応じて返す。指定がなければ null
    /// </summary>
    public static List<string> BuildDebugDealerDrawOrder(DebugDealPreset preset)
    {
        if (preset != DebugDealPreset.EnemyDealer) return null;
        return new List<string> { "Card_12_02", "Card_01_03" };
    }

    /// <summary>
    /// 配る順番（プレイヤー・敵の交互に手札、その後に場札）に合わせて、プリセットの状況になる札のID列を作る。
    /// 指定のない枠は、手役や場の4枚がうっかりできないように、月が散らばる順で埋める。指定がなければ null
    /// </summary>
    public static List<string> BuildDebugDrawOrder(DebugDealPreset preset, IEnumerable<CardData> allCards, int handCount, int fieldCount)
    {
        List<CardData> all = allCards.ToList();
        var playerSlots = new List<CardData>();
        var enemySlots = new List<CardData>();
        var fieldSlots = new List<CardData>();

        List<CardData> MonthCards(int month, int count) => all.Where(c => c.month == month).Take(count).ToList();

        switch (preset)
        {
            case DebugDealPreset.PlayerTeshi:
                playerSlots.AddRange(MonthCards(1, 4));
                break;
            case DebugDealPreset.EnemyTeshi:
                enemySlots.AddRange(MonthCards(1, 4));
                break;
            case DebugDealPreset.PlayerKuttsuki:
                for (int m = 1; m <= 4; m++) playerSlots.AddRange(MonthCards(m, 2));
                break;
            case DebugDealPreset.EnemyKuttsuki:
                for (int m = 1; m <= 4; m++) enemySlots.AddRange(MonthCards(m, 2));
                break;
            case DebugDealPreset.FieldFour:
                fieldSlots.AddRange(MonthCards(1, 4));
                break;
            default:
                return null;
        }

        // 残りの札を「各月の1枚目 → 各月の2枚目 → …」の順に並べ、月が偏らないようにする
        var used = new HashSet<CardData>(playerSlots.Concat(enemySlots).Concat(fieldSlots));
        Queue<CardData> filler = new Queue<CardData>(all
            .Where(c => !used.Contains(c))
            .GroupBy(c => c.month)
            .SelectMany(g => g.Select((c, i) => (c, i)))
            .OrderBy(x => x.i).ThenBy(x => x.c.month)
            .Select(x => x.c));

        void Fill(List<CardData> slots, int count)
        {
            while (slots.Count < count)
            {
                // その枠に同じ月が増えすぎない札を優先する（手四・くっつき・場の4枚を偶然作らないため）
                CardData pick = filler.FirstOrDefault(c => slots.Count(s => s.month == c.month) == 0) ?? filler.Peek();
                slots.Add(pick);
                filler = new Queue<CardData>(filler.Where(c => c != pick));
            }
        }

        Fill(playerSlots, handCount);
        Fill(enemySlots, handCount);
        Fill(fieldSlots, fieldCount);

        var order = new List<string>();
        for (int i = 0; i < handCount; i++)
        {
            order.Add(playerSlots[i].id);
            order.Add(enemySlots[i].id);
        }
        order.AddRange(fieldSlots.Select(c => c.id));
        return order;
    }
}
