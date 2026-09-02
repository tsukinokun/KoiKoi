using System;
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

    [Header("Koi-Koi UI")]
    [SerializeField] private GameObject koiKoiChoicePanel;

    [Header("Presentation")]
    [SerializeField] private AudioManager audioManager;
    [SerializeField] private CutInPresenter cutInPresenter;

    [Header("Initial Deal Settings")]
    [SerializeField] private int initialHandCount = 8;  // お互いの初期手札枚数
    [SerializeField] private int initialFieldCount = 8; // 初期場札枚数

    [Header("Timing Settings")]
    [SerializeField] private float deckDrawAnticipationDelay = 0.8f; // 山札をめくる直前の「溜め」
    [SerializeField] private float deckCardRevealDelay = 1.0f;       // めくったカードを見せる時間
    [SerializeField] private float fieldRearrangeDelay = 0.8f;       // 場札整列後、役判定に移るまでの間
    [SerializeField] private float captureOverlapDuration = 0.5f;    // 獲得時に重なるまでのアニメーション時間
    [SerializeField] private float captureOverlapBuffer = 0.05f;     // 重なりアニメ完了を確実に待つための余裕時間
    [SerializeField] private float captureAreaMoveFallbackDuration = 0.4f; // 獲得エリアViewが未設定の場合のフォールバック
    [SerializeField] private float npcThinkDelay = 1.5f;             // NPCが「考えている」演出時間
    [SerializeField] private float npcDiscardDelay = 0.4f;           // NPCが場に捨てた後の間

    [Header("Capture Animation")]
    [SerializeField] private Vector3 captureOverlapOffset = new Vector3(0.15f, -0.15f, -0.05f);

    // 次のターンへ進むためのコールバック保持用
    private System.Action _onFlowCompleteCallback;

    // こいこい後に役が更新されたかを判定するための得点記録
    private int _playerLastTotalPoints = 0;
    private int _enemyLastTotalPoints = 0;

    private int _tempCurrentPoints = 0;

    // オブジェクトが破棄された時に非同期処理を安全に止めるためのトークン
    private CancellationToken _destroyToken;

    private void OnEnable()
    {
        Card.Clicked += OnCardSelected;
    }

    private void OnDisable()
    {
        Card.Clicked -= OnCardSelected;
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
        // 1️⃣ まずはお互いの手札を初期枚数ずつ交互に配る
        for (int i = 0; i < initialHandCount; i++)
        {
            Card playerCard = deckController.DrawCard();
            playerHandView.AddCard(playerCard, isFaceUp: true);

            Card enemyCard = deckController.DrawCard();
            enemyHandView.AddCard(enemyCard, isFaceUp: false);
        }

        // 2️⃣ 初期場札を配る
        for (int i = 0; i < initialFieldCount; i++)
        {
            Card fieldCard = deckController.DrawCard();
            if (fieldView != null)
            {
                fieldView.AddCard(fieldCard, isFaceUp: true);
            }
        }
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
                audioManager?.PlayPlayerVoice();
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
                audioManager?.PlayPlayerVoice();

                Card hand = _currentSelectedCard;
                _currentSelectedCard = null;

                // 選択状態を解除
                hand.SetSelected(false);

                // 🌟 場に3枚出ている特殊パターンのチェック
                List<Card> matchingFieldCards = GetMatchingFieldCards(hand.Data.month);
                if (matchingFieldCards.Count == 3)
                {
                    // 3枚総取りルート
                    CollectCardsAsync(hand, matchingFieldCards, true).Forget();
                }
                else
                {
                    // 通常の1枚選択ルート
                    CollectCardsAsync(hand, new List<Card> { clickedCard }, true).Forget();
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

            CollectCardsAsync(hand, matchingFieldCards, true).Forget();

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

            CollectCardsAsync(hand, new List<Card> { field }, true).Forget();

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
        if (fieldView == null) return new List<Card>();
        return fieldView.Cards.Where(c => c.Data.month == month).ToList();
    }

    /// <summary>
    /// 指定された月の場札だけを光らせるヘルパー
    /// </summary>
    private void HighlightMatchingFieldCards(int month)
    {
        if (fieldView == null) return;
        foreach (Card fc in fieldView.Cards)
        {
            fc.SetGlow(fc.Data.month == month);
        }
    }

    /// <summary>
    /// すべての場札の光を消すヘルパー
    /// </summary>
    private void ClearAllFieldGlows()
    {
        if (fieldView == null) return;
        foreach (Card fc in fieldView.Cards)
        {
            fc.SetGlow(false);
        }
    }

    private async UniTaskVoid DrawFromDeckRoutineAsync(bool isPlayer)
    {
        _currentState = TurnState.CheckingMatch;

        await UniTask.Delay(TimeSpan.FromSeconds(deckDrawAnticipationDelay), cancellationToken: _destroyToken);

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
        List<Card> matchingFieldCards = fieldView != null
            ? fieldView.Cards.Where(c => c != drawnCard && c.Data.month == drawnCard.Data.month).ToList()
            : new List<Card>();

        await UniTask.Delay(TimeSpan.FromSeconds(deckCardRevealDelay), cancellationToken: _destroyToken);

        if (matchingFieldCards.Count > 0)
        {
            // 🌟 山札からめくった時も、場に3枚あれば総取り（合計4枚獲得）になる
            if (matchingFieldCards.Count == 3)
            {
                Debug.Log($"【山札めくり3枚一致】{drawnCard.Data.month}月が場札の3枚すべてと一致！総取りします。");
            }
            else
            {
                // 通常時（1枚、または2枚あるうちの1枚。2枚の時はルール上どれを貰っても同じなので最初の1枚を選択）
                Debug.Log($"【山札めくり一致】{drawnCard.Data.month}月が場札と一致！獲得します。");
                matchingFieldCards = new List<Card> { matchingFieldCards[0] };
            }
            await CollectCardsAsync(drawnCard, matchingFieldCards, isPlayer, shouldTriggerNextStep: false);
        }
        else
        {
            Debug.Log($"【山札めくり不一致】一致する月がないため、場札に加えます。");
        }

        if (fieldView != null)
        {
            fieldView.Rearrange();
        }
        await UniTask.Delay(TimeSpan.FromSeconds(fieldRearrangeDelay), cancellationToken: _destroyToken);

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
    /// 手札1枚と、それに一致する場札1枚以上（1枚ペア、または3枚総取り）を獲得エリアへ移す共通処理
    /// </summary>
    private async UniTask CollectCardsAsync(Card handCard, List<Card> fieldCards, bool isPlayer, bool shouldTriggerNextStep = true)
    {
        if (handCard != null && fieldCards != null && fieldCards.Count > 0)
        {
            Card overlapTargetCard = fieldCards[0];

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

            // 2. 移動先のローカル座標を算出（重ね合わせ効果。演出として先頭のカードに重ねる）
            Vector3 fieldLocalPos = overlapTargetCard.transform.localPosition;
            Vector3 overlapTargetPos = fieldLocalPos + captureOverlapOffset;

            handCard.transform.localRotation = overlapTargetCard.transform.localRotation;

            // 3. 補間アニメーション
            handCard.MoveToLocalPositionAsync(overlapTargetPos, captureOverlapDuration, handCard.GetCancellationTokenOnDestroy()).Forget();

            await UniTask.Delay(TimeSpan.FromSeconds(captureOverlapDuration + captureOverlapBuffer), cancellationToken: _destroyToken);
        }

        CapturedAreaView targetCapturedView = isPlayer ? playerCapturedView : enemyCapturedView;

        MoveToCapturedArea(handCard, isPlayer);
        if (fieldCards != null)
        {
            foreach (Card fc in fieldCards)
            {
                MoveToCapturedArea(fc, isPlayer);
            }
        }

        float duration = (targetCapturedView != null) ? targetCapturedView.MoveDuration : captureAreaMoveFallbackDuration;
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
            PlayYakuAnimationAndProceedAsync(isPlayer, activeYakus, currentTotalPoints, onComplete).Forget();
        }
        else
        {
            onComplete?.Invoke();
        }
    }

    /// <summary>
    /// 🌟 役成立時のアニメーション待機と、その後のこいこい判定・ゲーム終了フロー
    /// </summary>
    private async UniTaskVoid PlayYakuAnimationAndProceedAsync(bool isPlayer, List<YakuResult> activeYakus, int currentTotalPoints, System.Action onComplete)
    {
        if (cutInPresenter != null)
        {
            await cutInPresenter.PlayVictoryAsync(isPlayer, activeYakus, _destroyToken);
        }

        if (isPlayer)
        {
            _onFlowCompleteCallback = onComplete;
            OpenKoiKoiWindow(currentTotalPoints);
        }
        else
        {
            audioManager?.PlayEnemyAgariVoice();
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

        audioManager?.PlayPlayerKoiKoiVoice();
        if (koiKoiChoicePanel != null) koiKoiChoicePanel.SetActive(false);

        _playerLastTotalPoints = _tempCurrentPoints;

        _onFlowCompleteCallback?.Invoke();
        _onFlowCompleteCallback = null;
    }

    public void OnAgariSelected()
    {
        Debug.Log("勝負！ここで上がりです。");
        audioManager?.PlayPlayerAgariVoice();
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
        await UniTask.Delay(TimeSpan.FromSeconds(npcThinkDelay), cancellationToken: _destroyToken);

        Card npcChoice = null;
        List<Card> fieldChoices = new List<Card>();

        if (fieldView != null && enemyHandView != null)
        {
            foreach (Card npcCard in enemyHandView.Cards)
            {
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
            audioManager?.PlayEnemyVoice();
            // 🌟 NPCの処理も3枚場に出ている時は総取りルーチンへ分岐させる
            if (fieldChoices.Count == 3)
            {
                await CollectCardsAsync(npcChoice, fieldChoices, false);
            }
            else
            {
                await CollectCardsAsync(npcChoice, new List<Card> { fieldChoices[0] }, false);
            }
        }
        else
        {
            if (enemyHandView != null && enemyHandView.transform.childCount > 0)
            {
                audioManager?.PlayEnemyVoice();
                Card discard = enemyHandView.transform.GetChild(0).GetComponent<Card>();
                if (fieldView != null) fieldView.AddCard(discard, true);

                enemyHandView.Rearrange(discard);

                await UniTask.Delay(TimeSpan.FromSeconds(npcDiscardDelay), cancellationToken: _destroyToken);
            }
            DrawFromDeckRoutineAsync(false).Forget();
        }
    }

    private List<YakuResult> CheckAllYaku(bool isPlayer)
    {
        CapturedAreaView targetView = isPlayer ? playerCapturedView : enemyCapturedView;
        List<CardData> capturedCards = targetView != null
            ? targetView.Cards.Select(c => c.Data).ToList()
            : new List<CardData>();

        return YakuEvaluator.CheckAllYaku(capturedCards);
    }

    private void HighlightMatchableCards()
    {
        if (playerHandView == null || fieldView == null) return;

        HashSet<int> fieldMonths = new HashSet<int>(fieldView.Cards.Select(c => c.Data.month));

        foreach (Card handCard in playerHandView.Cards)
        {
            handCard.SetGlow(fieldMonths.Contains(handCard.Data.month));
        }
    }

    private void ClearAllHandGlows()
    {
        if (playerHandView == null) return;

        foreach (Card handCard in playerHandView.Cards)
        {
            handCard.SetGlow(false);
        }
    }
}
