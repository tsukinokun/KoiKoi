using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// タイトル画面で敵の強さと対局回数（月）を選択し、対局シーンへ遷移する
/// </summary>
public class TitleController : MonoBehaviour
{
    private const string DifficultyPrefsKey = "NpcDifficulty";

    [Header("Difficulty Buttons (弱い / 普通 / 強い の順)")]
    [SerializeField] private Button[] difficultyButtons;
    [SerializeField] private Color selectedColor = new Color(1f, 0.55f, 0.2f);
    [SerializeField] private Color unselectedColor = new Color(1f, 1f, 1f, 0.85f);

    private void Start()
    {
        // 前回選んだ強さを復元する（初期値は普通）
        int saved = PlayerPrefs.GetInt(DifficultyPrefsKey, (int)NpcDifficulty.Normal);
        ApplyDifficulty(saved);
    }

    public void SelectDifficulty(int difficulty)
    {
        ApplyDifficulty(difficulty);
        PlayerPrefs.SetInt(DifficultyPrefsKey, (int)GameSession.Difficulty);
        PlayerPrefs.Save();
    }

    private void ApplyDifficulty(int difficulty)
    {
        GameSession.Difficulty = (NpcDifficulty)Mathf.Clamp(difficulty, (int)NpcDifficulty.Weak, (int)NpcDifficulty.Strong);

        if (difficultyButtons == null) return;
        for (int i = 0; i < difficultyButtons.Length; i++)
        {
            Button button = difficultyButtons[i];
            if (button == null || button.targetGraphic == null) continue;
            button.targetGraphic.color = i == (int)GameSession.Difficulty ? selectedColor : unselectedColor;
        }
    }

    public void SelectRounds(int rounds)
    {
        GameSession.TotalRounds = rounds;
        GameSession.CurrentRound = 1;
        GameSession.PlayerScore = 0;
        GameSession.EnemyScore = 0;
        GameSession.DealerDecided = false;

        SceneManager.LoadScene("InGameScene");
    }
}
