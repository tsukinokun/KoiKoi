using UnityEngine;

public class Card : MonoBehaviour
{
    // このカードが持っているデータ
    public CardData Data { get; private set; }

    private Sprite _faceSprite;
    private Sprite _backSprite;
    private SpriteRenderer _sr;

    private bool _isSelected = false;
    private Vector3 _originalLocalPos; // 選択解除時に戻る場所

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

    private void OnMouseDown()
    {
        // GameManagerに通知
        GameManager gm = Object.FindAnyObjectByType<GameManager>();
        if (gm != null)
        {
            gm.OnCardSelected(this);
            Debug.Log($"Card clicked: {Data.id}", this);
        }
    }

    public void SetSelected(bool selected)
    {
        if (_isSelected == selected) return;

        _isSelected = selected;

        if (_isSelected)
        {
            // 選択されたら少し上にずらす
            _originalLocalPos = transform.localPosition;
            transform.localPosition += new Vector3(0, 0.3f, 0);
            _sr.color = Color.yellow; // 視覚的に分かりやすく色を変える（任意）
        }
        else
        {
            // 解除されたら元の位置に戻す
            transform.localPosition = _originalLocalPos;
            _sr.color = Color.white;
        }
    }
}