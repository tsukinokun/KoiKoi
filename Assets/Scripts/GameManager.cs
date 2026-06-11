using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Cysharp.Threading.Tasks; // 🌟UniTaskのインポート
using System.Threading;        // 🌟CancellationToken用

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

    // 🌟オブジェクトが破棄された（シーン遷移やゲーム終了）時に非同期処理を安全に止めるためのトークン
    private CancellationToken _destroyToken;

    void Start()
    {
        // 🌟破棄用トークンの取得
        _destroyToken = this.GetCancellationTokenOnDestroy();

        if (koiKoiChoicePanel != null) koiKoiChoicePanel.SetActive(false);

        // スコア記録をリセット
        _playerLastTotalPoints = 0;
        _enemyLastTotalPoints = 0;

        if (deckController != null)
        {
            deckController.InitializeDeck();
        }
        DealInitialCards();

        // ゲーム開始時、プレイヤーの最初の手札のエフェクトをチェック
        HighlightMatchableCards();
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

        // --- 1. プレイヤーの手札がクリックされた場合 ---
        if (playerHandView != null && currentParent == playerHandView.transform)
        {
            // すでに選択されている手札を「もう一度クリック」した場合
            if (_currentSelectedCard == clickedCard)
            {
                // 場札から同じ月のカードをすべてリストアップする
                List<Card> matchingFieldCards = new List<Card>();
                if (fieldView != null)
                {
                    foreach (Transform fieldCardTr in fieldView.transform)
                    {
                        Card fieldCard = fieldCardTr.GetComponent<Card>();
                        if (fieldCard != null && fieldCard.Data != null && fieldCard.Data.month == clickedCard.Data.month)
                        {
                            matchingFieldCards.Add(fieldCard);
                        }
                    }
                }

                // パターンA：取れるカードが「1枚だけ」の場合 → 自動でそのカードを取る
                if (matchingFieldCards.Count == 1)
                {
                    Card hand = _currentSelectedCard;
                    Card field = matchingFieldCards[0];

                    // 選択状態を解除
                    hand.SetSelected(false);
                    _currentSelectedCard = null;

                    // ペアを獲得（※この中で移動演出の待機等を行うため非同期メソッドに変更）
                    CollectPairAsync(hand, field, true).Forget(); // 🌟 Forgetで呼び出し

                    if (playerHandView != null) playerHandView.Rearrange();
                    ClearAllHandGlows();
                }
                // パターンB：取れるカードが「2枚以上」の場合 → 場札の該当カードを光らせて、クリックを待つ
                else if (matchingFieldCards.Count >= 2)
                {
                    Debug.Log($"取れるカードが {matchingFieldCards.Count} 枚あります。場札を選択してください。");

                    // 一旦すべての場札の光を消してから、対象のカードだけを光らせる
                    foreach (Transform fieldCardTr in fieldView.transform)
                    {
                        Card fc = fieldCardTr.GetComponent<Card>();
                        if (fc != null) fc.SetGlow(false);
                    }
                    foreach (Card fc in matchingFieldCards)
                    {
                        fc.SetGlow(true); // 取得可能な場札を光らせる
                    }
                }
                // パターンC：取れるカードが「ない」場合 → そのまま場札として出す
                else
                {
                    Debug.Log("取れるカードがないため、場札として出します。");
                    _currentSelectedCard = null;
                    clickedCard.SetSelected(false);

                    if (fieldView != null) fieldView.AddCard(clickedCard, true);
                    playerHandView.Rearrange();

                    clickedCard.SetGlow(false);
                    ClearAllHandGlows();

                    // 🌟コルーチンの代わりにUniTask版ルーチンを呼び出し (.Forget())
                    DrawFromDeckRoutineAsync(true).Forget();
                }
                return;
            }

            // まだ何も選択していない、または別の手札を選択した場合
            if (_currentSelectedCard != null) _currentSelectedCard.SetSelected(false);

            _currentSelectedCard = clickedCard;
            _currentSelectedCard.SetSelected(true);

            // 【親切設計】手札を1回クリックした時点で、どの場札が取れるかを光らせる
            if (fieldView != null)
            {
                foreach (Transform fieldCardTr in fieldView.transform)
                {
                    Card fc = fieldCardTr.GetComponent<Card>();
                    if (fc != null && fc.Data != null)
                    {
                        fc.SetGlow(fc.Data.month == clickedCard.Data.month);
                    }
                }
            }
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

                // 🌟獲得演出付きの非同期メソッドを起動
                CollectPairAsync(hand, field, true).Forget();

                if (playerHandView != null) playerHandView.Rearrange();

                // 場札の光をすべてリセット
                foreach (Transform fieldCardTr in fieldView.transform)
                {
                    Card fc = fieldCardTr.GetComponent<Card>();
                    if (fc != null) fc.SetGlow(false);
                }
                ClearAllHandGlows();
            }
        }
    }

    /// <summary>
    /// 🌟コルーチンから変更：山札からめくる非同期ルーチン
    /// </summary>
    private async UniTaskVoid DrawFromDeckRoutineAsync(bool isPlayer)
    {
        _currentState = TurnState.CheckingMatch;

        // 🌟 yield return new WaitForSeconds の代わり
        await UniTask.Delay(TimeSpan.FromSeconds(0.8f), cancellationToken: _destroyToken);

        if (deckController == null || deckController.Count == 0)
        {
            Debug.LogWarning("山札が空になりました。");
            SetNextTurn(isPlayer);
            return;
        }

        Card drawnCard = deckController.DrawCard();

        // 🌟演出のために、めくったカードを一瞬「山札の位置」に固定してから場へ補間移動させる場合：
        // Vector3 deckPos = deckController.transform.position; // (山札の座標がある場合)
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
            // 🌟めくり札と場札が一致した時も、演出完了を待ってから次に進む
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

        // 🌟 yield return new WaitUntil の代わり
        await UniTask.WaitUntil(() => isYakuFlowDone, cancellationToken: _destroyToken);

        SetNextTurn(isPlayer);
    }

    void SetNextTurn(bool currentIsPlayer)
    {
        if (currentIsPlayer)
        {
            _currentState = TurnState.NPCTurn;
            ClearAllHandGlows();

            // 🌟コルーチンからUniTask版へ変更
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
    /// 🌟新規非同期メソッド：2枚のカードを札エリアへ滑らかに移動させ、完了したら次のステップ（山札めくり等）を呼ぶ
    /// </summary>
    private async UniTask CollectPairAsync(Card handCard, Card fieldCard, bool isPlayer, bool shouldTriggerNextStep = true)
    {
        // 1. 各自の獲得先Viewの目標位置を取得する
        // (※現在、各ViewのAddCardが内部で即時座標を確定させていると仮定し、移動先座標を特定します)
        CapturedAreaView targetCapturedView = isPlayer ? playerCapturedView : enemyCapturedView;

        // 💡もしAddCardする前に移動させたい場合、一旦AddCardを呼び出して配置先を確定させてから
        // 元の座標に戻してスライドさせる（手順2のアプローチ）を行います。

        // 現状は安全に、移動処理を並行して実行して完了を待ちます
        // (各ViewのAddCardの中で即時SetParentされてジャンプしている場合、View側を後述のように調整するとより綺麗になります)
        MoveToCapturedArea(handCard, isPlayer);
        MoveToCapturedArea(fieldCard, isPlayer);

        // 🌟演出時間を仮に 0.4 秒として、2枚同時に移動完了するのをきっちり待つ
        // (もし Card.MoveToPositionAsync を直接 await する場合は、View側の即時配置と競合しないよう調整が必要です)
        await UniTask.Delay(TimeSpan.FromSeconds(0.4f), cancellationToken: _destroyToken);

        // 次のステップに進むフラグがON（手札からペアを取った時など）であれば山札めくりへ
        if (shouldTriggerNextStep)
        {
            DrawFromDeckRoutineAsync(isPlayer).Forget();
        }
    }

    void MoveToCapturedArea(Card card, bool isPlayer)
    {
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

    private int _tempCurrentPoints = 0;

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

    /// <summary>
    /// 🌟コルーチンから変更：NPCの思考非同期ルーチン
    /// </summary>
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
            // 🌟NPCがペアを獲得した時も、演出を挟んでから山札めくりへ進む
            await CollectPairAsync(npcChoice, fieldChoice, false);
            if (enemyHandView != null) enemyHandView.Rearrange();
        }
        else
        {
            if (enemyHandView != null && enemyHandView.transform.childCount > 0)
            {
                Card discard = enemyHandView.transform.GetChild(0).GetComponent<Card>();
                if (fieldView != null) fieldView.AddCard(discard, true);
                enemyHandView.Rearrange();

                // 🌟NPCが手札を場に「捨てる」時の一瞬の待機（演出時間を加味）
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