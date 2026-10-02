using System.Collections.Generic;

/// <summary>
/// 役一覧画面に表示する役名・点数・成立条件の一覧（判定ロジックは YakuEvaluator 側）
/// </summary>
public static class YakuCatalog
{
    public readonly struct Entry
    {
        public readonly string Name;
        public readonly int Points;
        public readonly string Condition;
        public readonly string[] ExampleCardIds; // 成立例として並べる札（CardAtlas のスプライト名）

        public Entry(string name, int points, string condition, params string[] exampleCardIds)
        {
            Name = name;
            Points = points;
            Condition = condition;
            ExampleCardIds = exampleCardIds;
        }
    }

    // YakuEvaluator の点数を変えたら、こちらも合わせて変更する
    public static readonly IReadOnlyList<Entry> Entries = new List<Entry>
    {
        new Entry("五光", 15, "光札5枚すべて",
            "Card_01_01", "Card_03_01", "Card_08_01", "Card_11_01", "Card_12_01"),
        new Entry("四光", 8, "雨（柳に小野道風）以外の\n光札4枚",
            "Card_01_01", "Card_03_01", "Card_08_01", "Card_12_01"),
        new Entry("雨四光", 7, "雨を含む光札4枚",
            "Card_01_01", "Card_03_01", "Card_08_01", "Card_11_01"),
        new Entry("三光", 5, "雨以外の光札3枚",
            "Card_01_01", "Card_03_01", "Card_08_01"),
        new Entry("猪鹿蝶", 5, "萩に猪・紅葉に鹿・牡丹に蝶",
            "Card_07_01", "Card_10_01", "Card_06_01"),
        new Entry("赤短", 5, "松・梅・桜の赤短",
            "Card_01_02", "Card_02_02", "Card_03_02"),
        new Entry("青短", 5, "牡丹・菊・紅葉の青短",
            "Card_06_02", "Card_09_02", "Card_10_02"),
        new Entry("花見で一杯", 5, "桜に幕＋菊に盃",
            "Card_03_01", "Card_09_01"),
        new Entry("月見で一杯", 5, "芒に月＋菊に盃",
            "Card_08_01", "Card_09_01"),
        new Entry("タネ", 1, "タネ札5枚\n（1枚増えるごとに+1文）",
            "Card_02_01", "Card_04_01", "Card_05_01", "Card_08_02", "Card_11_02"),
        new Entry("タン", 1, "短冊5枚\n（1枚増えるごとに+1文）",
            "Card_01_02", "Card_04_02", "Card_05_02", "Card_07_02", "Card_11_03"),
        new Entry("かす", 1, "カス札10枚\n（1枚増えるごとに+1文）",
            "Card_01_03", "Card_02_03", "Card_03_03", "Card_04_03", "Card_05_03",
            "Card_06_03", "Card_07_03", "Card_08_03", "Card_09_03", "Card_10_03"),
    };
}
