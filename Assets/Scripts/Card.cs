using UnityEngine;

// カード1枚の「見た目」と「データ」を保持する
public class Card : MonoBehaviour
{
    private SpriteRenderer _spriteRenderer;

    void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
    }

    // アトラスから受け取ったSpriteをセットする
    public void SetVisual(Sprite sprite)
    {
        if (_spriteRenderer == null) _spriteRenderer = GetComponent<SpriteRenderer>();
        _spriteRenderer.sprite = sprite;
    }
}