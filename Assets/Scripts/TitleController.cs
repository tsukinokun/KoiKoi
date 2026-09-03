using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// タイトル画面で対局回数（月）を選択し、対局シーンへ遷移する
/// </summary>
public class TitleController : MonoBehaviour
{
    public void SelectRounds(int rounds)
    {
        GameSession.TotalRounds = rounds;
        GameSession.CurrentRound = 1;
        GameSession.PlayerScore = 0;
        GameSession.EnemyScore = 0;

        SceneManager.LoadScene("InGameScene");
    }
}
