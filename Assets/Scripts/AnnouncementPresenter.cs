using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 画面中央に帯と文字を出して、親決め・配り直し・手役などを告知する
/// </summary>
public class AnnouncementPresenter : MonoBehaviour
{
    [SerializeField] private CanvasGroup root; // 帯と文字をまとめた親（あらかじめ非アクティブにしておく）
    [SerializeField] private Text messageText;
    [SerializeField] private float fadeDuration = 0.2f;

    private void Awake()
    {
        if (root != null)
        {
            root.alpha = 0f;
            root.gameObject.SetActive(false);
        }
    }

    public async UniTask ShowAsync(string message, float holdSeconds, CancellationToken cancellationToken)
    {
        if (root == null || messageText == null)
        {
            Debug.Log($"[告知] {message}");
            return;
        }

        messageText.text = message;
        root.gameObject.SetActive(true);
        try
        {
            await FadeAsync(0f, 1f, cancellationToken);
            await UniTask.Delay(TimeSpan.FromSeconds(holdSeconds), cancellationToken: cancellationToken);
            await FadeAsync(1f, 0f, cancellationToken);
        }
        finally
        {
            if (root != null)
            {
                root.alpha = 0f;
                root.gameObject.SetActive(false);
            }
        }
    }

    private async UniTask FadeAsync(float from, float to, CancellationToken cancellationToken)
    {
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            root.alpha = Mathf.Lerp(from, to, elapsed / fadeDuration);
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }
        root.alpha = to;
    }
}
