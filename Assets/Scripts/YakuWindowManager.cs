using System;
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