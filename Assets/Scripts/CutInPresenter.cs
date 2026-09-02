using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 役成立時のカットイン演出（イントロ＋ループ＋アウトロ）の再生を管理する
/// </summary>
public class CutInPresenter : MonoBehaviour
{
    private static readonly int PlayCutInHash = Animator.StringToHash("PlayCutIn");
    private static readonly int EndCutInLoopHash = Animator.StringToHash("EndCutInLoop");
    private static readonly int CutInLoopStateHash = Animator.StringToHash("CutIn_Loop");
    private static readonly int IdleStateHash = Animator.StringToHash("Idle");

    [Header("CutIn Animation")]
    [SerializeField] private GameObject cutInEffectObject; // BlueCutInEffectBack の GameObject をアタッチ
    [SerializeField] private Animator cutInAnimator;         // Animatorをアタッチ

    [Header("CutIn Clips (タイムアウト上限の算出用・任意)")]
    [SerializeField] private AnimationClip cutInIntroClip;
    [SerializeField] private AnimationClip cutInOutroClip;

    // 上記クリップが未設定の場合に使うフォールバックの再生時間（秒）
    [SerializeField] private float fallbackIntroDuration = 1.0f;
    [SerializeField] private float fallbackOutroDuration = 1.0f;

    // Animatorが実際に目的のステートへ到達するまでの待機に許容する追加バッファ（秒）
    [SerializeField] private float introTimeoutBuffer = 1.0f;
    [SerializeField] private float outroTimeoutBuffer = 1.0f;

    private bool _isPlaying;

    [Header("Character Icon Overlay")]
    [SerializeField] private Image characterIconImage; // Loop中に重ねるキャラアイコン（あらかじめ非アクティブにしておく）
    [SerializeField] private Sprite playerIconSprite;   // ZundaIcon
    [SerializeField] private Sprite enemyIconSprite;    // TsumugiIcon

    [Header("Yaku Reveal (Loop中に1つずつフェード表示)")]
    [SerializeField] private RectTransform yakuListContainer;
    [SerializeField] private Text yakuEntryTemplate; // yakuListContainerの子として配置し、非アクティブにしておく
    [SerializeField] private float yakuEntryFadeDuration = 0.3f;
    [SerializeField] private float yakuEntryStagger = 0.4f;
    [SerializeField] private float postRevealHoldDelay = 0.6f;

    private readonly List<GameObject> _spawnedYakuEntries = new List<GameObject>();

    public async UniTask PlayVictoryAsync(bool isPlayer, List<YakuResult> yakuResults, CancellationToken cancellationToken)
    {
        if (_isPlaying) return;
        _isPlaying = true;
        try
        {
            if (cutInEffectObject != null)
            {
                cutInEffectObject.SetActive(true);
            }

            if (cutInAnimator != null)
            {
                // 前回再生の残り状態を引きずらないよう、必ずIdleへ巻き戻してから開始する
                cutInAnimator.Rebind();
                cutInAnimator.Update(0f);
                cutInAnimator.ResetTrigger(PlayCutInHash);
                cutInAnimator.ResetTrigger(EndCutInLoopHash);
                cutInAnimator.SetTrigger(PlayCutInHash);

                // 1️⃣ イントロ→ループへ実際に遷移するまで待つ
                await WaitForAnimatorStateAsync(CutInLoopStateHash, GetIntroDuration() + introTimeoutBuffer, cancellationToken);
            }
            else
            {
                // 1️⃣ イントロ(Animator未設定時は実時間フォールバック)
                await UniTask.Delay(TimeSpan.FromSeconds(GetIntroDuration()), cancellationToken: cancellationToken);
            }

            // 2️⃣ ループ開始と同時に、行動したキャラクターのアイコンを重ねる
            ShowCharacterIcon(isPlayer);

            // 3️⃣ ループ中に役を1つずつフェードで表示
            await RevealYakuListAsync(yakuResults, cancellationToken);

            await UniTask.Delay(TimeSpan.FromSeconds(postRevealHoldDelay), cancellationToken: cancellationToken);

            // 4️⃣ 表示し終えたのでアウトロへ
            HideCharacterIcon();
            ClearYakuEntries();

            if (cutInAnimator != null)
            {
                cutInAnimator.SetTrigger(EndCutInLoopHash);
                await WaitForAnimatorStateAsync(IdleStateHash, GetOutroDuration() + outroTimeoutBuffer, cancellationToken);
            }
            else
            {
                await UniTask.Delay(TimeSpan.FromSeconds(GetOutroDuration()), cancellationToken: cancellationToken);
            }

            if (cutInEffectObject != null)
            {
                cutInEffectObject.SetActive(false);
            }
        }
        finally
        {
            _isPlaying = false;
        }
    }

    // Animatorが実際にstateHashのステートへ到達し、遷移が完了するまでポーリングする
    private async UniTask WaitForAnimatorStateAsync(int stateHash, float timeoutSeconds, CancellationToken cancellationToken)
    {
        if (cutInAnimator == null) return;

        float elapsed = 0f;
        while (elapsed < timeoutSeconds)
        {
            AnimatorStateInfo info = cutInAnimator.GetCurrentAnimatorStateInfo(0);
            if (info.shortNameHash == stateHash && !cutInAnimator.IsInTransition(0))
            {
                return;
            }

            elapsed += Time.deltaTime;
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }

        Debug.LogWarning($"CutInPresenter: waiting for animator state (hash={stateHash}) timed out. Proceeding anyway.");
    }

    private float GetIntroDuration()
    {
        return cutInIntroClip != null ? cutInIntroClip.length : fallbackIntroDuration;
    }

    private float GetOutroDuration()
    {
        return cutInOutroClip != null ? cutInOutroClip.length : fallbackOutroDuration;
    }

    private void ShowCharacterIcon(bool isPlayer)
    {
        if (characterIconImage == null) return;

        Sprite sprite = isPlayer ? playerIconSprite : enemyIconSprite;
        characterIconImage.sprite = sprite;
        characterIconImage.gameObject.SetActive(sprite != null);
    }

    private void HideCharacterIcon()
    {
        if (characterIconImage == null) return;
        characterIconImage.gameObject.SetActive(false);
    }

    private async UniTask RevealYakuListAsync(List<YakuResult> yakuResults, CancellationToken cancellationToken)
    {
        if (yakuListContainer == null || yakuEntryTemplate == null || yakuResults == null) return;

        for (int i = 0; i < yakuResults.Count; i++)
        {
            YakuResult yaku = yakuResults[i];

            Text entry = Instantiate(yakuEntryTemplate, yakuListContainer);
            entry.text = $"{yaku.Name}  {yaku.Points}文";
            entry.gameObject.SetActive(true);
            _spawnedYakuEntries.Add(entry.gameObject);

            CanvasGroup group = entry.GetComponent<CanvasGroup>();
            if (group == null) group = entry.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            await FadeCanvasGroupAsync(group, 0f, 1f, yakuEntryFadeDuration, cancellationToken);

            if (i < yakuResults.Count - 1)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(yakuEntryStagger), cancellationToken: cancellationToken);
            }
        }
    }

    private async UniTask FadeCanvasGroupAsync(CanvasGroup group, float from, float to, float duration, CancellationToken cancellationToken)
    {
        group.alpha = from;
        float elapsedTime = 0f;
        while (elapsedTime < duration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            elapsedTime += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsedTime / duration);
            group.alpha = Mathf.Lerp(from, to, t);
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }
        group.alpha = to;
    }

    private void ClearYakuEntries()
    {
        foreach (GameObject entry in _spawnedYakuEntries)
        {
            if (entry != null) Destroy(entry);
        }
        _spawnedYakuEntries.Clear();
    }
}
