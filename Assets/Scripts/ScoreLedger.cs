using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// プレイヤー・敵双方の累計獲得文数と、親・こいこい中の状態を常時表示するUIコンポーネント
/// </summary>
public class ScoreLedger : MonoBehaviour
{
    [SerializeField] private Text playerScoreText;
    [SerializeField] private Text enemyScoreText;

    [Header("Status Badges (親 / こいこい中)")]
    [SerializeField] private Text playerStatusText;
    [SerializeField] private Text enemyStatusText;

    public void UpdateScores(int playerScore, int enemyScore)
    {
        if (playerScoreText != null) playerScoreText.text = playerScore + " 文";
        if (enemyScoreText != null) enemyScoreText.text = enemyScore + " 文";
    }

    public void SetStatus(bool playerIsDealer, bool playerCalledKoiKoi, bool enemyCalledKoiKoi)
    {
        SetBadge(playerStatusText, playerIsDealer, playerCalledKoiKoi);
        SetBadge(enemyStatusText, !playerIsDealer, enemyCalledKoiKoi);
    }

    private static void SetBadge(Text badge, bool isDealer, bool calledKoiKoi)
    {
        if (badge == null) return;

        string text = isDealer ? "親" : "";
        if (calledKoiKoi) text += (text.Length > 0 ? "  " : "") + "こいこい中";
        badge.text = text;
        badge.gameObject.SetActive(text.Length > 0);
    }
}
