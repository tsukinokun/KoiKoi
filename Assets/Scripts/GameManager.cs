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
        _destroyToken = this.GetCancellationTokenOnDestroy();

        if (koiKoiChoicePanel != null) koiKoiChoicePanel.SetActive(false);

        _playerLastTotalPoints = 0;
        _enemyLastTotalPoints = 0;

        if (deckController != null)
        {
            deckController.InitializeDeck();
        }

        // 1️⃣ ここでカードが内部的にドローされて各Viewの子要素に収まる
        DealInitialCards();

        // ゲーム開始時、プレイヤーの最初の手札のエフェクトをチェック
        HighlightMatchableCards();
    }

    // GameManager.cs 内の初期配置を配っている部分
    private void DealInitialCards()
    {
        int handCount = 8;  // お互いの初期手札は8枚
        int fieldCount = 8; // 初期場札は8枚

        // 1️⃣ まずはお互いの手札を8枚ずつ交互に配る
        for (int i = 0; i < handCount; i++)
        {
            // プレイヤーへ分配
            Card playerCard = deckController.DrawCard();
            playerHandView.AddCard(playerCard, isFaceUp: true);

            // 敵へ分配
            Card enemyCard = deckController.DrawCard();
            enemyHandView.AddCard(enemyCard, isFaceUp: false);
        }

        // 2️⃣ 🌟消えていた場札を配る処理を追加！
        for (int i = 0; i < fieldCount; i++)
        {
            Card fieldCard = deckController.DrawCard();
            if (fieldView != null)
            {
                // 場札は当然、表向き(true)で配置
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
    /// 🌟2段階アニメーション：手札を一度場札の右下に重ねて（ペア認識）、そこから獲得エリアへ移動させる
    /// </summary>
    private async UniTask CollectPairAsync(Card handCard, Card fieldCard, bool isPlayer, bool shouldTriggerNextStep = true)
    {
        // --- フェーズ1: 手札（または山札）を、対象の場札の右下に重ねる ---
        if (handCard != null && fieldCard != null)
        {
            // 重ねるために一時的に親を fieldView に合わせる（worldPositionStaysはtrueでワープ防止）
            handCard.transform.SetParent(fieldView.transform, worldPositionStays: true);

            // 選択状態や光をリセット
            handCard.SetSelected(false);
            handCard.SetGlow(false);
            handCard.SetFaceUp(true);

            // 場札のローカル座標を基準に、少し「右下（X+0.15f, Y-0.15f）」かつ「手前（Z-0.05f）」の位置を計算
            Vector3 fieldLocalPos = fieldCard.transform.localPosition;
            Vector3 overlapTargetPos = new Vector3(fieldLocalPos.x + 0.15f, fieldLocalPos.y - 0.15f, fieldLocalPos.z - 0.05f);

            // 場札の回転に合わせる（基本は正面ですが、場札に傾きがある場合の保険）
            handCard.transform.localRotation = fieldCard.transform.localRotation;

            // まず、場札の右下へ滑り込ませる（演出時間は仮に0.25秒）
            float overlapDuration = 0.25f;
            handCard.MoveToLocalPositionAsync(overlapTargetPos, overlapDuration, handCard.GetCancellationTokenOnDestroy()).Forget();

            // 重なるまで少し待つ（「カツッ」と重なるタメの時間として 0.3秒）
            await UniTask.Delay(TimeSpan.FromSeconds(overlapDuration + 0.05f), cancellationToken: _destroyToken);
        }

        // --- フェーズ2: ペアが成立したので、2枚同時に獲得エリアへ送る ---
        CapturedAreaView targetCapturedView = isPlayer ? playerCapturedView : enemyCapturedView;

        // 獲得エリアへの移動を開始（ここで親子関係が各カテゴリーに切り替わり、ぬるっと移動する）
        MoveToCapturedArea(handCard, isPlayer);
        MoveToCapturedArea(fieldCard, isPlayer);

        // CapturedAreaView 側での移動完了を待つ（0.4秒）
        float duration = (targetCapturedView != null) ? targetCapturedView.MoveDuration : 0.4f;
        await UniTask.Delay(TimeSpan.FromSeconds(duration), cancellationToken: _destroyToken);

        // 次のステップに進むフラグがON（手札からペアを取った時など）であれば山札めくりへ
        if (shouldTriggerNextStep)
        {
            DrawFromDeckRoutineAsync(isPlayer).Forget();
        }
    }

    // MoveToCapturedArea はシンプルに受け渡すだけに整理
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