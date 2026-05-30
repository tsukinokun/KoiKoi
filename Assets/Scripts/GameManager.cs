using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.U2D;
using System.Linq;

public enum TurnState
{
    PlayerTurn,
    NPCTurn,
    CheckingMatch
}

public class GameManager : MonoBehaviour
{
    private TurnState _currentState = TurnState.PlayerTurn;

    public SpriteAtlas cardAtlas;
    public GameObject cardPrefab;

    public Transform playerHandParent; // プレイヤー手札の親
    public Transform enemyHandParent;  // 相手手札の親
    public FieldView fieldView;        // 🌟場札を管理するビューコンポーネント

    [Header("Player Captured Areas")]
    public Transform pHikariParent;
    public Transform pTaneParent;
    public Transform pTanParent;
    public Transform pKasuParent;

    [Header("Enemy Captured Areas")]
    public Transform eHikariParent;
    public Transform eTaneParent;
    public Transform eTanParent;
    public Transform eKasuParent;

    // これが「山札」の実体です
    private List<Card> _deck = new List<Card>();

    // 現在選択されているカードの参照
    private Card _currentSelectedCard;

    [Header("UI Managers")]
    [SerializeField] private YakuWindowManager yakuWindowManager;

    void Start()
    {
        // JSONを読み込み、48枚を生成して山札に入れる
        CreateDeck();

        // 山札をシャッフルする
        Shuffle();

        // カードを配る
        DealInitialCards();
    }

    void CreateDeck()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "JSON", "cards_master.json");
        string jsonText = File.ReadAllText(path);

        string wrappedJson = "{\"cards\":" + jsonText + "}";

        // 成形した wrappedJson を読み込む
        CardList cardList = JsonUtility.FromJson<CardList>(wrappedJson);

        Sprite backSprite = cardAtlas.GetSprite("Card_Back");

        if (cardList == null || cardList.cards == null)
        {
            Debug.LogError("JSONのパースに失敗しました。形式を確認してください。");
            return;
        }

        foreach (var data in cardList.cards)
        {
            // 生成
            GameObject go = Instantiate(cardPrefab);
            Sprite faceSprite = cardAtlas.GetSprite(data.id);
            Debug.Log(data.id);

            Card card = go.GetComponent<Card>();
            // データ、表面、裏面をセットして初期化
            card.Initialize(data, faceSprite, backSprite);

            // 山札の定位置（左側など）に移動させて裏向きにする
            go.transform.position = new Vector3(-5f, 0, 0);
            card.SetFaceUp(false);

            // リストに溜める
            _deck.Add(card);
        }
        Debug.Log($"山札に {_deck.Count} 枚準備しました。");
    }

    void Shuffle()
    {
        // フィッシャー–イェーツのシャッフル
        for (int i = _deck.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            Card temp = _deck[i];
            _deck[i] = _deck[j];
            _deck[j] = temp;
        }
        Debug.Log("シャッフル完了！");
    }

    // 最初の手札・場札を配る
    void DealInitialCards()
    {
        // 場札を配る
        for (int i = 0; i < 8; i++) transferCard(null, true, i, true);

        // プレイヤーの手札を配る
        for (int i = 0; i < 8; i++) transferCard(playerHandParent, true, i, false);

        // 相手の手札を配る（裏向き）
        for (int i = 0; i < 8; i++) transferCard(enemyHandParent, false, i, false);
    }

    // 山札から指定の場所にカードを物理的に移動させる
    void transferCard(Transform targetParent, bool isFaceUp, int index, bool isField)
    {
        if (_deck.Count == 0) return;

        Card card = _deck[_deck.Count - 1];
        _deck.RemoveAt(_deck.Count - 1);

        // 🌟場札（isField）ならFieldViewにお任せする（内部で自動ソートされます）
        if (isField)
        {
            if (fieldView != null)
            {
                fieldView.AddCard(card, isFaceUp);
            }
            return;
        }

        // 親を指定の場所に付け替える（手札用）
        card.transform.SetParent(targetParent);
        card.SetFaceUp(isFaceUp);

        if (targetParent == playerHandParent || targetParent == enemyHandParent)
        {
            // 【手札：扇形に並べる】処理
            bool isEnemy = (targetParent == enemyHandParent);
            float radius = 12.0f;
            float angleStep = 5.0f;
            float baseAngle = isEnemy ? 270.0f : 90.0f;
            float currentAngle = baseAngle + (index - 3.5f) * angleStep * (isEnemy ? 1 : -1);
            float rad = currentAngle * Mathf.Deg2Rad;
            float x = Mathf.Cos(rad) * radius;
            float y = (Mathf.Sin(rad) * radius) + (isEnemy ? radius : -radius);

            card.transform.localPosition = new Vector3(x, y, -0.01f * index);
            float rotationOffset = isEnemy ? 270.0f : 90.0f;
            card.transform.localRotation = Quaternion.Euler(0, 0, currentAngle - rotationOffset);
        }
    }

    // 獲得札エリアかどうかを判定する補助関数
    bool IsCapturedArea(Transform t)
    {
        return t == pHikariParent || t == pTaneParent || t == pTanParent || t == pKasuParent ||
               t == eHikariParent || t == eTaneParent || t == eTanParent || t == eKasuParent;
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
        if (currentParent == playerHandParent)
        {
            // ★すでにこのカードが選択されている状態でもう一度クリックされた場合（ダブルクリック扱い：場に捨てる）
            if (_currentSelectedCard == clickedCard)
            {
                Debug.Log($"手札の再クリックを検知: {clickedCard.Data.month}月を場に捨てます。");

                _currentSelectedCard = null;
                clickedCard.SetSelected(false);

                // 🌟FieldViewにカードの追加と再整列を任せる
                if (fieldView != null)
                {
                    fieldView.AddCard(clickedCard, true);
                }

                // 自分の山札めくりフェーズへ移行
                StartCoroutine(DrawFromDeckRoutine(true));
                return;
            }

            if (_currentSelectedCard != null)
            {
                _currentSelectedCard.SetSelected(false);
            }

            // 新しく手札を選択
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

            // 月が一致するかチェック
            if (_currentSelectedCard.Data.month == clickedCard.Data.month)
            {
                Debug.Log($"【手札獲得一致】{_currentSelectedCard.Data.month}月が一致しました！");

                Card hand = _currentSelectedCard;
                Card field = clickedCard;

                _currentSelectedCard = null;

                // 獲得処理と役確認が終わった後に、山札めくりフェーズを実行
                CollectPair(hand, field, true, () =>
                {
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

        if (_deck.Count == 0)
        {
            Debug.LogWarning("山札が空になりました。");
            SetNextTurn(isPlayer);
            yield break;
        }

        Card drawnCard = _deck[_deck.Count - 1];
        _deck.RemoveAt(_deck.Count - 1);

        // 🌟山札からめくったカードをFieldViewに追加（自動整列される）
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

        // 🌟獲得されて減った、あるいは不一致で増えた場札を綺麗に並べ直す
        if (fieldView != null)
        {
            fieldView.Rearrange();
        }

        yield return new WaitForSeconds(0.8f);

        SetNextTurn(isPlayer);
    }

    // ターンを交代する補助関数
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

    // ペアの獲得処理
    void CollectPair(Card handCard, Card fieldCard, bool isPlayer, System.Action onComplete)
    {
        // 獲得エリアへ移動
        MoveToCapturedArea(handCard, isPlayer);
        MoveToCapturedArea(fieldCard, isPlayer);

        // 成立したすべての役をリストで取得
        List<YakuResult> activeYakus = CheckAllYaku(isPlayer);

        // 役が1つ以上成立している場合の処理
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

    // 1枚のカードをタイプに応じた獲得エリアに移動させる
    void MoveToCapturedArea(Card card, bool isPlayer)
    {
        card.SetSelected(false);
        card.SetFaceUp(true);

        Transform targetParent = null;
        string cardType = card.Data.type.ToLower();

        if (isPlayer)
        {
            if (cardType == "hikari") targetParent = pHikariParent;
            else if (cardType == "tane") targetParent = pTaneParent;
            else if (cardType == "tan" || cardType == "tanzaku") targetParent = pTanParent;
            else targetParent = pKasuParent;
        }
        else
        {
            if (cardType == "hikari") targetParent = eHikariParent;
            else if (cardType == "tane") targetParent = eTaneParent;
            else if (cardType == "tan" || cardType == "tanzaku") targetParent = eTanParent;
            else targetParent = eKasuParent;
        }

        card.transform.SetParent(targetParent);

        int childCount = targetParent.childCount;
        card.transform.localPosition = new Vector3(childCount * 0.2f, 0, -0.01f * childCount);
        card.transform.localRotation = Quaternion.identity;
    }

    private IEnumerator NPCTurnRoutine()
    {
        Debug.Log("NPCが考えています...");
        yield return new WaitForSeconds(1.5f);

        Card npcChoice = null;
        Card fieldChoice = null;

        if (fieldView != null)
        {
            foreach (Transform npcCardTr in enemyHandParent)
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
                StartCoroutine(DrawFromDeckRoutine(false));
            });
        }
        else
        {
            if (enemyHandParent.childCount > 0)
            {
                Card discard = enemyHandParent.GetChild(0).GetComponent<Card>();
                Debug.Log($"NPCは一致する札がないため、{discard.Data.month}月を場に捨てました。");

                // 🌟NPCの捨札もFieldViewを介して追加・自動整列
                if (fieldView != null)
                {
                    fieldView.AddCard(discard, true);
                }
            }

            StartCoroutine(DrawFromDeckRoutine(false));
        }
    }

    private List<YakuResult> CheckAllYaku(bool isPlayer)
    {
        List<CardData> capturedCards = new List<CardData>();
        Transform[] targets = isPlayer
            ? new Transform[] { pHikariParent, pTaneParent, pTanParent, pKasuParent }
            : new Transform[] { eHikariParent, eTaneParent, eTanParent, eKasuParent };

        foreach (var parent in targets)
        {
            if (parent == null) continue;
            foreach (Transform child in parent)
            {
                Card card = child.GetComponent<Card>();
                if (card != null && card.Data != null)
                {
                    capturedCards.Add(card.Data);
                }
            }
        }

        return YakuEvaluator.CheckAllYaku(capturedCards);
    }
}