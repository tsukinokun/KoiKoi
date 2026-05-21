using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.U2D;

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
    public Transform fieldParent;      // 場札の親

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

        // あとはそのまま
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
        for (int i = 0; i < 8; i++) transferCard(fieldParent, true, i);
        // ★ここで配り終わった場札を綺麗に並べる
        RearrangeFieldCards();

        // プレイヤーの手札を配る
        for (int i = 0; i < 8; i++) transferCard(playerHandParent, true, i);

        // 相手の手札を配る（裏向き）
        for (int i = 0; i < 8; i++) transferCard(enemyHandParent, false, i);
    }

    // 場札を現在の枚数に応じてグリッド状にきれいに並べ直す関数
    void RearrangeFieldCards()
    {
        float xSpacing = 1.2f;
        float ySpacing = 1.2f;

        int index = 0;
        foreach (Transform child in fieldParent)
        {
            Card card = child.GetComponent<Card>();
            if (card == null) continue;

            int column = index % 4;
            int row = index / 4;

            float x = (column - 1.5f) * xSpacing;
            float y = (row == 0 ? (ySpacing / 2f) : -(ySpacing / 2f));

            // Z軸は重なり順がおかしくならないようにindexで少しずつ手前に出す
            card.transform.localPosition = new Vector3(x, y, -0.01f * index);
            card.transform.localRotation = Quaternion.identity;

            index++;
        }
    }

    // 山札から指定の場所にカードを物理的に移動させる
    void transferCard(Transform targetParent, bool isFaceUp, int index)
    {
        if (_deck.Count == 0) return;

        // リスト（山札）の最後から1枚取り出す
        Card card = _deck[_deck.Count - 1];
        _deck.RemoveAt(_deck.Count - 1);

        // 親を指定の場所（FieldParentなど）に付け替える
        card.transform.SetParent(targetParent);

        // 表裏をセット
        card.SetFaceUp(isFaceUp);

        if (targetParent == playerHandParent || targetParent == enemyHandParent)
        {
            // 【手札：扇形に並べる】
            bool isEnemy = (targetParent == enemyHandParent);

            float radius = 12.0f;     // 円の半径
            float angleStep = 5.0f;   // カード間の角度

            // プレイヤーは90度（真上）、敵は270度（真下）を基準にする
            float baseAngle = isEnemy ? 270.0f : 90.0f;

            // 敵の場合は並び順を反転させる
            float currentAngle = baseAngle + (index - 3.5f) * angleStep * (isEnemy ? 1 : -1);
            float rad = currentAngle * Mathf.Deg2Rad;

            float x = Mathf.Cos(rad) * radius;
            // 敵は半径分「上」へ、プレイヤーは「下」へオフセット
            float y = (Mathf.Sin(rad) * radius) + (isEnemy ? radius : -radius);

            card.transform.localPosition = new Vector3(x, y, -0.01f * index);

            // 回転：敵なら下を向くように調整
            float rotationOffset = isEnemy ? 270.0f : 90.0f;
            card.transform.localRotation = Quaternion.Euler(0, 0, currentAngle - rotationOffset);
        }
        else
        {
            // ここでは親の付け替えと表裏のセットだけ行い、
            // 座標リセットは配り終わった後の RearrangeFieldCards に任せる
            card.transform.localPosition = Vector3.zero;
            card.transform.localRotation = Quaternion.identity;
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
        // ガード1: 引数のカード自体がヌルなら処理しない
        if (clickedCard == null) return;

        // ガード2: プレイヤーのターン以外は一切のクリックを無視
        if (_currentState != TurnState.PlayerTurn) return;

        // ガード3: カードのデータがバインドされていなければ処理しない
        if (clickedCard.Data == null)
        {
            Debug.LogError($"クリックされたカード {clickedCard.name} のDataが割り当てられていません！");
            return;
        }

        Transform currentParent = clickedCard.transform.parent;

        // 【フェーズ1: 自分の手札をクリックした時】
        if (currentParent == playerHandParent)
        {
            // ★【変更】すでにこのカードが選択されている状態でもう一度クリックされた場合（ダブルクリック扱い）
            if (_currentSelectedCard == clickedCard)
            {
                Debug.Log($"手札の再クリックを検知: {clickedCard.Data.month}月を場に捨てます。");

                // 選択ポインタをクリア
                _currentSelectedCard = null;

                // カードの選択状態を解除して場に移動
                clickedCard.SetSelected(false);
                clickedCard.transform.SetParent(fieldParent);
                clickedCard.SetFaceUp(true); // 表を向ける

                // 場札を綺麗に並び替える
                RearrangeFieldCards();

                // 自分の山札めくりフェーズへ移行
                StartCoroutine(DrawFromDeckRoutine(true));
                return;
            }

            // 別の手札が選択されていたら古い方の選択を解除
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
        else if (currentParent == fieldParent && _currentSelectedCard != null)
        {
            // 安全のため、選択中の手札のデータもヌルチェック
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

                // ローカル変数に退避させてからクリア
                Card hand = _currentSelectedCard;
                Card field = clickedCard;

                _currentSelectedCard = null;

                CollectPair(hand, field, true);

                // 山札めくりフェーズへ移行
                StartCoroutine(DrawFromDeckRoutine(true));
            }
            else
            {
                Debug.LogWarning("月の違う札です。合わせられません。");
            }
        }
    }

    private IEnumerator DrawFromDeckRoutine(bool isPlayer)
    {
        // 状態を CheckingMatch にして、一時的にユーザーの入力をブロック
        _currentState = TurnState.CheckingMatch;

        yield return new WaitForSeconds(0.8f); // 前のアクションからの余韻

        if (_deck.Count == 0)
        {
            Debug.LogWarning("山札が空になりました。");
            // 本来はここでゲーム終了・集計ですが、一旦次のターンへ
            SetNextTurn(isPlayer);
            yield break;
        }

        // 山札の末尾（一番上）から1枚めくるポインタを取得
        Card drawnCard = _deck[_deck.Count - 1];
        _deck.RemoveAt(_deck.Count - 1);

        // 一旦場札の領域に仮移動させて表を向ける（「山札からめくった」視覚表現）
        drawnCard.transform.SetParent(fieldParent);
        drawnCard.SetFaceUp(true);
        Debug.Log($"山札からめくった札: {drawnCard.Data.month}月 ({drawnCard.Data.type})");

        // めくった札が場札と一致するかチェック
        Card matchedFieldCard = null;
        foreach (Transform fieldCardTr in fieldParent)
        {
            Card fieldCard = fieldCardTr.GetComponent<Card>();
            // 自分自身（たった今仮配置したdrawnCard）は除外して比較
            if (fieldCard != null && fieldCard != drawnCard)
            {
                if (drawnCard.Data.month == fieldCard.Data.month)
                {
                    matchedFieldCard = fieldCard;
                    break;
                }
            }
        }

        yield return new WaitForSeconds(1.0f); // めくったカードをプレイヤーに確認させる時間

        if (matchedFieldCard != null)
        {
            Debug.Log($"【山札めくり一致】{drawnCard.Data.month}月が場札と一致！獲得します。");
            // めくった札と場札のペアを獲得エリアへ
            CollectPair(drawnCard, matchedFieldCard, isPlayer);
        }
        else
        {
            Debug.Log($"【山札めくり不一致】一致する月がないため、場札に加えます。");
            // 一致しなかったらそのまま場札の仲間入り
            // （すでに親は fieldParent になっているので並び替えるだけでOK）
        }

        // 場札を綺麗に並び替える
        RearrangeFieldCards();

        yield return new WaitForSeconds(0.8f);

        // 次のターンへ移行
        SetNextTurn(isPlayer);
    }

    // ターンを交代する補助関数
    void SetNextTurn(bool currentIsPlayer)
    {
        if (currentIsPlayer)
        {
            // プレイヤーが引き終わったのでNPCのターンへ
            _currentState = TurnState.NPCTurn;
            StartCoroutine(NPCTurnRoutine());
        }
        else
        {
            // NPCが引き終わったのでプレイヤーのターンへ
            _currentState = TurnState.PlayerTurn;
            Debug.Log("あなたのターンです。");
        }
    }

    // ペアの獲得処理 (isPlayer: プレイヤーかNPCか)
    void CollectPair(Card handCard, Card fieldCard, bool isPlayer)
    {
        // 1. 手札だった札を獲得エリアへ
        MoveToCapturedArea(handCard, isPlayer);

        // 2. 場にあった札を獲得エリアへ
        MoveToCapturedArea(fieldCard, isPlayer);
    }

    // 1枚のカードをタイプに応じた獲得エリアに移動させる
    void MoveToCapturedArea(Card card, bool isPlayer)
    {
        // 選択状態の見た目を完全に初期化
        card.SetSelected(false);
        card.SetFaceUp(true); // 獲得札は常に表向き

        // 飛ばし先の親トランスフォームを決定する
        Transform targetParent = null;

        // 文字列のブレを考慮して小文字で判定
        string cardType = card.Data.type.ToLower();

        if (isPlayer)
        {
            if (cardType == "hikari") targetParent = pHikariParent;
            else if (cardType == "tane") targetParent = pTaneParent;
            else if (cardType == "tan") targetParent = pTanParent;
            else targetParent = pKasuParent;
        }
        else
        {
            if (cardType == "hikari") targetParent = eHikariParent;
            else if (cardType == "tane") targetParent = eTaneParent;
            else if (cardType == "tan") targetParent = eTanParent;
            else targetParent = eKasuParent;
        }

        // 親を付け替える
        card.transform.SetParent(targetParent);

        // 獲得エリア内での整列（簡易的にランダムに少しずらして重ねる、またはきれいに並べる）
        // ここでは一旦、親の中心 (0,0,0) にリセットします（後ほどUIに合わせて調整してください）
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

        foreach (Transform npcCardTr in enemyHandParent)
        {
            Card npcCard = npcCardTr.GetComponent<Card>();
            foreach (Transform fieldCardTr in fieldParent)
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

        if (npcChoice != null && fieldChoice != null)
        {
            Debug.Log($"【NPC獲得】{npcChoice.Data.month}月が一致しました。");
            CollectPair(npcChoice, fieldChoice, false);
        }
        else
        {
            if (enemyHandParent.childCount > 0)
            {
                Card discard = enemyHandParent.GetChild(0).GetComponent<Card>();
                Debug.Log($"NPCは一致する札がないため、{discard.Data.month}月を場に捨てました。");
                discard.transform.SetParent(fieldParent);
                discard.SetFaceUp(true);
                RearrangeFieldCards();
            }
        }

        // 【変更】ここで即プレイヤーに返さず、NPCの山札めくりフェーズへ進む
        StartCoroutine(DrawFromDeckRoutine(false));
    }
}