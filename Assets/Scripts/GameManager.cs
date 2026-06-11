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

    // 次のターンへ進むためのコールバック保持用
    private System.Action _onFlowCompleteCallback;

    // こいこい後に役が更新されたかを判定するための得点記録
    private int _playerLastTotalPoints = 0;
    private int _enemyLastTotalPoints = 0;

    private int _tempCurrentPoints = 0;

    // オブジェクトが破棄された時に非同期処理を安全に止めるためのトークン
    private CancellationToken _destroyToken;

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

        // --- 1. プレイヤーの手札がクリックされた場合 ---
        if (playerHandView != null && currentParent == playerHandView.transform)
        {
            // すでに選択されている手札を「もう一度クリック」した場合
            if (_currentSelectedCard == clickedCard)
            {
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
        // --- 2. 選択中に場札がクリックされた場合 ---
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
                Card hand = _currentSelectedCard;
                Card field = clickedCard;
                _currentSelectedCard = null;

                // 選択状態を解除
                hand.SetSelected(false);

                // 🌟手札クリック時と「完全に同じ」安全なルートで獲得処理を実行
                CollectPairAsync(hand, field, true).Forget();

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
        // 場札から同じ月のカードをすべてリストアップする
        List<Card> matchingFieldCards = new List<Card>();
        if (fieldView != null)
        {
            foreach (Transform fieldCardTr in fieldView.transform)
            {
                Card fieldCard = fieldCardTr.GetComponent<Card>();
                if (fieldCard != null && fieldCard.Data != null && fieldCard.Data.month == clickedHandCard.Data.month)
                {
                    matchingFieldCards.Add(fieldCard);
                }
            }
        }

        // パターンA：取れるカードが「1枚だけ」の場合 → 自動でそのカードを取る
        if (matchingFieldCards.Count == 1)
        {
            Card hand = clickedHandCard;
            Card field = matchingFieldCards[0];

            hand.SetSelected(false);
            _currentSelectedCard = null;

            CollectPairAsync(hand, field, true).Forget();

            ClearAllHandGlows();
            ClearAllFieldGlows();
        }
        // パターンB：取れるカードが「2枚以上」の場合 → 場札の該当カードを光らせて、クリックを待つ
        else if (matchingFieldCards.Count >= 2)
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

        Card matchedFieldCard = null;
        if (fieldView != null)
        {
            foreach (Transform fieldCardTr in fieldView.transform)
            {
                Card fieldCard = fieldCardTr.GetComponent<Card>();
                if (fieldCard != null && fieldCard != drawnCard)
                {
                    if (drawnCard.Data.month == fieldCard.Data.month)
                    {
                        matchedFieldCard = fieldCard;
                        break;
                    }
                }
            }
        }

        await UniTask.Delay(TimeSpan.FromSeconds(1.0f), cancellationToken: _destroyToken);

        if (matchedFieldCard != null)
        {
            Debug.Log($"【山札めくり一致】{drawnCard.Data.month}月が場札と一致！獲得します。");
            await CollectPairAsync(drawnCard, matchedFieldCard, isPlayer, shouldTriggerNextStep: false);
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

    private async UniTask CollectPairAsync(Card handCard, Card fieldCard, bool isPlayer, bool shouldTriggerNextStep = true)
    {
        if (handCard != null && fieldCard != null)
        {
            // UIレイアウト破綻対策：親を付け替える前の純粋なワールド座標を一度保持
            Vector3 originalWorldPos = handCard.transform.position;

            // 1. 場札のビューへと親の所属を変更
            handCard.transform.SetParent(fieldView.transform, worldPositionStays: true);

            // ズレ対策：強制的に元のワールド座標を再代入
            handCard.transform.position = originalWorldPos;

            // ✨ 親の離脱が確定したこの瞬間に、手札側の再整列（Rearrange）をキックする
            if (isPlayer && playerHandView != null)
            {
                playerHandView.Rearrange(handCard);
            }
            else if (!isPlayer && enemyHandView != null)
            {
                enemyHandView.Rearrange(handCard);
            }

            handCard.SetSelected(false);
            handCard.SetGlow(false);
            handCard.SetFaceUp(true);

            // 2. 移動先のローカル座標を算出
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
                yakuWindowManager.ShowYaku(combinedName, currentTotalPoints, () =>
                {
                    if (isPlayer)
                    {
                        _onFlowCompleteCallback = onComplete;
                        OpenKoiKoiWindow(currentTotalPoints);
                    }
                    else
                    {
                        Debug.Log("NPCが役を更新しました。勝負あり！");
                        OnGameEnd(false);
                    }
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
        if (koiKoiChoicePanel != null) koiKoiChoicePanel.SetActive(false);

        _playerLastTotalPoints = _tempCurrentPoints;

        _onFlowCompleteCallback?.Invoke();
        _onFlowCompleteCallback = null;
    }

    public void OnAgariSelected()
    {
        Debug.Log("勝負！ここで上がりです。");
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
        Card fieldChoice = null;

        if (fieldView != null && enemyHandView != null)
        {
            foreach (Transform npcCardTr in enemyHandView.transform)
            {
                Card npcCard = npcCardTr.GetComponent<Card>();
                foreach (Transform fieldCardTr in fieldView.transform)
                {
                    Card fieldCard = fieldCardTr.GetComponent<Card>();
                    if (npcCard.Data.month == fieldCard.Data.month)
                    {
                        npcChoice = npcCard;
                        fieldChoice = fieldCard;
                        break;
                    }
                }
                if (npcChoice != null) break;
            }
        }

        if (npcChoice != null && fieldChoice != null)
        {
            await CollectPairAsync(npcChoice, fieldChoice, false);
        }
        else
        {
            if (enemyHandView != null && enemyHandView.transform.childCount > 0)
            {
                Card discard = enemyHandView.transform.GetChild(0).GetComponent<Card>();
                if (fieldView != null) fieldView.AddCard(discard, true);

                // 手札を捨てた瞬間、即座にNPCの手札を詰める
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
}