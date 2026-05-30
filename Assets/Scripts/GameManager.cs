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

    // こいこい後に役が更新されたかを判定するための得点記録
    private int _playerLastTotalPoints = 0;
    private int _enemyLastTotalPoints = 0;

    void Start()
    {
        if (koiKoiChoicePanel != null) koiKoiChoicePanel.SetActive(false);

        // スコア記録をリセット
        _playerLastTotalPoints = 0;
        _enemyLastTotalPoints = 0;

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

        if (playerHandView != null && currentParent == playerHandView.transform)
        {
            if (_currentSelectedCard == clickedCard)
            {
                _currentSelectedCard = null;
                clickedCard.SetSelected(false);

                if (fieldView != null) fieldView.AddCard(clickedCard, true);
                playerHandView.Rearrange();

                StartCoroutine(DrawFromDeckRoutine(true));
                return;
            }

            if (_currentSelectedCard != null) _currentSelectedCard.SetSelected(false);

            _currentSelectedCard = clickedCard;
            _currentSelectedCard.SetSelected(true);
        }
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

                CollectPair(hand, field, true);

                if (playerHandView != null) playerHandView.Rearrange();
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
            CollectPair(drawnCard, matchedFieldCard, isPlayer);
        }
        else
        {
            Debug.Log($"【山札めくり不一致】一致する月がないため、場札に加えます。");
        }

        if (fieldView != null)
        {
            fieldView.Rearrange();
        }
        yield return new WaitForSeconds(0.8f);

        bool isYakuFlowDone = false;
        CheckYakuAndProceed(isPlayer, () =>
        {
            isYakuFlowDone = true;
        });

        yield return new WaitUntil(() => isYakuFlowDone);

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
    /// ターンの最後に一括して役を判定し、進行を分岐させるメソッド
    /// </summary>
    private void CheckYakuAndProceed(bool isPlayer, System.Action onComplete)
    {
        List<YakuResult> activeYakus = CheckAllYaku(isPlayer);
        int currentTotalPoints = activeYakus.Sum(y => y.Points);

        // 対象のプレイヤーの「前回の総得点」を取得
        int lastTotalPoints = isPlayer ? _playerLastTotalPoints : _enemyLastTotalPoints;

        // 🌟【アルゴリズム修正】今回の総得点が、前回の総得点より高くなっている場合のみ演出へ入る
        if (activeYakus.Count > 0 && currentTotalPoints > lastTotalPoints)
        {
            string combinedName = string.Join(" ・ ", activeYakus.Select(y => y.Name));

            if (yakuWindowManager != null)
            {
                // 表示する得点は、現在の役の純粋な合計値（currentTotalPoints）を渡す
                yakuWindowManager.ShowYaku(combinedName, currentTotalPoints, () =>
                {
                    if (isPlayer)
                    {
                        _onFlowCompleteCallback = onComplete;

                        // 🌟こいこい選択をする前に、今回の新しいスコアを「最新の確定点数」として一旦キープしておく準備
                        // (OnKoiKoiSelectedで実際に上書き更新されます)
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
            // 役ができていない、あるいは点数が更新されていなければスルーして次のターンへ
            onComplete?.Invoke();
        }
    }

    // 引数で今回の確定スコアを受け取れるように拡張
    private void OpenKoiKoiWindow(int currentTotalPoints)
    {
        _currentState = TurnState.ChoosingKoiKoi;
        if (koiKoiChoicePanel != null)
        {
            koiKoiChoicePanel.SetActive(true);

            // ボタンが押されたときのために、一時的に現在のスコアをコンポーネント等に忍ばせるか、
            // もしくはこの時点で一時変数に保持して、選択時に反映させます。
            _tempCurrentPoints = currentTotalPoints;
        }
        else
        {
            _tempCurrentPoints = currentTotalPoints;
            OnKoiKoiSelected();
        }
    }

    // 選択中のスコアを一時保持する変数
    private int _tempCurrentPoints = 0;

    public void OnKoiKoiSelected()
    {
        Debug.Log("こいこい！勝負を続行します。");
        if (koiKoiChoicePanel != null) koiKoiChoicePanel.SetActive(false);

        // 🌟こいこいを選択したため、今回の得点を「過去の基準点」として正式登録
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