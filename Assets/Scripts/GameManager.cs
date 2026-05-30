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

    // 🌟インスペクターから紐付ける「こいこい選択パネル」の参照（後述）
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

                CollectPair(hand, field, true, () =>
                {
                    if (playerHandView != null) playerHandView.Rearrange();
                    StartCoroutine(DrawFromDeckRoutine(true));
                });
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
        bool isDrawingProcessDone = false;

        if (matchedFieldCard != null)
        {
            CollectPair(drawnCard, matchedFieldCard, isPlayer, () =>
            {
                isDrawingProcessDone = true;
            });
        }
        else
        {
            isDrawingProcessDone = true;
        }

        yield return new WaitUntil(() => isDrawingProcessDone);

        if (fieldView != null) fieldView.Rearrange();
        yield return new WaitForSeconds(0.8f);

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

    void CollectPair(Card handCard, Card fieldCard, bool isPlayer, System.Action onComplete)
    {
        MoveToCapturedArea(handCard, isPlayer);
        MoveToCapturedArea(fieldCard, isPlayer);

        List<YakuResult> activeYakus = CheckAllYaku(isPlayer);

        if (activeYakus.Count > 0)
        {
            _currentState = TurnState.CheckingMatch;

            string combinedName = string.Join(" ・ ", activeYakus.Select(y => y.Name));
            int totalPoints = activeYakus.Sum(y => y.Points);

            if (yakuWindowManager != null)
            {
                yakuWindowManager.ShowYaku(combinedName, totalPoints, () =>
                {
                    if (isPlayer)
                    {
                        // プレイヤーの場合：こいこい選択ダイアログを表示
                        _onFlowCompleteCallback = onComplete; // コールバックを一時保存
                        OpenKoiKoiWindow();
                    }
                    else
                    {
                        // NPCの場合：現状はシンプルに一律「勝負（あがる）」にしてゲーム終了とします
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
            onComplete?.Invoke();
        }
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

    // こいこい選択ウィンドウを開く
    private void OpenKoiKoiWindow()
    {
        _currentState = TurnState.ChoosingKoiKoi;
        if (koiKoiChoicePanel != null)
        {
            koiKoiChoicePanel.SetActive(true);
        }
        else
        {
            // 万が一UIが未設定なら自動でこいこい（続行）させる
            Debug.LogWarning("koiKoiChoicePanel がアタッチされていないため自動でこいこいします。");
            OnKoiKoiSelected();
        }
    }

    // 🌟ボタンから呼ばれる関数：こいこい（続行）
    public void OnKoiKoiSelected()
    {
        Debug.Log("こいこい！勝負を続行します。");
        if (koiKoiChoicePanel != null) koiKoiChoicePanel.SetActive(false);

        // 保存しておいた次のターンへのフローを再開
        _onFlowCompleteCallback?.Invoke();
        _onFlowCompleteCallback = null;
    }

    // 🌟ボタンから呼ばれる関数：勝負（あがり）
    public void OnAgariSelected()
    {
        Debug.Log("勝負！ここで上がりです。");
        if (koiKoiChoicePanel != null) koiKoiChoicePanel.SetActive(false);

        OnGameEnd(true);
    }

    // 🌟ゲーム終了処理（今はログを出すだけ。ここにリザルト処理を追加していきます）
    private void OnGameEnd(bool isPlayerWinner)
    {
        _currentState = TurnState.CheckingMatch; // 進行をロック
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
            CollectPair(npcChoice, fieldChoice, false, () =>
            {
                if (enemyHandView != null) enemyHandView.Rearrange();
                StartCoroutine(DrawFromDeckRoutine(false));
            });
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