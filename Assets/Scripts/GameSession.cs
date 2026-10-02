/// <summary>
/// タイトル〜対局〜結果画面の間で、選択した対局回数（月）・敵の強さ・累計獲得文数を保持する
/// </summary>
public static class GameSession
{
    public static int TotalRounds = 1;
    public static int CurrentRound = 1;
    public static int PlayerScore = 0;
    public static int EnemyScore = 0;
    public static NpcDifficulty Difficulty = NpcDifficulty.Normal;

    // 親（先手）。最初の局で札を引いて決め、以降は上がった側が次の局の親になる
    public static bool PlayerIsDealer = true;
    public static bool DealerDecided = false;
}
