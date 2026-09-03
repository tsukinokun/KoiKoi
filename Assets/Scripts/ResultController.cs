using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 対局終了後、最終獲得文数と勝敗を表示し、タイトル/リトライへ遷移する
/// </summary>
public class ResultController : MonoBehaviour
{
    [SerializeField] private Text playerScoreText;
    [SerializeField] private Text enemyScoreText;
    [SerializeField] private Text resultText;

    private void Start()
    {
        int playerScore = GameSession.PlayerScore;
        int enemyScore = GameSession.EnemyScore;

        if (playerScoreText != null) playerScoreText.text = playerScore + " 文";
        if (enemyScoreText != null) enemyScoreText.text = enemyScore + " 文";

        if (resultText != null)
        {
            if (playerScore > enemyScore) resultText.text = "あなたの勝ち！";
            else if (playerScore < enemyScore) resultText.text = "あなたの負け...";
            else resultText.text = "引き分け";
        }
    }

    public void OnBackToTitle()
    {
        SceneManager.LoadScene("Title");
    }

    public void OnRetrySameRounds()
    {
        GameSession.CurrentRound = 1;
        GameSession.PlayerScore = 0;
        GameSession.EnemyScore = 0;

        SceneManager.LoadScene("InGameScene");
    }
}
