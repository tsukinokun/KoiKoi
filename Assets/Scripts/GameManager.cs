using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class GameManager : MonoBehaviour
{
    private TurnState _currentState = TurnState.PlayerTurn;

    [Header("Deck Controller")]
    public DeckController deckController; // 🌟新設した山札コントローラー

    [Header("Hand & Field Views")]
    public HandView playerHandView;       // プレイヤー手札の管理ビュー
    public HandView enemyHandView;        // 相手手札の管理ビュー
    public FieldView fieldView;           // 場札の管理ビュー

    [Header("Captured Area Views")]
    public CapturedAreaView playerCapturedView; // プレイヤー獲得札の管理ビュー
    public CapturedAreaView enemyCapturedView;  // 相手獲得札の管理ビュー

    // 現在選択されているカードの参照
    private Card _currentSelectedCard;

    [Header("UI Managers")]
    [SerializeField] private YakuWindowManager yakuWindowManager;

    void Start()
    {
        // 🌟山札の生成とシャッフルをコントローラーに委譲
        if (deckController != null)
        {
            deckController.InitializeDeck();
        }

        // カードを配る
        DealInitialCards();
    }

    // 最初の手札・場札を配る
    void DealInitialCards()
    {
        // 場札を配る
        for (int i = 0; i < 8; i++) DistributeFieldCard(true);

        // プレイヤーの手札を配る
        for (int i = 0; i < 8; i++) DistributeHandCard(playerHandView, true);

        // 相手の手札を配る（裏向き）
        for (int i = 0; i < 8; i++) DistributeHandCard(enemyHandView, false);
    }

    // 山札から1枚引いて手札に追加する抽象化されたメソッド
    void DistributeHandCard(HandView targetHand, bool isFaceUp)
    {
        if (deckController == null || deckController.Count == 0 || targetHand == null) return;

        // 🌟DeckControllerから1枚引く
        Card card = deckController.DrawCard();
        targetHand.AddCard(card, isFaceUp);
    }

    // 山札から1枚引いて場札に追加する抽象化されたメソッド
    void DistributeFieldCard(bool isFaceUp)
    {
        if (deckController == null || deckController.Count == 0 || fieldView == null) return;

        // 🌟DeckControllerから1枚引く
        Card card = deckController.DrawCard();
        fieldView.AddCard(card, isFaceUp);
    }

    public void OnCardSelected(Card clickedCard)
    {
        if (clickedCard == null) return;
        if (_currentState != TurnState.PlayerTurn) return;
        if (clickedCard.Data == null)
        {
            Debug.LogError($"クリックされたカード {clickedCard.name} のDataが割り当てられていません！");
            return;
        }

        Transform currentParent = clickedCard.transform.parent;

        // 【フェーズ1: 自分の手札をクリックした時】
        if (playerHandView != null && currentParent == playerHandView.transform)
        {
            if (_currentSelectedCard == clickedCard)
            {
                Debug.Log($"手札の再クリックを検知: {clickedCard.Data.month}月を場に捨てます。");

                _currentSelectedCard = null;
                clickedCard.SetSelected(false);

                if (fieldView != null)
                {
                    fieldView.AddCard(clickedCard, true);
                }

                playerHandView.Rearrange();

                StartCoroutine(DrawFromDeckRoutine(true));
                return;
            }

            if (_currentSelectedCard != null)
            {
                _currentSelectedCard.SetSelected(false);
            }

            _currentSelectedCard = clickedCard;
            _currentSelectedCard.SetSelected(true);
            Debug.Log($"手札を選択しました: {_currentSelectedCard.Data.month}月 ({_currentSelectedCard.Data.type})");
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
                Debug.Log($"【手札獲得一致】{_currentSelectedCard.Data.month}月が一致しました！");

                Card hand = _currentSelectedCard;
                Card field = clickedCard;

                _currentSelectedCard = null;

                CollectPair(hand, field, true, () =>
                {
                    if (playerHandView != null) playerHandView.Rearrange();
                    StartCoroutine(DrawFromDeckRoutine(true));
                });
            }
            else
            {
                Debug.LogWarning("月の違う札です。合わせられません。");
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

        // 🌟DeckControllerから山札めくり用のカードを1枚引く
        Card drawnCard = deckController.DrawCard();

        if (fieldView != null)
        {
            fieldView.AddCard(drawnCard, true);
        }
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

        bool isDrawingProcessDone = false;

        if (matchedFieldCard != null)
        {
            Debug.Log($"【山札めくり一致】{drawnCard.Data.month}月が場札と一致！獲得します。");

            CollectPair(drawnCard, matchedFieldCard, isPlayer, () =>
            {
                isDrawingProcessDone = true;
            });
        }
        else
        {
            Debug.Log($"【山札めくり不一致】一致する月がないため、場札に加えます。");
            isDrawingProcessDone = true;
        }

        yield return new WaitUntil(() => isDrawingProcessDone);

        if (fieldView != null)
        {
            fieldView.Rearrange();
        }

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

        if (isPlayer && activeYakus.Count > 0 && yakuWindowManager != null)
        {
            _currentState = TurnState.CheckingMatch;

            string combinedName = string.Join(" ・ ", activeYakus.Select(y => y.Name));
            int totalPoints = activeYakus.Sum(y => y.Points);

            yakuWindowManager.ShowYaku(combinedName, totalPoints, () =>
            {
                onComplete?.Invoke();
            });
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
            Debug.Log($"【NPC獲得】{npcChoice.Data.month}月が一致しました。");

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
                Debug.Log($"NPCは一致する札がないため、{discard.Data.month}月を場に捨てました。");

                if (fieldView != null)
                {
                    fieldView.AddCard(discard, true);
                }

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