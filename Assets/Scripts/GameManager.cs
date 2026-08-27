using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Cysharp.Threading.Tasks;
using System.Threading;

public class GameManager : MonoBehaviour
{
    private TurnState _currentState = TurnState.PlayerTurn;

    [Header("Deck Controller")]
    public DeckController deckController;

    [Header("Hand & Field Views")]
    public HandView playerHandView;
    public HandView enemyHandView;
    public FieldView fieldView;

    [Header("Captured Area Views")]
    public CapturedAreaView playerCapturedView;
    public CapturedAreaView enemyCapturedView;

    // 現在選択されているカードの参照
    private Card _currentSelectedCard;

    [Header("UI Managers")]
    [SerializeField] private YakuWindowManager yakuWindowManager;

    [Header("Koi-Koi UI")]
    [SerializeField] private GameObject koiKoiChoicePanel;

    // BGM用の設定
    [Header("Audio Settings (BGM)")]
    [SerializeField] private AudioClip gameBgmClip;      // 流したいBGMのクリップ
    [Range(0f, 1f)][SerializeField] private float bgmVolume = 0.1f; // BGMの音量調整

    [Header("Audio Settings (Volume)")]
    [Range(0f, 1f)][SerializeField] private float seVolume = 1.0f; // ボイス/SEの音量

    // オーディオクリップ登録用
    [Header("Audio Settings (Player)")]
    [SerializeField] private AudioClip playerKoiKoiClip;
    [SerializeField] private AudioClip playerAgariClip;

    [Header("Audio Settings (Enemy/NPC)")]
    [SerializeField] private AudioClip enemyKoiKoiClip;
    [SerializeField] private AudioClip enemyAgariClip;

    [Header("Audio Settings (Voices)")]
    [SerializeField] private List<AudioClip> playerVoiceClips; // プレイヤー用の掛け声3種
    [SerializeField] private List<AudioClip> enemyVoiceClips;  // NPC用の掛け声3種

    [Header("CutIn Animation")]
    [SerializeField] private GameObject cutInEffectObject; // 🌟 BlueCutInEffectBack の GameObject をアタッチ
    [SerializeField] private Animator cutInAnimator;         // Animatorをアタッチ
    private AudioSource _audioSource; // 内部再生用コンポーネント
    private AudioSource _bgmAudioSource;

    // 次のターンへ進むためのコールバック保持用
    private System.Action _onFlowCompleteCallback;

    // こいこい後に役が更新されたかを判定するための得点記録
    private int _playerLastTotalPoints = 0;
    private int _enemyLastTotalPoints = 0;

    private int _tempCurrentPoints = 0;

    // オブジェクトが破棄された時に非同期処理を安全に止めるためのトークン
    private CancellationToken _destroyToken;

    private void Awake()
    {
        // 1️⃣ ボイス・SE用のAudioSourceを確保
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
        }
        _audioSource.spatialBlend = 0f;
        _audioSource.playOnAwake = false;

        // 2️⃣ 🌟 BGM専用のAudioSourceを追加してセットアップ
        _bgmAudioSource = gameObject.AddComponent<AudioSource>();
        _bgmAudioSource.spatialBlend = 0f;
        _bgmAudioSource.loop = true;          // BGMなのでループ再生を有効に
        _bgmAudioSource.playOnAwake = false;   // 管理をコード側で行うため一旦false
        _bgmAudioSource.volume = bgmVolume;

        // 🌟 BGMの再生を開始
        PlayBGM(gameBgmClip);
    }

    void Start()
    {
        _destroyToken = this.GetCancellationTokenOnDestroy();

        if (koiKoiChoicePanel != null) koiKoiChoicePanel.SetActive(false);

        _playerLastTotalPoints = 0;
        _enemyLastTotalPoints = 0;

        if (deckController != null)
        {
            deckController.InitializeDeck();
        }

        // カードが内部的にドローされて各Viewの子要素に収まる
        DealInitialCards();

        // ゲーム開始時、プレイヤーの最初の手札のエフェクトをチェック
        HighlightMatchableCards();
    }

    private void DealInitialCards()
    {
        int handCount = 8;  // お互いの初期手札は8枚
        int fieldCount = 8; // 初期場札は8枚

        // 1️⃣ まずはお互いの手札を8枚ずつ交互に配る
        for (int i = 0; i < handCount; i++)
        {
            Card playerCard = deckController.DrawCard();
            playerHandView.AddCard(playerCard, isFaceUp: true);

            Card enemyCard = deckController.DrawCard();
            enemyHandView.AddCard(enemyCard, isFaceUp: false);
        }

        // 2️⃣ 初期場札を配る
        for (int i = 0; i < fieldCount; i++)
        {
            Card fieldCard = deckController.DrawCard();
            if (fieldView != null)
            {
                fieldView.AddCard(fieldCard, isFaceUp: true);
            }
        }
    }

    void DistributeHandCard(HandView targetHand, bool isFaceUp)
    {
        if (deckController == null || deckController.Count == 0 || targetHand == null) return;
        Card card = deckController.DrawCard();
        targetHand.AddCard(card, isFaceUp);
    }

    void DistributeFieldCard(bool isFaceUp)
    {
        if (deckController == null || deckController.Count == 0 || fieldView == null) return;
        Card card = deckController.DrawCard();
        fieldView.AddCard(card, isFaceUp);
    }

    public void OnCardSelected(Card clickedCard)
    {
        if (clickedCard == null) return;
        if (_currentState != TurnState.PlayerTurn) return;
        if (clickedCard.Data == null) return;

        Transform currentParent = clickedCard.transform.parent;

        // --- プレイヤーの手札がクリックされた場合 ---
        if (playerHandView != null && currentParent == playerHandView.transform)
        {
            // すでに選択されている手札を「もう一度クリック」した場合
            if (_currentSelectedCard == clickedCard)
            {
                // 掛け声
                PlayRandomVoice(playerVoiceClips);
                ExecuteAutoCapture(clickedCard);
                return;
            }

            // まだ何も選択していない、または別の手札を選択した場合
            if (_currentSelectedCard != null) _currentSelectedCard.SetSelected(false);

            _currentSelectedCard = clickedCard;
            _currentSelectedCard.SetSelected(true);

            // 【親切設計】手札を1回クリックした時点で、どの場札が取れるかを光らせる
            HighlightMatchingFieldCards(clickedCard.Data.month);
        }
        // --- 選択中に場札がクリックされた場合 ---
        else if (fieldView != null && currentParent == fieldView.transform && _currentSelectedCard != null)
        {
            if (_currentSelectedCard.Data == null)
            {
                _currentSelectedCard.SetSelected(false);
                _currentSelectedCard = null;
                return;
            }

            // クリックした場札が、選択中の手札と同じ月の場合のみ獲得
            if (_currentSelectedCard.Data.month == clickedCard.Data.month)
            {
                // 掛け声
                PlayRandomVoice(playerVoiceClips);

                Card hand = _currentSelectedCard;
                _currentSelectedCard = null;

                // 選択状態を解除
                hand.SetSelected(false);

                // 🌟 場に3枚出ている特殊パターンのチェック
                List<Card> matchingFieldCards = GetMatchingFieldCards(hand.Data.month);
                if (matchingFieldCards.Count == 3)
                {
                    // 3枚総取りルート
                    CollectMultipleAsync(hand, matchingFieldCards, true).Forget();
                }
                else
                {
                    // 通常の1枚選択ルート
                    CollectPairAsync(hand, clickedCard, true).Forget();
                }

                ClearAllFieldGlows();
                ClearAllHandGlows();
            }
        }
    }

    /// <summary>
    /// 手札のダブルクリック時に、一致する場札の枚数に応じて自動獲得、または選択待ちを行う処理
    /// </summary>
    private void ExecuteAutoCapture(Card clickedHandCard)
    {
        List<Card> matchingFieldCards = GetMatchingFieldCards(clickedHandCard.Data.month);

        // パターン特例：取れるカードが「3枚」の場合 → 3枚すべてを総取りする
        if (matchingFieldCards.Count == 3)
        {
            Debug.Log("場に同じ月のカードが3枚あるため、総取りします！");
            Card hand = clickedHandCard;
            _currentSelectedCard = null;
            hand.SetSelected(false);

            CollectMultipleAsync(hand, matchingFieldCards, true).Forget();

            ClearAllHandGlows();
            ClearAllFieldGlows();
        }
        // パターンA：取れるカードが「1枚だけ」の場合 → 自動でそのカードを取る
        else if (matchingFieldCards.Count == 1)
        {
            Card hand = clickedHandCard;
            Card field = matchingFieldCards[0];

            hand.SetSelected(false);
            _currentSelectedCard = null;

            CollectPairAsync(hand, field, true).Forget();

            ClearAllHandGlows();
            ClearAllFieldGlows();
        }
        // パターンB：取れるカードが「2枚」の場合 → 場札の該当カードを光らせて、どちらを取るかクリックを待つ
        else if (matchingFieldCards.Count == 2)
        {
            Debug.Log($"取れるカードが {matchingFieldCards.Count} 枚あります。場札を選択してください。");
            HighlightMatchingFieldCards(clickedHandCard.Data.month);
        }
        // パターンC：取れるカードが「ない」場合 → そのまま場札として出す
        else
        {
            Debug.Log("取れるカードがないため、場札として出します。");
            Card hand = clickedHandCard;
            _currentSelectedCard = null;
            hand.SetSelected(false);

            if (fieldView != null) fieldView.AddCard(hand, true);

            // 親が変わったので、安全に再整列
            playerHandView.Rearrange(hand);

            hand.SetGlow(false);
            ClearAllHandGlows();

            DrawFromDeckRoutineAsync(true).Forget();
        }
    }

    /// <summary>
    /// 指定された月の場札をリストアップするヘルパー
    /// </summary>
    private List<Card> GetMatchingFieldCards(int month)
    {
        List<Card> list = new List<Card>();
        if (fieldView == null) return list;

        foreach (Transform fieldCardTr in fieldView.transform)
        {
            Card fieldCard = fieldCardTr.GetComponent<Card>();
            if (fieldCard != null && fieldCard.Data != null && fieldCard.Data.month == month)
            {
                list.Add(fieldCard);
            }
        }
        return list;
    }

    /// <summary>
    /// 指定された月の場札だけを光らせるヘルパー
    /// </summary>
    private void HighlightMatchingFieldCards(int month)
    {
        if (fieldView == null) return;
        foreach (Transform fieldCardTr in fieldView.transform)
        {
            Card fc = fieldCardTr.GetComponent<Card>();
            if (fc != null && fc.Data != null)
            {
                fc.SetGlow(fc.Data.month == month);
            }
        }
    }

    /// <summary>
    /// すべての場札の光を消すヘルパー
    /// </summary>
    private void ClearAllFieldGlows()
    {
        if (fieldView == null) return;
        foreach (Transform fieldCardTr in fieldView.transform)
        {
            Card fc = fieldCardTr.GetComponent<Card>();
            if (fc != null) fc.SetGlow(false);
        }
    }

    private async UniTaskVoid DrawFromDeckRoutineAsync(bool isPlayer)
    {
        _currentState = TurnState.CheckingMatch;

        await UniTask.Delay(TimeSpan.FromSeconds(0.8f), cancellationToken: _destroyToken);

        if (deckController == null || deckController.Count == 0)
        {
            Debug.LogWarning("山札が空になりました。");
            SetNextTurn(isPlayer);
            return;
        }

        Card drawnCard = deckController.DrawCard();

        if (fieldView != null) fieldView.AddCard(drawnCard, true);

        Debug.Log($"山札からめくった札: {drawnCard.Data.month}月 ({drawnCard.Data.type})");

        // 山札から引いたカードと一致する場札（自分自身は除く）を全て取得
        List<Card> matchingFieldCards = new List<Card>();
        if (fieldView != null)
        {
            foreach (Transform fieldCardTr in fieldView.transform)
            {
                Card fieldCard = fieldCardTr.GetComponent<Card>();
                if (fieldCard != null && fieldCard != drawnCard && fieldCard.Data != null)
                {
                    if (drawnCard.Data.month == fieldCard.Data.month)
                    {
                        matchingFieldCards.Add(fieldCard);
                    }
                }
            }
        }

        await UniTask.Delay(TimeSpan.FromSeconds(1.0f), cancellationToken: _destroyToken);

        if (matchingFieldCards.Count > 0)
        {
            // 🌟 山札からめくった時も、場に3枚あれば総取り（合計4枚獲得）になる
            if (matchingFieldCards.Count == 3)
            {
                Debug.Log($"【山札めくり3枚一致】{drawnCard.Data.month}月が場札の3枚すべてと一致！総取りします。");
                await CollectMultipleAsync(drawnCard, matchingFieldCards, isPlayer, shouldTriggerNextStep: false);
            }
            else
            {
                // 通常時（1枚、または2枚あるうちの1枚。2枚の時はルール上どれを貰っても同じなので最初の1枚を選択）
                Debug.Log($"【山札めくり一致】{drawnCard.Data.month}月が場札と一致！獲得します。");
                await CollectPairAsync(drawnCard, matchingFieldCards[0], isPlayer, shouldTriggerNextStep: false);
            }
        }
        else
        {
            Debug.Log($"【山札めくり不一致】一致する月がないため、場札に加えます。");
        }

        if (fieldView != null)
        {
            fieldView.Rearrange();
        }
        await UniTask.Delay(TimeSpan.FromSeconds(0.8f), cancellationToken: _destroyToken);

        bool isYakuFlowDone = false;
        CheckYakuAndProceed(isPlayer, () =>
        {
            isYakuFlowDone = true;
        });

        await UniTask.WaitUntil(() => isYakuFlowDone, cancellationToken: _destroyToken);

        SetNextTurn(isPlayer);
    }

    void SetNextTurn(bool currentIsPlayer)
    {
        if (currentIsPlayer)
        {
            _currentState = TurnState.NPCTurn;
            ClearAllHandGlows();

            NPCTurnRoutineAsync().Forget();
        }
        else
        {
            _currentState = TurnState.PlayerTurn;
            Debug.Log("あなたのターンです。");
            HighlightMatchableCards();
        }
    }

    /// <summary>
    /// 通常の1対1のペア獲得処理
    /// </summary>
    private async UniTask CollectPairAsync(Card handCard, Card fieldCard, bool isPlayer, bool shouldTriggerNextStep = true)
    {
        if (handCard != null && fieldCard != null)
        {
            // UIレイアウト破綻対策：親を付け替える前の純粋なワールド座標を一度保持
            Vector3 originalWorldPos = handCard.transform.position;

            // 1. 場札のビューへと親の所属を変更
            handCard.transform.SetParent(fieldView.transform, worldPositionStays: true);
            handCard.transform.position = originalWorldPos;

            // ✨ 親の離脱が確定した瞬間に手札側を再整列
            if (isPlayer && playerHandView != null) playerHandView.Rearrange(handCard);
            else if (!isPlayer && enemyHandView != null) enemyHandView.Rearrange(handCard);

            handCard.SetSelected(false);
            handCard.SetGlow(false);
            handCard.SetFaceUp(true);

            // 2. 移動先のローカル座標を算出（重ね合わせ効果）
            Vector3 fieldLocalPos = fieldCard.transform.localPosition;
            Vector3 overlapTargetPos = new Vector3(fieldLocalPos.x + 0.15f, fieldLocalPos.y - 0.15f, fieldLocalPos.z - 0.05f);

            handCard.transform.localRotation = fieldCard.transform.localRotation;

            // 3. 補間アニメーション
            float overlapDuration = 0.5f;
            handCard.MoveToLocalPositionAsync(overlapTargetPos, overlapDuration, handCard.GetCancellationTokenOnDestroy()).Forget();

            await UniTask.Delay(TimeSpan.FromSeconds(overlapDuration + 0.05f), cancellationToken: _destroyToken);
        }

        CapturedAreaView targetCapturedView = isPlayer ? playerCapturedView : enemyCapturedView;

        MoveToCapturedArea(handCard, isPlayer);
        MoveToCapturedArea(fieldCard, isPlayer);

        float duration = (targetCapturedView != null) ? targetCapturedView.MoveDuration : 0.4f;
        await UniTask.Delay(TimeSpan.FromSeconds(duration), cancellationToken: _destroyToken);

        if (shouldTriggerNextStep)
        {
            DrawFromDeckRoutineAsync(isPlayer).Forget();
        }
    }

    /// <summary>
    /// 🌟場札が3枚ある場合の「総取り（1枚＋3枚の計4枚）」獲得処理
    /// </summary>
    private async UniTask CollectMultipleAsync(Card handCard, List<Card> fieldCards, bool isPlayer, bool shouldTriggerNextStep = true)
    {
        if (handCard != null && fieldCards != null && fieldCards.Count > 0)
        {
            Vector3 originalWorldPos = handCard.transform.position;

            // 場札ビューへと親を変更
            handCard.transform.SetParent(fieldView.transform, worldPositionStays: true);
            handCard.transform.position = originalWorldPos;

            if (isPlayer && playerHandView != null) playerHandView.Rearrange(handCard);
            else if (!isPlayer && enemyHandView != null) enemyHandView.Rearrange(handCard);

            handCard.SetSelected(false);
            handCard.SetGlow(false);
            handCard.SetFaceUp(true);

            // 演出として、3枚のうち真ん中（1番目）のカードに向けて重ね合わせるように移動
            Vector3 targetLocalPos = fieldCards[0].transform.localPosition;
            Vector3 overlapTargetPos = new Vector3(targetLocalPos.x + 0.15f, targetLocalPos.y - 0.15f, targetLocalPos.z - 0.05f);
            handCard.transform.localRotation = fieldCards[0].transform.localRotation;

            float overlapDuration = 0.5f;
            handCard.MoveToLocalPositionAsync(overlapTargetPos, overlapDuration, handCard.GetCancellationTokenOnDestroy()).Forget();

            await UniTask.Delay(TimeSpan.FromSeconds(overlapDuration + 0.05f), cancellationToken: _destroyToken);
        }

        // 獲得札エリアへ移動（出したカード＋場札3枚の合計4枚）
        CapturedAreaView targetCapturedView = isPlayer ? playerCapturedView : enemyCapturedView;

        MoveToCapturedArea(handCard, isPlayer);
        if (fieldCards != null)
        {
            foreach (Card fc in fieldCards)
            {
                MoveToCapturedArea(fc, isPlayer);
            }
        }

        float duration = (targetCapturedView != null) ? targetCapturedView.MoveDuration : 0.4f;
        await UniTask.Delay(TimeSpan.FromSeconds(duration), cancellationToken: _destroyToken);

        if (shouldTriggerNextStep)
        {
            DrawFromDeckRoutineAsync(isPlayer).Forget();
        }
    }

    void MoveToCapturedArea(Card card, bool isPlayer)
    {
        if (card == null) return;

        card.SetSelected(false);
        card.SetFaceUp(true);
        card.SetGlow(false);

        if (isPlayer)
        {
            if (playerCapturedView != null) playerCapturedView.AddCard(card, card.Data.type);
        }
        else
        {
            if (enemyCapturedView != null) enemyCapturedView.AddCard(card, card.Data.type);
        }
    }

    private void CheckYakuAndProceed(bool isPlayer, System.Action onComplete)
    {
        List<YakuResult> activeYakus = CheckAllYaku(isPlayer);
        int currentTotalPoints = activeYakus.Sum(y => y.Points);
        int lastTotalPoints = isPlayer ? _playerLastTotalPoints : _enemyLastTotalPoints;

        if (activeYakus.Count > 0 && currentTotalPoints > lastTotalPoints)
        {
            string combinedName = string.Join(" ・ ", activeYakus.Select(y => y.Name));

            if (yakuWindowManager != null)
            {
                // 1️⃣ 役成立ウィンドウを表示
                yakuWindowManager.ShowYaku(combinedName, currentTotalPoints, () =>
                {
                    // 2️⃣ ここでカットインや役成立のアニメーションを再生・待機させる
                    PlayYakuAnimationAndProceed(isPlayer, currentTotalPoints, onComplete).Forget();
                });
            }
            else
            {
                onComplete?.Invoke();
            }
        }
        else
        {
            onComplete?.Invoke();
        }
    }

    /// <summary>
    /// 🌟 役成立時のアニメーション待機と、その後のこいこい判定・ゲーム終了フロー
    /// </summary>
    private async UniTaskVoid PlayYakuAnimationAndProceed(bool isPlayer, int currentTotalPoints, System.Action onComplete)
    {
        if (cutInEffectObject != null)
        {
            cutInEffectObject.SetActive(true);
        }

        if (cutInAnimator != null)
        {
            cutInAnimator.SetTrigger("PlayCutIn");
        }

        // 🌟 イントロ＋ループ＋アウトロを合わせた総再生時間（秒）だけ待機
        // 例: 合計が 2.5 秒の場合
        await UniTask.Delay(TimeSpan.FromSeconds(2.5f), cancellationToken: _destroyToken);

        // 🌟 再生が終わったら非表示に戻す
        if (cutInEffectObject != null)
        {
            cutInEffectObject.SetActive(false);
        }

        if (isPlayer)
        {
            _onFlowCompleteCallback = onComplete;
            OpenKoiKoiWindow(currentTotalPoints);
        }
        else
        {
            PlayVoice(enemyAgariClip);
            Debug.Log("NPCが役を更新しました。勝負あり！");
            OnGameEnd(false);
        }
    }

    private void OpenKoiKoiWindow(int currentTotalPoints)
    {
        _currentState = TurnState.ChoosingKoiKoi;
        if (koiKoiChoicePanel != null)
        {
            koiKoiChoicePanel.SetActive(true);
            _tempCurrentPoints = currentTotalPoints;
        }
        else
        {
            _tempCurrentPoints = currentTotalPoints;
            OnKoiKoiSelected();
        }
    }

    public void OnKoiKoiSelected()
    {
        Debug.Log("こいこい！勝負を続行します。");

        PlayVoice(playerKoiKoiClip);
        if (koiKoiChoicePanel != null) koiKoiChoicePanel.SetActive(false);

        _playerLastTotalPoints = _tempCurrentPoints;

        _onFlowCompleteCallback?.Invoke();
        _onFlowCompleteCallback = null;
    }

    public void OnAgariSelected()
    {
        Debug.Log("勝負！ここで上がりです。");
        PlayVoice(playerAgariClip);
        if (koiKoiChoicePanel != null) koiKoiChoicePanel.SetActive(false);

        OnGameEnd(true);
    }

    private void OnGameEnd(bool isPlayerWinner)
    {
        _currentState = TurnState.CheckingMatch;
        if (isPlayerWinner)
        {
            Debug.Log($"✨【GAME OVER】プレイヤーの勝ちです！ 最終得点: {CheckAllYaku(true).Sum(y => y.Points)}点 ✨");
        }
        else
        {
            Debug.Log($"💀【GAME OVER】NPCの勝ちです... 最終得点: {CheckAllYaku(false).Sum(y => y.Points)}点 💀");
        }
    }

    private async UniTaskVoid NPCTurnRoutineAsync()
    {
        Debug.Log("NPCが考えています...");
        await UniTask.Delay(TimeSpan.FromSeconds(1.5f), cancellationToken: _destroyToken);

        Card npcChoice = null;
        List<Card> fieldChoices = new List<Card>();

        if (fieldView != null && enemyHandView != null)
        {
            foreach (Transform npcCardTr in enemyHandView.transform)
            {
                Card npcCard = npcCardTr.GetComponent<Card>();
                List<Card> matches = GetMatchingFieldCards(npcCard.Data.month);
                if (matches.Count > 0)
                {
                    npcChoice = npcCard;
                    fieldChoices = matches;
                    break;
                }
            }
        }

        if (npcChoice != null && fieldChoices.Count > 0)
        {
            PlayRandomVoice(enemyVoiceClips);
            // 🌟 NPCの処理も3枚場に出ている時は総取りルーチンへ分岐させる
            if (fieldChoices.Count == 3)
            {
                await CollectMultipleAsync(npcChoice, fieldChoices, false);
            }
            else
            {
                await CollectPairAsync(npcChoice, fieldChoices[0], false);
            }
        }
        else
        {
            if (enemyHandView != null && enemyHandView.transform.childCount > 0)
            {
                PlayRandomVoice(enemyVoiceClips);
                Card discard = enemyHandView.transform.GetChild(0).GetComponent<Card>();
                if (fieldView != null) fieldView.AddCard(discard, true);

                enemyHandView.Rearrange(discard);

                await UniTask.Delay(TimeSpan.FromSeconds(0.4f), cancellationToken: _destroyToken);
            }
            DrawFromDeckRoutineAsync(false).Forget();
        }
    }

    private List<YakuResult> CheckAllYaku(bool isPlayer)
    {
        CapturedAreaView targetView = isPlayer ? playerCapturedView : enemyCapturedView;
        List<CardData> capturedCards = new List<CardData>();

        if (targetView != null)
        {
            foreach (Transform categoryParent in targetView.transform)
            {
                foreach (Transform child in categoryParent)
                {
                    Card card = child.GetComponent<Card>();
                    if (card != null && card.Data != null)
                    {
                        capturedCards.Add(card.Data);
                    }
                }
            }
        }
        return YakuEvaluator.CheckAllYaku(capturedCards);
    }

    private void HighlightMatchableCards()
    {
        if (playerHandView == null || fieldView == null) return;

        HashSet<int> fieldMonths = new HashSet<int>();
        foreach (Transform fieldCardTr in fieldView.transform)
        {
            Card fieldCard = fieldCardTr.GetComponent<Card>();
            if (fieldCard != null && fieldCard.Data != null)
            {
                fieldMonths.Add(fieldCard.Data.month);
            }
        }

        foreach (Transform handCardTr in playerHandView.transform)
        {
            Card handCard = handCardTr.GetComponent<Card>();
            if (handCard != null && handCard.Data != null)
            {
                bool canCapture = fieldMonths.Contains(handCard.Data.month);
                handCard.SetGlow(canCapture);
            }
        }
    }

    private void ClearAllHandGlows()
    {
        if (playerHandView == null) return;

        foreach (Transform handCardTr in playerHandView.transform)
        {
            Card handCard = handCardTr.GetComponent<Card>();
            if (handCard != null)
            {
                handCard.SetGlow(false);
            }
        }
    }

    /// 指定されたオーディオクリップを安全に再生するヘルパー関数
    private void PlayVoice(AudioClip clip)
    {
        if (_audioSource != null && clip != null)
        {
            _audioSource.volume = seVolume; // ここで音量を適用する
            _audioSource.Stop();
            _audioSource.PlayOneShot(clip);
        }
    }

    private void PlayBGM(AudioClip clip)
    {
        if (_bgmAudioSource == null || clip == null) return;

        _bgmAudioSource.clip = clip;
        _bgmAudioSource.Play();
    }

    private void PlayRandomVoice(List<AudioClip> clips)
    {
        if (clips != null && clips.Count > 0)
        {
            AudioClip clip = clips[UnityEngine.Random.Range(0, clips.Count)];
            PlayVoice(clip);
        }
    }
}