using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

/// <summary>
/// 山札の生成・シャッフル・配布（ドロー）を専門に行うデータ管理コンポーネント
/// </summary>
public class DeckController : MonoBehaviour
{
    [Header("Assets")]
    [SerializeField] private SpriteAtlas cardAtlas;
    [SerializeField] private GameObject cardPrefab;

    private const string CardMasterResourcePath = "JSON/cards_master";

    // 山札の実体
    private List<Card> _deck = new List<Card>();

    /// <summary>
    /// 現在の山札の残り枚数
    /// </summary>
    public int Count => _deck.Count;

    /// <summary>
    /// JSONからカードマスターを読み込み、48枚の山札を生成してシャッフルする
    /// </summary>
    public void InitializeDeck()
    {
        // 前局で使われなかった山札のカードを破棄する（参照を消すだけだとGameObjectが残り続ける）
        foreach (Card card in _deck)
        {
            if (card != null) Destroy(card.gameObject);
        }
        _deck.Clear();

        // Resources経由で読む（StreamingAssetsのファイル読み込みはAndroid/WebGLで失敗するため）
        TextAsset json = Resources.Load<TextAsset>(CardMasterResourcePath);
        if (json == null)
        {
            Debug.LogError($"カードマスターが見つかりません: Resources/{CardMasterResourcePath}");
            return;
        }

        string wrappedJson = "{\"cards\":" + json.text + "}";

        CardList cardList = JsonUtility.FromJson<CardList>(wrappedJson);
        Sprite backSprite = cardAtlas.GetSprite("Card_Back");

        if (cardList == null || cardList.cards == null)
        {
            Debug.LogError("JSONのパースに失敗しました。形式を確認してください。");
            return;
        }

        foreach (var data in cardList.cards)
        {
            GameObject go = Instantiate(cardPrefab);
            Sprite faceSprite = cardAtlas.GetSprite(data.id);

            Card card = go.GetComponent<Card>();
            card.Initialize(data, faceSprite, backSprite);

            // 山札の初期位置（画面外など）に設定して裏向きにする
            go.transform.position = new Vector3(-5f, 0, 0);
            card.SetFaceUp(false);

            _deck.Add(card);
        }

        Debug.Log($"山札の原形を {_deck.Count} 枚生成しました。続いてシャッフルします。");
        Shuffle();
    }

    /// <summary>
    /// フィッシャー–イェーツのアルゴリズムによる山札のシャッフル
    /// </summary>
    private void Shuffle()
    {
        for (int i = _deck.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            Card temp = _deck[i];
            _deck[i] = _deck[j];
            _deck[j] = temp;
        }
        Debug.Log("山札のシャッフルが完了しました。");
    }

    /// <summary>
    /// 山札の上からカードを1枚引き、山札から削除して返す
    /// </summary>
    public Card DrawCard()
    {
        if (_deck.Count == 0)
        {
            Debug.LogWarning("山札が空です。カードを引けません。");
            return null;
        }

        int lastIndex = _deck.Count - 1;
        Card drawnCard = _deck[lastIndex];
        _deck.RemoveAt(lastIndex);

        return drawnCard;
    }
}