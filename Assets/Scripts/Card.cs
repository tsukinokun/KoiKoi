using UnityEngine;

public class Card : MonoBehaviour
{
    // このカードが持っているデータ
    public CardData Data { get; private set; }

    private Sprite _faceSprite;
    private Sprite _backSprite;
    private SpriteRenderer _sr;

    // GameManagerから呼ばれる初期化関数
    // 引数の数と型を、GameManagerの呼び出し側(data, face, back)と合わせるのがポイントです
    public void Initialize(CardData data, Sprite face, Sprite back)
    {
        this.Data = data;
        this._faceSprite = face;
        this._backSprite = back;

        _sr = GetComponent<SpriteRenderer>();

        // 最初は山札なので裏向き
        SetFaceUp(false);
    }

    // 表裏を切り替える関数
    public void SetFaceUp(bool isFaceUp)
    {
        if (_sr == null) _sr = GetComponent<SpriteRenderer>();
        _sr.sprite = isFaceUp ? _faceSprite : _backSprite;
    }
}