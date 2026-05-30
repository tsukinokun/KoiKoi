using System.Collections.Generic;
using System.Linq;

/// 獲得したカードのデータリストから成立している役を判定する純粋ロジッククラス
public static class YakuEvaluator
{
    public static List<YakuResult> CheckAllYaku(List<CardData> capturedCards)
    {
        List<YakuResult> results = new List<YakuResult>();

        if (capturedCards == null || capturedCards.Count == 0) return results;

        // --- 各種カウント・フラグ処理 ---
        int hikariCount = capturedCards.Count(c => c.type == "Hikari");
        int kasuCount = capturedCards.Count(c => c.type == "Kasu");
        int taneCount = capturedCards.Count(c => c.type == "Tane");

        // type名が "Tan" と "Tanzaku" のどちらでもヒットするように小文字にして判定
        int tanzakuCount = capturedCards.Count(c => c.type.ToLower() == "tan" || c.type.ToLower() == "tanzaku");

        bool hasAme = capturedCards.Any(c => c.tags.Contains("Ame"));
        bool hasSakazuki = capturedCards.Any(c => c.tags.Contains("Sakazuki"));

        int inoshikachoCount = capturedCards.Count(c => c.tags.Contains("Inoshikacho"));
        int akatanCount = capturedCards.Count(c => c.tags.Contains("Akatan"));
        int aotanCount = capturedCards.Count(c => c.tags.Contains("Aotan"));

        // ----------------------------------------------------
        // 判定①：光札系（上位の役が下位の役を内包するため独占型）
        // ----------------------------------------------------
        if (hikariCount == 5)
        {
            results.Add(new YakuResult("五光", 15));
        }
        else if (hikariCount == 4 && !hasAme)
        {
            results.Add(new YakuResult("四光", 8));
        }
        else if (hikariCount == 4 && hasAme)
        {
            results.Add(new YakuResult("雨四光", 7));
        }
        else if (hikariCount == 3 && !hasAme)
        {
            results.Add(new YakuResult("三光", 5));
        }

        // ----------------------------------------------------
        // 判定②：独立した特殊役（重複して成立する）
        // ----------------------------------------------------
        if (inoshikachoCount == 3)
        {
            results.Add(new YakuResult("猪鹿蝶", 5));
        }
        if (akatanCount == 3)
        {
            results.Add(new YakuResult("赤短", 5));
        }
        if (aotanCount == 3)
        {
            results.Add(new YakuResult("青短", 5));
        }

        // 花見で一杯 (盃 ＋ 桜に幕)
        if (hasSakazuki && capturedCards.Any(c => c.month == 3 && c.type == "Hikari"))
        {
            results.Add(new YakuResult("花見で一杯", 5));
        }
        // 月見で一杯 (盃 ＋ ススキに月)
        if (hasSakazuki && capturedCards.Any(c => c.month == 8 && c.type == "Hikari"))
        {
            results.Add(new YakuResult("月見で一杯", 5));
        }

        // ----------------------------------------------------
        // 判定③：枚数系の通常役（重複して成立する）
        // ----------------------------------------------------
        if (taneCount >= 5)
        {
            results.Add(new YakuResult("タネ", 1 + (taneCount - 5)));
        }
        if (tanzakuCount >= 5)
        {
            results.Add(new YakuResult("タン", 1 + (tanzakuCount - 5)));
        }
        if (kasuCount >= 10)
        {
            results.Add(new YakuResult("かす", 1 + (kasuCount - 10)));
        }

        return results;
    }
}