using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 敵AIが判断に使う盤面情報（見えている情報だけを持つ）
/// </summary>
public sealed class BoardSnapshot
{
    public List<CardData> OwnHand = new List<CardData>();
    public List<CardData> Field = new List<CardData>();
    public List<CardData> OwnCaptured = new List<CardData>();
    public List<CardData> OpponentCaptured = new List<CardData>();
    public int OpponentHandCount;

    // まだ見えていない札（相手の手札＋山札）。どれが相手の手札かは区別しない
    public List<CardData> Unknown = new List<CardData>();
}

/// <summary>
/// 敵AIの思考ロジック。強さに応じて、出す札・取る札・こいこいするかを決める（Unityに依存しない）
/// </summary>
public sealed class NpcBrain
{
    // 役ごとの「対象になる札」と「必要枚数」。光札系は排他なので Group でまとめ、最も高い見込みだけを使う
    private sealed class YakuGoal
    {
        public readonly string Group;
        public readonly int Points;
        public readonly int Need;
        public readonly Func<CardData, bool> Counts;

        public YakuGoal(string group, int points, int need, Func<CardData, bool> counts)
        {
            Group = group;
            Points = points;
            Need = need;
            Counts = counts;
        }
    }

    private static readonly YakuGoal[] Goals =
    {
        new YakuGoal("Hikari", 5, 3, c => IsHikari(c) && !IsAme(c)),  // 三光
        new YakuGoal("Hikari", 8, 4, c => IsHikari(c) && !IsAme(c)),  // 四光
        new YakuGoal("Hikari", 7, 4, IsHikari),                       // 雨四光
        new YakuGoal("Hikari", 15, 5, IsHikari),                      // 五光
        new YakuGoal("Inoshikacho", 5, 3, c => HasTag(c, "Inoshikacho")),
        new YakuGoal("Akatan", 5, 3, c => HasTag(c, "Akatan")),
        new YakuGoal("Aotan", 5, 3, c => HasTag(c, "Aotan")),
        new YakuGoal("Hanami", 5, 2, c => HasTag(c, "Sakazuki") || (c.month == 3 && IsHikari(c))),
        new YakuGoal("Tsukimi", 5, 2, c => HasTag(c, "Sakazuki") || (c.month == 8 && IsHikari(c))),
        new YakuGoal("Tane", 1, 5, c => c.type == "Tane"),
        new YakuGoal("Tan", 1, 5, IsTanzaku),
        new YakuGoal("Kasu", 1, 10, c => c.type == "Kasu"),
    };

    // 重みとこいこいの条件は、AI同士の自動対戦（数千局）で調整した値
    private const float CompletedPointWeight = 10f; // 成立済みの役1文あたりの重み（見込みより実際の点数を重く見る）
    private const float CardValueWeight = 0.1f;
    private const float DenialWeight = 1.0f;        // 相手が欲しい札を先に取る重み（強い）
    private const float KeepForLaterWeight = 0.3f;  // 後で取れそうな札を手元に残す重み（強い）
    private const float WeakCaptureChance = 0.6f;   // 弱いが、取れる札があるときに取る確率
    private const int KoiMinHand = 3;               // こいこいするのに必要な残り手札
    private const int KoiMaxPoints = 6;             // これより高い点数ならすぐ上がる
    private const int KoiThreatMargin = 1;          // 相手が「あと何枚で役」なら危ないとみなすか

    private readonly NpcDifficulty _difficulty;
    private readonly Random _rng;

    public NpcBrain(NpcDifficulty difficulty, Random rng)
    {
        _difficulty = difficulty;
        _rng = rng ?? new Random();
    }

    /// <summary>
    /// 手札から出す札と、取る場札を決める（takes が空なら、取れないので場に捨てる）
    /// </summary>
    public (CardData hand, List<CardData> takes) ChooseHandMove(BoardSnapshot s)
    {
        EnsureTotalCounts(s);
        List<(CardData hand, List<CardData> takes)> moves = EnumerateMoves(s);
        if (moves.Count == 0) return (null, new List<CardData>());

        if (_difficulty == NpcDifficulty.Weak)
        {
            var captures = moves.Where(m => m.takes.Count > 0).ToList();
            var discards = moves.Where(m => m.takes.Count == 0).ToList();
            bool pickCapture = captures.Count > 0 && (discards.Count == 0 || _rng.NextDouble() < WeakCaptureChance);
            var pool = pickCapture ? captures : discards;
            return pool[_rng.Next(pool.Count)];
        }

        return moves.OrderByDescending(m => ScoreMove(s, m.hand, m.takes)).First();
    }

    /// <summary>
    /// 山札からめくった札が場の2枚と一致したとき、どちらを取るかを決める
    /// </summary>
    public CardData ChooseDeckMatch(BoardSnapshot s, CardData drawn, List<CardData> candidates)
    {
        if (candidates == null || candidates.Count == 0) return null;
        EnsureTotalCounts(s);

        if (_difficulty == NpcDifficulty.Weak)
        {
            return candidates[_rng.Next(candidates.Count)];
        }

        return candidates
            .OrderByDescending(c => ScoreCapture(s, new List<CardData> { drawn, c }, new List<CardData> { c }))
            .First();
    }

    /// <summary>
    /// 役ができたときに、こいこいするかどうか（強いだけが判断し、それ以外は必ず上がる）
    /// </summary>
    public bool ShouldKoiKoi(BoardSnapshot s, int currentPoints)
    {
        if (_difficulty != NpcDifficulty.Strong) return false;
        EnsureTotalCounts(s);
        if (s.OwnHand.Count < KoiMinHand) return false;
        if (currentPoints > KoiMaxPoints) return false;

        // 相手があと1枚で役になる（またはすでに役があり、何か取れば上がれる）なら危ないので上がる
        if (IsThreatening(s.OpponentCaptured, s.OwnCaptured)) return false;

        // 自分の役がまだ伸びそうなときだけ続ける
        return HasGrowthChance(s.OwnCaptured, s.OpponentCaptured);
    }

    // ---------------------------------------------------------------
    // 合法手と評価
    // ---------------------------------------------------------------

    private static List<(CardData hand, List<CardData> takes)> EnumerateMoves(BoardSnapshot s)
    {
        var moves = new List<(CardData, List<CardData>)>();
        foreach (CardData hand in s.OwnHand)
        {
            List<CardData> matches = s.Field.Where(f => f.month == hand.month).ToList();
            if (matches.Count == 0)
            {
                moves.Add((hand, new List<CardData>()));
            }
            else if (matches.Count == 3)
            {
                // 3枚あれば総取り
                moves.Add((hand, matches));
            }
            else
            {
                foreach (CardData m in matches)
                {
                    moves.Add((hand, new List<CardData> { m }));
                }
            }
        }
        return moves;
    }

    private float ScoreMove(BoardSnapshot s, CardData hand, List<CardData> takes)
    {
        if (takes.Count > 0)
        {
            var gained = new List<CardData>(takes) { hand };
            return ScoreCapture(s, gained, takes);
        }

        return ScoreDiscard(s, hand);
    }

    // gained: 自分の取り札に加わる札 / takenFromField: そのうち場から取った札（相手に取られなくなる札）
    private float ScoreCapture(BoardSnapshot s, List<CardData> gained, List<CardData> takenFromField)
    {
        // 普通は札そのものの価値だけを見る。強いは役の進み具合も見る
        if (_difficulty == NpcDifficulty.Normal)
        {
            return gained.Sum(CardValue) * CardValueWeight;
        }

        float score = Gain(s.OwnCaptured, s.OpponentCaptured, gained);

        if (_difficulty == NpcDifficulty.Strong)
        {
            // 相手が欲しがっている場札を先に取る
            score += DenialWeight * Gain(s.OpponentCaptured, s.OwnCaptured, takenFromField);
        }

        return score;
    }

    private float ScoreDiscard(BoardSnapshot s, CardData hand)
    {
        float score = -CardValue(hand) * CardValueWeight;

        if (_difficulty == NpcDifficulty.Strong)
        {
            int sameMonthUnknown = s.Unknown.Count(c => c.month == hand.month);

            // 相手がこの札を取れる確率（同じ月の見えていない札を、相手が持っている見込み）
            float p = s.Unknown.Count > 0
                ? Math.Min(1f, (float)sameMonthUnknown * s.OpponentHandCount / s.Unknown.Count)
                : 0f;
            score -= p * Gain(s.OpponentCaptured, s.OwnCaptured, new List<CardData> { hand });

            // 同じ月がまだ残っている札は、後で自分が取れるかもしれないので少し手元に残す
            if (sameMonthUnknown > 0)
            {
                score -= CardValue(hand) * KeepForLaterWeight * CardValueWeight;
            }
        }

        return score;
    }

    private static float Gain(List<CardData> own, List<CardData> opponent, List<CardData> added)
    {
        var after = new List<CardData>(own);
        after.AddRange(added);
        float cardValues = added.Sum(CardValue) * CardValueWeight;
        return Potential(after, opponent) - Potential(own, opponent) + cardValues;
    }

    /// <summary>
    /// 取り札の「役への近さ」。成立済みの点数と、各役の進み具合（相手に取られて不可能な役は除く）を合計する
    /// </summary>
    private static float Potential(List<CardData> own, List<CardData> opponent)
    {
        float completed = YakuEvaluator.CheckAllYaku(own).Sum(y => y.Points) * CompletedPointWeight;

        var bestByGroup = new Dictionary<string, float>();
        foreach (YakuGoal goal in Goals)
        {
            int have = own.Count(goal.Counts);
            int lost = opponent.Count(goal.Counts);
            int total = AllCardsCount(goal);
            if (total - lost < goal.Need) continue; // 相手に取られすぎて、もう成立しない

            float progress = Math.Min(have, goal.Need) / (float)goal.Need;
            float value = goal.Points * progress * progress;

            if (!bestByGroup.TryGetValue(goal.Group, out float best) || value > best)
            {
                bestByGroup[goal.Group] = value;
            }
        }

        return completed + bestByGroup.Values.Sum();
    }

    private static bool IsThreatening(List<CardData> captured, List<CardData> opponentOfThem)
    {
        if (YakuEvaluator.CheckAllYaku(captured).Count > 0) return true;

        foreach (YakuGoal goal in Goals)
        {
            int have = captured.Count(goal.Counts);
            int lost = opponentOfThem.Count(goal.Counts);
            if (AllCardsCount(goal) - lost < goal.Need) continue;
            if (have >= goal.Need - KoiThreatMargin) return true;
        }
        return false;
    }

    private static bool HasGrowthChance(List<CardData> own, List<CardData> opponent)
    {
        foreach (YakuGoal goal in Goals)
        {
            int have = own.Count(goal.Counts);
            int lost = opponent.Count(goal.Counts);
            int remaining = AllCardsCount(goal) - lost - have;
            if (remaining <= 0) continue;

            // タネ・タン・カスは成立後も1枚ごとに伸びる。それ以外は、あと1〜2枚で成立する役を狙う
            bool countsAfterComplete = goal.Group == "Tane" || goal.Group == "Tan" || goal.Group == "Kasu";
            if (have >= goal.Need && countsAfterComplete) return true;
            if (have < goal.Need && have >= goal.Need - 2) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------
    // 札の価値・判定ヘルパー
    // ---------------------------------------------------------------

    private static float CardValue(CardData c)
    {
        if (IsHikari(c)) return IsAme(c) ? 14f : 20f;
        if (c.type == "Tane") return HasTag(c, "Inoshikacho") || HasTag(c, "Sakazuki") ? 15f : 10f;
        if (IsTanzaku(c)) return HasTag(c, "Akatan") || HasTag(c, "Aotan") ? 9f : 6f;
        return 1f;
    }

    private static bool IsHikari(CardData c) => c.type == "Hikari";
    private static bool IsAme(CardData c) => IsHikari(c) && HasTag(c, "Ame");
    private static bool IsTanzaku(CardData c) => c.type.ToLower() == "tan" || c.type.ToLower() == "tanzaku";
    private static bool HasTag(CardData c, string tag) => c.tags != null && c.tags.Contains(tag);

    // 札の構成（全48枚）から、その役の対象になる札の総数
    // （盤面の札をすべて合わせると全48枚になるので、最初の判断時に一度だけ数える）
    private static readonly Dictionary<YakuGoal, int> TotalCounts = new Dictionary<YakuGoal, int>();

    private static void EnsureTotalCounts(BoardSnapshot s)
    {
        if (TotalCounts.Count > 0) return;

        List<CardData> all = s.OwnHand.Concat(s.Field).Concat(s.OwnCaptured).Concat(s.OpponentCaptured).Concat(s.Unknown).ToList();
        if (all.Count < 48) return; // めくり途中の札などが欠けている盤面では数えない

        foreach (YakuGoal goal in Goals)
        {
            TotalCounts[goal] = all.Count(goal.Counts);
        }
    }

    private static int AllCardsCount(YakuGoal goal)
    {
        return TotalCounts.TryGetValue(goal, out int n) ? n : int.MaxValue / 2;
    }
}
