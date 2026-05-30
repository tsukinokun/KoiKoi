using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

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

    void Start()
    {
        if (koiKoiChoicePanel != null) koiKoiChoicePanel.SetActive(false);

        if (deckController != null)
        {
            deckController.InitializeDeck();
        }
        DealInitialCards();
    }

    void DealInitialCards()
    {
        for (int i = 0; i < 8; i++) DistributeFieldCard(true);
        for (int i = 0; i < 8; i++) DistributeHandCard(playerHandView, true);
        for (int i = 0; i < 8; i++) DistributeHandCard(enemyHandView, false);
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

        // 【フェーズ1: 自分の手札をクリックした時】
        if (playerHandView != null && currentParent == playerHandView.transform)
        {
            if (_currentSelectedCard == clickedCard)
            {
                _currentSelectedCard = null;
                clickedCard.SetSelected(false);

                if (fieldView != null) fieldView.AddCard(clickedCard, true);
                playerHandView.Rearrange();

                // 🌟手札を場に捨てた場合も、役判定なしですぐ山札めくりへ
                StartCoroutine(DrawFromDeckRoutine(true));
                return;
            }

            if (_currentSelectedCard != null) _currentSelectedCard.SetSelected(false);

            _currentSelectedCard = clickedCard;
            _currentSelectedCard.SetSelected(true);
        }
        // 【フェーズ2: 手札を選択した状態で、場札をクリックした時】
        else if (fieldView != null && currentParent == fieldView.transform && _currentSelectedCard != null)
        {
            if (_currentSelectedCard.Data == null)
            {
                _currentSelectedCard.SetSelected(false);
                _currentSelectedCard = null;
                return;
            }

            if (_currentSelectedCard.Data.month == clickedCard.Data.month)
            {
                Card hand = _currentSelectedCard;
                Card field = clickedCard;
                _currentSelectedCard = null;

                // 🌟手札での獲得処理（ここでは役判定は行わず、カードの移動のみ）
                CollectPair(hand, field, true);

                if (playerHandView != null) playerHandView.Rearrange();

                // すぐに山札めくりフェーズへ移行
                StartCoroutine(DrawFromDeckRoutine(true));
            }
        }
    }

    private IEnumerator DrawFromDeckRoutine(bool isPlayer)
    {
        _currentState = TurnState.CheckingMatch;
        yield return new WaitForSeconds(0.8f);

        if (deckController == null || deckController.Count == 0)
        {
            Debug.LogWarning("山札が空になりました。");
            SetNextTurn(isPlayer);
            yield break;
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

        yield return new WaitForSeconds(1.0f);

        if (matchedFieldCard != null)
        {
            Debug.Log($"【山札めくり一致】{drawnCard.Data.month}月が場札と一致！獲得します。");
            // 🌟山札めくりでの獲得処理（カードの移動のみ）
            CollectPair(drawnCard, matchedFieldCard, isPlayer);
        }
        else
        {
            Debug.Log($"【山札めくり不一致】一致する月がないため、場札に加えます。");
        }

        // 獲得されて減った、あるいは不一致で増えた場札を綺麗に並べ直す
        if (fieldView != null)
        {
            fieldView.Rearrange();
        }
        yield return new WaitForSeconds(0.8f);

        // 🌟【ここが新しい！】手札・山札のすべての処理が終わったので、ここで最終的な役判定を一発だけ行う
        bool isYakuFlowDone = false;
        CheckYakuAndProceed(isPlayer, () =>
        {
            isYakuFlowDone = true;
        });

        // こいこい選択ウィンドウが出ている間は、ここでコルーチンを一時停止させる
        yield return new WaitUntil(() => isYakuFlowDone);

        // 次のターンへ
        SetNextTurn(isPlayer);
    }

    void SetNextTurn(bool currentIsPlayer)
    {
        if (currentIsPlayer)
        {
            _currentState = TurnState.NPCTurn;
            StartCoroutine(NPCTurnRoutine());
        }
        else
        {
            _currentState = TurnState.PlayerTurn;
            Debug.Log("あなたのターンです。");
        }
    }

    /// <summary>
    /// ペアの獲得処理（純粋に獲得エリアへの移動のみを行う）
    /// </summary>
    void CollectPair(Card handCard, Card fieldCard, bool isPlayer)
    {
        MoveToCapturedArea(handCard, isPlayer);
        MoveToCapturedArea(fieldCard, isPlayer);
    }

    void MoveToCapturedArea(Card card, bool isPlayer)
    {
        card.SetSelected(false);
        card.SetFaceUp(true);

        if (isPlayer)
        {
            if (playerCapturedView != null) playerCapturedView.AddCard(card, card.Data.type);
        }
        else
        {
            if (enemyCapturedView != null) enemyCapturedView.AddCard(card, card.Data.type);
        }
    }

    /// <summary>
    /// 🌟ターンの最後に一括して役を判定し、進行を分岐させるメソッド
    /// </summary>
    private void CheckYakuAndProceed(bool isPlayer, System.Action onComplete)
    {
        List<YakuResult> activeYakus = CheckAllYaku(isPlayer);

        if (activeYakus.Count > 0)
        {
            string combinedName = string.Join(" ・ ", activeYakus.Select(y => y.Name));
            int totalPoints = activeYakus.Sum(y => y.Points);

            if (yakuWindowManager != null)
            {
                yakuWindowManager.ShowYaku(combinedName, totalPoints, () =>
                {
                    if (isPlayer)
                    {
                        // プレイヤーの場合：こいこい選択ダイアログを表示してフローを待機
                        _onFlowCompleteCallback = onComplete;
                        OpenKoiKoiWindow();
                    }
                    else
                    {
                        // NPCの場合：ゲーム終了
                        Debug.Log("NPCが役を完成させました。勝負あり！");
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
            // 役が何もできていなければ、そのまま次のターンへ進行
            onComplete?.Invoke();
        }
    }

    private void OpenKoiKoiWindow()
    {
        _currentState = TurnState.ChoosingKoiKoi;
        if (koiKoiChoicePanel != null)
        {
            koiKoiChoicePanel.SetActive(true);
        }
        else
        {
            OnKoiKoiSelected();
        }
    }

    public void OnKoiKoiSelected()
    {
        Debug.Log("こいこい！勝負を続行します。");
        if (koiKoiChoicePanel != null) koiKoiChoicePanel.SetActive(false);

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
            Debug.Log("✨【GAME OVER】プレイヤーの勝ちです！ ✨");
        }
        else
        {
            Debug.Log("💀【GAME OVER】NPCの勝ちです... 💀");
        }
    }

    private IEnumerator NPCTurnRoutine()
    {
        Debug.Log("NPCが考えています...");
        yield return new WaitForSeconds(1.5f);

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
            CollectPair(npcChoice, fieldChoice, false);
            if (enemyHandView != null) enemyHandView.Rearrange();
            StartCoroutine(DrawFromDeckRoutine(false));
        }
        else
        {
            if (enemyHandView != null && enemyHandView.transform.childCount > 0)
            {
                Card discard = enemyHandView.transform.GetChild(0).GetComponent<Card>();
                if (fieldView != null) fieldView.AddCard(discard, true);
                enemyHandView.Rearrange();
            }
            StartCoroutine(DrawFromDeckRoutine(false));
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
}