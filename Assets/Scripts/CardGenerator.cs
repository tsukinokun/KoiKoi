using UnityEngine;
using UnityEngine.U2D; // SpriteAtlasに必要

public class CardGenerator : MonoBehaviour
{
    public SpriteAtlas cardAtlas;
    public GameObject cardPrefab; // 後ほど作るプレハブ

    void Start()
    {
        // 動作テスト：1月(松)の1枚目を生成してみる
        TestGenerate("Card_01_01");
    }

    void TestGenerate(string cardId)
    {
        // 1. アトラスからSpriteを取得
        Sprite s = cardAtlas.GetSprite(cardId);

        if (s == null)
        {
            Debug.LogError($"アトラスの中に {cardId} が見つかりません！名前を確認してください。");
            return;
        }

        // 2. プレハブからインスタンス化
        GameObject go = Instantiate(cardPrefab, Vector3.zero, Quaternion.identity);

        // 3. Cardスクリプトを通じて画像をセット
        Card card = go.GetComponent<Card>();
        card.SetVisual(s);

        Debug.Log($"<color=green>成功：</color> {cardId} の生成に成功しました！");
    }
}