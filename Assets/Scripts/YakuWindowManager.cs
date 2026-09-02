using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI; // ★標準Textコンポーネントを扱うために必須

public class YakuWindowManager : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private GameObject windowRoot; // HandWindowRoot自身
    [SerializeField] private Text yakuNameText;     
    [SerializeField] private Text pointText;        

    // UIが閉じられたことをGameManagerに伝えるためのコールバック
    private Action _onCloseCallback;

    private void Awake()
    {
        // 初期状態では確実に非表示にしておく
        if (windowRoot != null)
        {
            windowRoot.SetActive(false);
        }
    }

    /// <summary>
    /// 出来役ウィンドウを表示する
    /// </summary>
    public void ShowYaku(string yakuName, int points, Action onClose)
    {
        if (windowRoot == null || yakuNameText == null)
        {
            Debug.LogError("YakuWindowManager: 必要なUIコンポーネントがアサインされていません。");
            onClose?.Invoke();
            return;
        }

        // テキストの更新
        if (pointText != null)
        {
            yakuNameText.text = yakuName;
            pointText.text = points + " 文";
        }
        else
        {
            // もしテキストコンポーネントが1つ（HandTextのみ）なら、改行してまとめて表示
            // 標準Text用のシンプルな文字列結合に修正
            yakuNameText.text = yakuName + "\n" + points + " 文";
        }

        // コールバックの登録
        _onCloseCallback = onClose;

        // ウィンドウをアクティブにする
        windowRoot.SetActive(true);
    }

    /// <summary>
    /// 成立した役をすべてまとめて一括表示する
    /// </summary>
    public void ShowYakuList(List<YakuResult> yakuResults, Action onClose)
    {
        if (windowRoot == null || yakuNameText == null)
        {
            Debug.LogError("YakuWindowManager: 必要なUIコンポーネントがアサインされていません。");
            onClose?.Invoke();
            return;
        }

        int totalPoints = 0;
        var sb = new StringBuilder();
        for (int i = 0; i < yakuResults.Count; i++)
        {
            YakuResult yaku = yakuResults[i];
            totalPoints += yaku.Points;
            sb.Append(pointText != null ? yaku.Name : $"{yaku.Name}  {yaku.Points} 文");
            if (i < yakuResults.Count - 1) sb.Append("\n");
        }

        yakuNameText.text = sb.ToString();
        if (pointText != null)
        {
            pointText.text = totalPoints + " 文";
        }

        _onCloseCallback = onClose;
        windowRoot.SetActive(true);
    }

    public void CloseWindow()
    {
        if (windowRoot != null)
        {
            windowRoot.SetActive(false);
        }

        // 登録されていた終了時処理（GameManager側の次のターン遷移など）を実行
        _onCloseCallback?.Invoke();
        _onCloseCallback = null;
    }

    private void Update()
    {
        if (windowRoot != null && windowRoot.activeSelf)
        {
            if (Input.GetMouseButtonDown(0))
            {
                CloseWindow();
            }
        }
    }
}