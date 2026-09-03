using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// プレイヤー・敵双方の累計獲得文数を常時表示するUIコンポーネント
/// </summary>
public class ScoreLedger : MonoBehaviour
{
    [SerializeField] private Text playerScoreText;
    [SerializeField] private Text enemyScoreText;

    public void UpdateScores(int playerScore, int enemyScore)
    {
        if (playerScoreText != null) playerScoreText.text = playerScore + " 文";
        if (enemyScoreText != null) enemyScoreText.text = enemyScore + " 文";
    }
}
