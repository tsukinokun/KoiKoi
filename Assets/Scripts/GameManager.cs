using UnityEngine;
using UnityEngine.U2D;
using System.Collections.Generic;
using System.IO;

public class GameManager : MonoBehaviour
{
    public SpriteAtlas cardAtlas;
    public GameObject cardPrefab;

    public Transform playerHandParent; // プレイヤー手札の親
    public Transform enemyHandParent;  // 相手手札の親
    public Transform fieldParent;      // 場札の親

    // これが「山札」の実体です
    private List<Card> _deck = new List<Card>();

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
}