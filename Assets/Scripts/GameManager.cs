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

        // プレイヤーの手札を配る
        for (int i = 0; i < 8; i++) transferCard(playerHandParent, true, i);

        // 相手の手札を配る（裏向き）
        for (int i = 0; i < 8; i++) transferCard(enemyHandParent, false, i);
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
            // 【場札：4x2のグリッド配置】
            float xSpacing = 1.2f;
            float ySpacing = 1.2f;

            int column = index % 4;
            int row = index / 4;

            float x = (column - 1.5f) * xSpacing;
            float y = (row == 0 ? (ySpacing / 2f) : -(ySpacing / 2f));

            card.transform.localPosition = new Vector3(x, y, -0.01f * index);
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
        // プレイヤーのターン以外は一切のクリックを無視（ガード）
        if (_currentState != TurnState.PlayerTurn) return;

        Transform currentParent = clickedCard.transform.parent;

        // 【フェーズ1: 自分の手札をクリックした時】
        if (currentParent == playerHandParent)
        {
            // すでに選択されている手札があれば選択を解除
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
            // 月（Data.month）が一致するかチェック
            if (_currentSelectedCard.Data.month == clickedCard.Data.month)
            {
                Debug.Log($"【獲得一致】{_currentSelectedCard.Data.month}月が一致しました！");

                // 一致したペアの獲得処理へ
                CollectPair(_currentSelectedCard, clickedCard, true);

                // 選択ポインタをクリア
                _currentSelectedCard = null;

                // 本来はこの後「山札から1枚めくるフェーズ」に入りますが、
                // まずはターンが交互に進むかテストするため、NPCターンへ切り替えます
                _currentState = TurnState.NPCTurn;
                StartCoroutine(NPCTurnRoutine());
            }
            else
            {
                Debug.LogWarning("月の違う札です。合わせられません。");
            }
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
        yield return new WaitForSeconds(1.5f); // 1.5秒待って人間らしさを演出

        Card npcChoice = null;
        Card fieldChoice = null;

        // NPCの手札を1枚ずつ走査
        foreach (Transform npcCardTr in enemyHandParent)
        {
            Card npcCard = npcCardTr.GetComponent<Card>();

            // 場札を1枚ずつ走査
            foreach (Transform fieldCardTr in fieldParent)
            {
                Card fieldCard = fieldCardTr.GetComponent<Card>();

                // 月が一致するペアが見つかったら即決定
                if (npcCard.Data.month == fieldCard.Data.month)
                {
                    npcChoice = npcCard;
                    fieldChoice = fieldCard;
                    break;
                }
            }
            if (npcChoice != null) break;
        }

        // ペアが見つかった場合
        if (npcChoice != null && fieldChoice != null)
        {
            Debug.Log($"【NPC獲得】{npcChoice.Data.month}月が一致しました。");
            CollectPair(npcChoice, fieldChoice, false);
        }
        else
        {
            // 一致するものがなければ、NPCの手札の1枚目（インデックス0）を場に捨てる
            if (enemyHandParent.childCount > 0)
            {
                Card discard = enemyHandParent.GetChild(0).GetComponent<Card>();
                Debug.Log($"NPCは一致する札がないため、{discard.Data.month}月を場に捨てました。");

                // 場札の親へ移動
                discard.transform.SetParent(fieldParent);
                discard.SetFaceUp(true);

                // 場札の再整列ロジックが必要ですが、まずは末尾に配置
                int fieldCount = fieldParent.childCount;
                // DealInitialCardsの場札配置ロジックを流用すると綺麗ですが、一旦簡易配置
                discard.transform.localPosition = new Vector3((fieldCount % 4 - 1.5f) * 1.2f, (fieldCount / 4 == 0 ? 0.6f : -0.6f), -0.01f * fieldCount);
                discard.transform.localRotation = Quaternion.identity;
            }
        }

        yield return new WaitForSeconds(1.0f);

        // プレイヤーにターンを戻す
        _currentState = TurnState.PlayerTurn;
        Debug.Log("あなたのターンです。");
    }
}